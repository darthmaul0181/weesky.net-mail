import { useCallback, useMemo, useRef, useState } from 'react'
import { api, uploadAttachment } from '../../../api.js'

export interface StagedItem {
  key: string
  id: string | null
  fileName: string
  size: number
  progress: number
  error: string | null
}

let nextKey = 0

/** `initial` seeds parts the backend already staged (a forward's attachments): uploaded, done. */
export function useStagedAttachments(initial: { id: string; fileName: string; size: number }[] = []) {
  const [items, setItems] = useState<StagedItem[]>(() => initial.map(item => ({
    key: `staged-${nextKey++}`, id: item.id, fileName: item.fileName, size: item.size, progress: 1, error: null,
  })))
  const itemsRef = useRef(items)

  // Keeps the ref authoritative at every instant, not just after the next
  // passive-effect flush — an event handler can run in that gap.
  const apply = useCallback((next: (previous: StagedItem[]) => StagedItem[]) => {
    itemsRef.current = next(itemsRef.current)
    setItems(itemsRef.current)
  }, [])

  const patch = useCallback((key: string, change: Partial<StagedItem>) => {
    apply(previous => {
      const index = previous.findIndex(item => item.key === key)
      if (index === -1) return previous
      const next = previous.slice()
      next[index] = { ...next[index], ...change }
      return next
    })
  }, [apply])

  const addFiles = useCallback((files: FileList | File[]) => {
    for (const file of Array.from(files)) {
      const key = `staged-${nextKey++}`
      apply(previous => [...previous, {
        key, id: null, fileName: file.name, size: file.size, progress: 0, error: null,
      }])
      uploadAttachment(file, { onProgress: (ratio: number) => patch(key, { progress: ratio }) })
        .then((info: { id: string; size: number }) => patch(key, { id: info.id, size: info.size, progress: 1 }))
        .catch((error: Error) => patch(key, { error: error.message, progress: 1 }))
    }
  }, [apply, patch])

  const remove = useCallback((key: string) => {
    const item = itemsRef.current.find(i => i.key === key)
    if (item?.id) api.deleteAttachment(item.id).catch(() => { /* sweeper's problem now */ })
    apply(previous => previous.filter(i => i.key !== key))
  }, [apply])

  const discardAll = useCallback(() => {
    for (const item of itemsRef.current) {
      if (item.id) api.deleteAttachment(item.id).catch(() => { /* sweeper's problem now */ })
    }
    apply(() => [])
  }, [apply])

  const ids = useMemo(
    () => items.reduce<string[]>((acc, item) => { if (item.id !== null) acc.push(item.id); return acc }, []),
    [items],
  )

  return {
    items, addFiles, remove, discardAll,
    uploading: items.some(item => item.id === null && item.error === null),
    ids,
  }
}
