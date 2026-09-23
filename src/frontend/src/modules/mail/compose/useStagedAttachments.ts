import i18next from 'i18next'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { api, uploadAttachment } from '../../../api.js'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'

export interface StagedItem {
  key: string
  id: string | null
  fileName: string
  size: number
  progress: number
  error: string | null
  /** The mailbox holding the staged bytes — the only one a release of them can reach. */
  accountId: string
}

let nextKey = 0

// Staged files are namespaced by account. `initial` seeds parts already staged (a forward's), and
// `inlineIds` live in the body: never shown here, yet released with the tray.
export function useStagedAttachments(
  accountId: string,
  initial: { id: string; fileName: string; size: number }[] = [],
  inlineIds: string[] = [],
) {
  const [items, setItems] = useState<StagedItem[]>(() => initial.map(item => ({
    key: `staged-${nextKey++}`, id: item.id, fileName: item.fileName, size: item.size,
    progress: 1, error: null, accountId,
  })))
  const itemsRef = useRef(items)
  // Each id keeps the account it was staged under: a release aimed at whatever account is current
  // when the composer closes would leave the real owner's files to the TTL sweeper, in silence.
  const inlineRef = useRef(inlineIds.map(id => ({ id, accountId })))
  // Ids the tray has taken over. The sync below must never resurrect one: the caller keeps
  // handing in the seed it mounted on, which cannot know an id has become a plain attachment —
  // and a resurrected id is adopted twice, then released twice.
  const adoptedRef = useRef(new Set<string>())
  // Inline ids the composer inserted, kept apart from inlineRef: the effect below rebuilds that
  // one wholesale from the seed prop, which knows nothing about an insertion.
  const addedInlineRef = useRef<{ id: string; accountId: string }[]>([])
  const discardedRef = useRef(false)
  // A passive sync is enough: the inline ids come from the seed and cannot change between a render and
  // the next handler. A known id keeps its recorded account, or a switch would rewrite its owner.
  useEffect(() => {
    const known = new Map(inlineRef.current.map(entry => [entry.id, entry]))
    inlineRef.current = inlineIds
      .filter(id => !adoptedRef.current.has(id))
      .map(id => known.get(id) ?? { id, accountId })
  }, [inlineIds, accountId])

  // Keeps the ref authoritative at every instant, not just after the next
  // passive-effect flush — an event handler can run in that gap.
  const apply = useCallback((next: (previous: StagedItem[]) => StagedItem[]) => {
    itemsRef.current = next(itemsRef.current)
    setItems(itemsRef.current)
  }, [])

  const release = useCallback((id: string, owner: string) => {
    api.deleteAttachment(id, { accountId: owner }).catch(() => { /* sweeper's problem now */ })
  }, [])

  // Synchronous, since a discard can run before the next passive flush; an upload landing after the
  // discard is released on the spot rather than joining a list nobody reads again.
  const addInline = useCallback((id: string) => {
    if (discardedRef.current) { release(id, accountId); return }
    addedInlineRef.current = [...addedInlineRef.current, { id, accountId }]
  }, [accountId, release])

  const patch = useCallback((key: string, change: Partial<StagedItem>) => {
    apply(previous => {
      const index = previous.findIndex(item => item.key === key)
      if (index === -1) return previous
      const next = previous.slice()
      next[index] = { ...next[index]!, ...change }
      return next
    })
  }, [apply])

  const addFiles = useCallback((files: FileList | File[]) => {
    for (const file of Array.from(files)) {
      const key = `staged-${nextKey++}`
      apply(previous => [...previous, {
        key, id: null, fileName: file.name, size: file.size, progress: 0, error: null, accountId,
      }])
      uploadAttachment(file,
        { accountId, onProgress: (ratio: number) => patch(key, { progress: ratio }) })
        .then((info: { id: string; size: number }) => patch(key, { id: info.id, size: info.size, progress: 1 }))
        .catch((error: Error) =>
          patch(key, { error: apiErrorMessage(error, i18next.t('compose:attachments.uploadFailed')), progress: 1 }))
    }
  }, [apply, patch, accountId])

  const remove = useCallback((key: string) => {
    const item = itemsRef.current.find(i => i.key === key)
    if (item?.id) release(item.id, item.accountId)
    apply(previous => previous.filter(i => i.key !== key))
  }, [apply, release])

  // The body can no longer show an inline part, so it becomes an attachment. `known` names the files
  // from the caller's seed. Idempotent.
  const adoptInline = useCallback((known: { id: string; fileName: string; size: number }[]) => {
    const moving = [...inlineRef.current, ...addedInlineRef.current]
    if (moving.length === 0) return
    inlineRef.current = []
    addedInlineRef.current = []
    for (const entry of moving) adoptedRef.current.add(entry.id)
    const byId = new Map(known.map(entry => [entry.id, entry]))
    apply(previous => [...previous, ...moving.map(entry => ({
      key: `staged-${nextKey++}`,
      id: entry.id,
      fileName: byId.get(entry.id)?.fileName ?? 'attachment',
      size: byId.get(entry.id)?.size ?? 0,
      progress: 1,
      error: null,
      accountId: entry.accountId,
    }))])
  }, [apply])

  const discardAll = useCallback(() => {
    discardedRef.current = true
    for (const inline of [...inlineRef.current, ...addedInlineRef.current]) release(inline.id, inline.accountId)
    for (const item of itemsRef.current) {
      if (item.id) release(item.id, item.accountId)
    }
    apply(() => [])
  }, [apply, release])

  const ids = useMemo(
    () => items.reduce<string[]>((acc, item) => { if (item.id !== null) acc.push(item.id); return acc }, []),
    [items],
  )

  return {
    items, addFiles, remove, discardAll, adoptInline, addInline,
    uploading: items.some(item => item.id === null && item.error === null),
    ids,
  }
}
