import { useEffect, useState } from 'react'
import { requestBlob } from '../../../api.js'
import LoadingBlock from '../../../components/LoadingBlock'
import ChevronLeftIcon from '../../../icons/ChevronLeftIcon'
import ChevronRightIcon from '../../../icons/ChevronRightIcon'
import { formatSize } from './formatSize'

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

/**
 * Image attachment preview. Fetches through requestBlob because the API cookie is Lax and
 * cross-origin: a plain <img src> at the API would go out without it. The object URL lives
 * as long as the shown image and is revoked on navigation and on close. Arrows and the
 * keyboard's ArrowLeft/ArrowRight move through the message's images; the ends do not wrap.
 */
export default function AttachmentViewerModal({ images, initialIndex, onDownload, onClose }: Props) {
  const [index, setIndex] = useState(initialIndex)
  const [objectUrl, setObjectUrl] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const image = images[index]
  const several = images.length > 1

  useEffect(() => {
    let url: string | null = null
    let cancelled = false
    setError(null)
    setObjectUrl(null)
    requestBlob(image.src)
      .then((result: { blob: Blob }) => {
        if (cancelled) return
        url = URL.createObjectURL(result.blob)
        setObjectUrl(url)
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load the image')
      })
    return () => {
      cancelled = true
      if (url) URL.revokeObjectURL(url)
    }
  }, [image.src])

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
      // Clamped, never wrapped — same ends as the disabled arrows.
      if (event.key === 'ArrowLeft') setIndex(i => Math.max(0, i - 1))
      if (event.key === 'ArrowRight') setIndex(i => Math.min(images.length - 1, i + 1))
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onClose, images.length])

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal attachment-viewer" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">{image.fileName}</span>
          {several && <span className="attachment-viewer-count">{index + 1} / {images.length}</span>}
          <span className="attachment-viewer-size">{formatSize(image.size)}</span>
          <button className="modal-close" aria-label="Close" onClick={onClose}>✕</button>
        </div>
        <div className="attachment-viewer-body">
          {several && (
            <button type="button" className="attachment-viewer-nav" aria-label="Previous image"
              disabled={index === 0} onClick={() => setIndex(i => Math.max(0, i - 1))}>
              <ChevronLeftIcon size={18} />
            </button>
          )}
          <div className="attachment-viewer-stage">
            {error
              ? <span className="attachment-viewer-error" role="alert">{error}</span>
              : objectUrl
                ? <img src={objectUrl} alt={image.fileName} className="attachment-viewer-img" />
                : <LoadingBlock />}
          </div>
          {several && (
            <button type="button" className="attachment-viewer-nav" aria-label="Next image"
              disabled={index === images.length - 1}
              onClick={() => setIndex(i => Math.min(images.length - 1, i + 1))}>
              <ChevronRightIcon size={18} />
            </button>
          )}
        </div>
        <div className="modal-actions">
          <button type="button" className="btn btn-ghost" onClick={() => onDownload(image)}>Download</button>
        </div>
      </div>
    </div>
  )
}
