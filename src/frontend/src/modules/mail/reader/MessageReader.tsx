import { useEffect, useState } from 'react'
import { mailAttachmentUrl, requestBlob } from '../../../api.js'
import { useTheme } from '../../../contexts/ThemeContext'
import PaperclipIcon from '../../../icons/PaperclipIcon'
import ChevronRightIcon from '../../../icons/ChevronRightIcon'
import { useMessage } from '../queries'
import { alwaysShowImagesOf, showSpamScoreOf, usePreferences } from '../../../hooks/usePreferences'
import { formatReaderDate } from './formatReaderDate'
import AddressLabel, { AddressList } from './AddressLabel'
import AuthBadge from './AuthBadge'
import SpamGauge from './SpamGauge'
import ReaderActions from './ReaderActions'
import ReaderDetails from './ReaderDetails'
import { isWebUnsubscribe } from './unsubscribeLink'
import { formatSize } from './formatSize'
import { darkenColours } from './darkenColours'
import { renderBodyDocument, revealBlockedImages, sanitizeBody } from './sanitizeBody'

interface Props {
  folderPath: string | null
  uid: number | null
}

export default function MessageReader({ folderPath, uid }: Props) {
  const { data, isLoading, isError } = useMessage(folderPath, uid)
  const { isDark } = useTheme()
  const { data: preferences } = usePreferences()
  const [imagesShown, setImagesShown] = useState(false)
  const [originalColours, setOriginalColours] = useState(false)
  const [downloadError, setDownloadError] = useState<string | null>(null)
  const [detailsOpen, setDetailsOpen] = useState(false)

  // Consent is per message and never carried to the next one. So is the colour choice: a mail
  // that recolours badly says nothing about the next one.
  useEffect(() => {
    setImagesShown(false)
    setOriginalColours(false)
    setDownloadError(null)
    setDetailsOpen(false)
  }, [folderPath, uid])

  if (uid === null) return <p className="mail-empty">Select a message</p>
  if (isLoading) return <p className="mail-empty">Loading message…</p>
  if (isError || !data) return <p className="mail-empty">Could not load this message.</p>

  const attachments = data.attachments.filter(attachment => !attachment.isInline)
  const unsubscribe = isWebUnsubscribe(data.unsubscribeUrl) ? data.unsubscribeUrl : null
  const inverted = isDark && !originalColours
  const showImages = imagesShown || (!!preferences && alwaysShowImagesOf(preferences))
  // Recolour before sanitising, so everything darkenColours writes faces the same pass as the
  // rest — the same reason revealBlockedImages runs on this side of it.
  const revealed = showImages ? revealBlockedImages(data.htmlBody) : data.htmlBody
  const body = sanitizeBody(inverted ? darkenColours(revealed) : revealed)

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
        <div className="reader-stack">
          <h1 className="reader-subject">{data.subject || '(no subject)'}</h1>
          <div className="reader-meta">
            <div className="reader-from">
              <AddressLabel sender name={data.fromName} address={data.fromAddress} />
              <AuthBadge authentication={data.authentication} />
              <span className="reader-date">({formatReaderDate(data.date)})</span>
              <button
                type="button"
                className={`details-toggle${detailsOpen ? ' is-open' : ''}`}
                aria-expanded={detailsOpen}
                aria-label={detailsOpen ? 'Hide details' : 'Show details'}
                onClick={() => setDetailsOpen(open => !open)}
              >
                <ChevronRightIcon size={12} />
              </button>
              {/* Unsubscribing acts on the sender, not on this message — hence here, not in the
                  actions zone. */}
              {unsubscribe && (
                <>
                  <span className="from-sep">·</span>
                  <a className="unsub-link" href={unsubscribe} target="_blank" rel="noopener noreferrer">
                    Unsubscribe
                  </a>
                </>
              )}
            </div>
            {detailsOpen ? (
              <ReaderDetails message={data} />
            ) : (
              <>
                {data.to.length > 0 && (
                  <div className="reader-recipients">To: <AddressList addresses={data.to} /></div>
                )}
                {data.cc.length > 0 && (
                  <div className="reader-recipients">Cc: <AddressList addresses={data.cc} /></div>
                )}
              </>
            )}
            {!!preferences && showSpamScoreOf(preferences) && <SpamGauge spamScore={data.spamScore} />}
          </div>
        </div>
        <ReaderActions
          showColourToggle={isDark && !!data.htmlBody}
          originalColours={originalColours}
          onToggleColours={() => setOriginalColours(v => !v)}
        />
      </header>

      {data.blockedImageCount > 0 && !showImages && (
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
          srcDoc={renderBodyDocument(body, { dark: inverted })}
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
