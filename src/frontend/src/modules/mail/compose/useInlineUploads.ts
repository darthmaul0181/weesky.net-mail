import { useCallback, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { Toast } from '../../../hooks/useToasts'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import { stagedAttachmentUrl, uploadAttachment } from '../../../api.js'
import type { EditorHandle } from './SquireEditor'

/** The single predicate for "goes in the body", so the drop overlay and routeFiles cannot drift. */
export const isImage = (type: string) => type.startsWith('image/')

export type InsertedInline = { id: string; fileName: string; size: number }

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
  const insertImages = useCallback(async (files: File[]) => {
    for (const file of files) {
      setInlineUploads(n => n + 1)
      try {
        const info = await uploadAttachment(file, { accountId, inline: true })
        addInline(info.id)
        setInsertedInline(previous => [...previous, info])
        editor?.insertImage(stagedAttachmentUrl(info.id, accountId))
        // Only once something is actually in the body: a refused upload leaves nothing to lose,
        // and a composer dirtied by it would still ask to save on the way out.
        markDirty()
      } catch (error) {
        onNotify(apiErrorMessage(error, t('toast.imageFailed')), 'error')
      } finally {
        setInlineUploads(n => n - 1)
      }
    }
  }, [markDirty, accountId, addInline, setInsertedInline, editor, onNotify, t])

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
