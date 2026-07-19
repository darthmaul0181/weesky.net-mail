import { useEffect, useState } from 'react'
import { mailAttachmentUrl, requestBlob } from '../../../api.js'
import PaperclipIcon from '../../../icons/PaperclipIcon'
import { useMessage } from '../queries'
import { formatReaderDate } from './formatReaderDate'
import { formatSize } from './formatSize'
import { revealBlockedImages, sanitizeBody } from './sanitizeBody'

interface Props {
  folderPath: string | null
  uid: number | null
}

export default function MessageReader({ folderPath, uid }: Props) {
  const { data, isLoading, isError } = useMessage(folderPath, uid)
  const [imagesShown, setImagesShown] = useState(false)
  const [downloadError, setDownloadError] = useState<string | null>(null)

  // Consent is per message and never carried to the next one.
  useEffect(() => {
    setImagesShown(false)
    setDownloadError(null)
  }, [folderPath, uid])

  if (uid === null) return <p className="mail-empty">Select a message</p>
  if (isLoading) return <p className="mail-empty">Loading message…</p>
  if (isError || !data) return <p className="mail-empty">Could not load this message.</p>

  const attachments = data.attachments.filter(attachment => !attachment.isInline)
  const body = sanitizeBody(imagesShown ? revealBlockedImages(data.htmlBody) : data.htmlBody)

  async function download(part: string, fileName: string) {
    setDownloadError(null)
    try {
      const result = await requestBlob(mailAttachmentUrl(folderPath!, uid!, part))

      const url = URL.createObjectURL(result.blob)
      const link = document.createElement('a')
      link.href = url
      link.download = result.fileName || fileName
      link.click()
      URL.revokeObjectURL(url)
    } catch (error) {
      setDownloadError(error instanceof Error ? error.message : 'Could not download the attachment')
    }
  }

  return (
    <article>
      <header className="reader-header">
        <h1 className="reader-subject">{data.subject || '(no subject)'}</h1>
        <div className="reader-meta">
          <span>{data.fromName ? `${data.fromName} <${data.fromAddress}>` : data.fromAddress}</span>
          <span>{formatReaderDate(data.date)}</span>
          {data.to.length > 0 && <span>To: {data.to.join(', ')}</span>}
          {data.cc.length > 0 && <span>Cc: {data.cc.join(', ')}</span>}
        </div>
      </header>

      {data.blockedImageCount > 0 && !imagesShown && (
        <div className="reader-blocked-images">
          <span>
            {data.blockedImageCount} remote image{data.blockedImageCount > 1 ? 's were' : ' was'} blocked.
            Loading them tells the sender you opened this message.
          </span>
          <button type="button" className="btn" onClick={() => setImagesShown(true)}>Show images</button>
        </div>
      )}

      {data.htmlBody ? (
        // Three independent barriers: the backend sanitised this, DOMPurify sanitised it again
        // with a different parser, and this iframe can neither run scripts nor reach our
        // origin. Message HTML is never rendered into the page itself.
        //
        // The two popup permissions are what make links work at all. A fully empty sandbox
        // withholds every capability including navigation, so target="_blank" anchors — which
        // is what the sanitiser rewrites every link into — silently do nothing on click. The
        // escape clause matters as much as the popup one: without it the opened tab inherits
        // this sandbox and the destination site loads scriptless and broken. Neither grants
        // allow-scripts or allow-same-origin, so the message body itself stays inert.
        <iframe
          className="reader-body"
          sandbox="allow-popups allow-popups-to-escape-sandbox"
          title="Message body"
          srcDoc={body}
        />
      ) : (
        <div className="reader-text">{data.textBody}</div>
      )}

      {downloadError && <div className="reader-blocked-images">{downloadError}</div>}

      {attachments.length > 0 && (
        <div className="reader-attachments">
          {attachments.map(attachment => (
            <button
              key={attachment.part}
              type="button"
              className="attachment-chip"
              onClick={() => download(attachment.part, attachment.fileName)}
            >
              <PaperclipIcon size={13} />
              {attachment.fileName}
              <span className="attachment-chip-size">{formatSize(attachment.size)}</span>
            </button>
          ))}
        </div>
      )}
    </article>
  )
}
