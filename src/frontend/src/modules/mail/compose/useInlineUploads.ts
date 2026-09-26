import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { Toast } from '../../../hooks/useToasts'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import { api, stagedAttachmentUrl, uploadAttachment } from '../../../api.js'
import type { StagedAttachmentInfo } from '../api/mailTypes'
import type { EditorHandle } from './SquireEditor'

/** The single predicate for "goes in the body", so the drop overlay and routeFiles cannot drift. */
export const isImage = (type: string) => type.startsWith('image/')

export type InsertedInline = { id: string; fileName: string; size: number }

const INLINE_UPLOAD_CONCURRENCY = 3

// At most `limit` tasks run at once, started in call order; each settles rather than rejects, so
// a caller awaiting them one by one never leaves a rejection unhandled.
function createLimiter(limit: number) {
  let active = 0
  const queue: (() => void)[] = []
  return <R>(task: () => Promise<R>) => new Promise<PromiseSettledResult<R>>(settle => {
    const start = () => {
      active++
      task()
        .then(value => settle({ status: 'fulfilled', value }), (reason: unknown) => settle({ status: 'rejected', reason }))
        .finally(() => { active--; queue.shift()?.() })
    }
    if (active < limit) start()
    else queue.push(start)
  })
}

interface Args {
  accountId: string
  plainText: boolean
  editor: EditorHandle | null
  addFiles: (files: File[]) => void
  addInline: (id: string) => void
  setInsertedInline: React.Dispatch<React.SetStateAction<InsertedInline[]>>
  markDirty: () => void
  onNotify: (message: string, kind?: Toast['type']) => void
}

/** Images pasted, dropped or picked for the body: uploaded as inline parts, then inserted. */
export function useInlineUploads(
  { accountId, plainText, editor, addFiles, addInline, setInsertedInline, markDirty, onNotify }: Args,
) {
  const { t } = useTranslation('compose')
  // An inline upload never reaches the tray, so attachments.uploading cannot see it. Until it
  // resolves there is no id for a send to carry, for an adoption to move, or for a discard to
  // release, which is why everything that consumes those ids waits on this count.
  const [inlineUploads, setInlineUploads] = useState(0)
  // Closing the composer aborts every upload, queued ones included, and nothing lands after it.
  const lifetime = useRef<AbortController | null>(null)
  useEffect(() => {
    const controller = new AbortController()
    lifetime.current = controller
    return () => controller.abort()
  }, [])
  const [limit] = useState(() => createLimiter(INLINE_UPLOAD_CONCURRENCY))
  const insertTail = useRef<Promise<void>>(Promise.resolve())

  const insertInOrder = useCallback(async (uploads: Promise<PromiseSettledResult<StagedAttachmentInfo>>[], signal: AbortSignal) => {
    for (const [index, upload] of uploads.entries()) {
      const result = await upload
      if (signal.aborted) {
        for (const left of uploads.slice(index)) {
          void left.then(r => { if (r.status === 'fulfilled') api.deleteAttachment(r.value.id, { accountId }).catch(() => undefined) })
        }
        return
      }
      setInlineUploads(n => n - 1)
      if (result.status === 'rejected') {
        onNotify(apiErrorMessage(result.reason, t('toast.imageFailed')), 'error')
        continue
      }
      const info = result.value
      addInline(info.id)
      setInsertedInline(previous => [...previous, info])
      editor?.insertImage(stagedAttachmentUrl(info.id, accountId))
      // Only once something is actually in the body: a refused upload leaves nothing to lose,
      // and a composer dirtied by it would still ask to save on the way out.
      markDirty()
    }
  }, [markDirty, accountId, addInline, setInsertedInline, editor, onNotify, t])

  // Uploaded a few at a time, inserted in paste order whatever order they finish in: each paste's
  // insertions wait for the previous paste's. One that landed after the close is released.
  const insertImages = useCallback((files: File[]) => {
    const signal = lifetime.current?.signal
    if (!signal) return Promise.resolve()
    setInlineUploads(n => n + files.length)
    const uploads = files.map(file => limit(() => uploadAttachment(file, { accountId, inline: true, signal })))
    const run = insertTail.current.then(() => insertInOrder(uploads, signal))
    insertTail.current = run.catch(() => undefined)
    return run
  }, [accountId, limit, insertInOrder])

  // An image goes in the body, anything else in the tray. Plain text has no body to put one in,
  // so there everything is an attachment.
  const routeFiles = useCallback((files: File[]) => {
    if (plainText) { addFiles(files); return }
    const images = files.filter(file => isImage(file.type))
    const rest = files.filter(file => !isImage(file.type))
    if (rest.length > 0) addFiles(rest)
    if (images.length > 0) void insertImages(images)
  }, [plainText, addFiles, insertImages])

  return { inlineUploads, routeFiles }
}
