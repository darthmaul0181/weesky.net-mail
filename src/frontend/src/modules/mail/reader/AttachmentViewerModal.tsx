import { useEffect, useState, type KeyboardEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { requestBlob } from '../../../api.js'
import LoadingBlock from '../../../components/LoadingBlock'
import Modal from '../../../components/Modal'
import ChevronLeftIcon from '../../../icons/ChevronLeftIcon'
import ChevronRightIcon from '../../../icons/ChevronRightIcon'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import { formatSize } from './formatSize'
import { useKeyedState } from '../../../hooks/useKeyedState'

export interface ViewerImage {
  /** IMAP body part id — the caller's key back to the attachment for a download. */
  part: string
  /** Authenticated API URL of the part (mailAttachmentUrl output — the caller builds it). */
  src: string
  fileName: string
  size: number
}

interface Props {
  /** The message's image attachments, in row order; the viewer navigates within them. */
  images: ViewerImage[]
  initialIndex: number
  onDownload: (image: ViewerImage) => void
  onClose: () => void
}

// requestBlob because the API cookie is Lax and cross-origin: a plain <img src> would go out
// without it. The download is aborted and the object URL revoked on navigation and on close;
// the arrows do not wrap.
export default function AttachmentViewerModal({ images, initialIndex, onDownload, onClose }: Props) {
  const { t } = useTranslation('mail')
  const [index, setIndex] = useState(initialIndex)
  // index is clamped to [0, images.length - 1] by onArrow and the nav buttons below, and the
  // caller only opens this modal for a non-empty `images` and a valid `initialIndex` into it.
  const image = images[index]
  const src = image?.src
  const several = images.length > 1
  // The failure, not its wording: worded here the effect would close over `t` and refetch the
  // bytes on a language change. Translated at render, so it follows the language on its own.
  const [loaded, setLoaded] =
    useKeyedState<{ objectUrl?: string; error?: unknown }>(() => ({}), src ?? '')
  const { objectUrl, error } = loaded

  useEffect(() => {
    if (!src) return undefined
    let url: string | null = null
    let cancelled = false
    const controller = new AbortController()
    requestBlob(src, { signal: controller.signal })
      .then((result: { blob: Blob }) => {
        if (cancelled) return
        url = URL.createObjectURL(result.blob)
        setLoaded({ objectUrl: url })
      })
      .catch((err: unknown) => {
        if (!cancelled) setLoaded({ error: err })
      })
    return () => {
      cancelled = true
      controller.abort()
      if (url) URL.revokeObjectURL(url)
    }
  }, [src, setLoaded])

  // Bound to the dialog's own box rather than to the document: a viewer under another dialog
  // never sees these, and Escape stays the layer stack's. Clamped, never wrapped — the same
  // ends as the disabled arrows.
  function onArrow(event: KeyboardEvent<HTMLElement>) {
    if (event.key === 'ArrowLeft') setIndex(i => Math.max(0, i - 1))
    if (event.key === 'ArrowRight') setIndex(i => Math.min(images.length - 1, i + 1))
  }

  if (!image) return null

  return (
    <Modal
      className="attachment-viewer"
      title={image.fileName}
      onClose={onClose}
      onKeyDown={onArrow}
      headerExtra={(
        <>
          {several && <span className="attachment-viewer-count">{index + 1} / {images.length}</span>}
          <span className="attachment-viewer-size">{formatSize(image.size)}</span>
        </>
      )}
    >
      <div className="attachment-viewer-body">
        {several && (
          <button type="button" className="attachment-viewer-nav" aria-label={t('viewer.previous')}
            disabled={index === 0} onClick={() => setIndex(i => Math.max(0, i - 1))}>
            <ChevronLeftIcon size={18} />
          </button>
        )}
        <div className="attachment-viewer-stage">
          {error
            ? (
              <span className="attachment-viewer-error" role="alert">
                {apiErrorMessage(error, t('viewer.loadFailed'))}
              </span>
            )
            : objectUrl
              ? <img src={objectUrl} alt={image.fileName} className="attachment-viewer-img" />
              : <LoadingBlock />}
        </div>
        {several && (
          <button type="button" className="attachment-viewer-nav" aria-label={t('viewer.next')}
            disabled={index === images.length - 1}
            onClick={() => setIndex(i => Math.min(images.length - 1, i + 1))}>
            <ChevronRightIcon size={18} />
          </button>
        )}
      </div>
      <div className="modal-actions">
        <button type="button" className="btn btn-ghost" onClick={() => onDownload(image)}>{t('reader.download')}</button>
      </div>
    </Modal>
  )
}
