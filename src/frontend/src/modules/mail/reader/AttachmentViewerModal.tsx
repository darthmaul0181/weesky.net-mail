import { useEffect, useState } from 'react'
import { requestBlob } from '../../../api.js'
import { formatSize } from './formatSize'

interface Props {
  /** Authenticated API URL of the part (mailAttachmentUrl output — the caller builds it). */
  src: string
  fileName: string
  size: number
  onDownload: () => void
  onClose: () => void
}

/**
 * Image attachment preview. Fetches through requestBlob because the API cookie is Lax and
 * cross-origin: a plain <img src> at the API would go out without it. The object URL lives
 * as long as the modal and is revoked on close.
 */
export default function AttachmentViewerModal({ src, fileName, size, onDownload, onClose }: Props) {
  const [objectUrl, setObjectUrl] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let url: string | null = null
    let cancelled = false
    setError(null)
    setObjectUrl(null)
    requestBlob(src)
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
  }, [src])

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onClose])

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal attachment-viewer" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">{fileName}</span>
          <span className="attachment-viewer-size">{formatSize(size)}</span>
          <button className="modal-close" aria-label="Close" onClick={onClose}>✕</button>
        </div>
        <div className="attachment-viewer-body">
          {error
            ? <span className="attachment-viewer-error" role="alert">{error}</span>
            : objectUrl
              ? <img src={objectUrl} alt={fileName} className="attachment-viewer-img" />
              : <span>Loading…</span>}
        </div>
        <div className="modal-actions">
          <button type="button" className="btn btn-ghost" onClick={onDownload}>Download</button>
        </div>
      </div>
    </div>
  )
}
