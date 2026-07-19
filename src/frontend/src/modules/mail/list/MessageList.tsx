import { useEffect, useState } from 'react'
import PaperclipIcon from '../../../icons/PaperclipIcon'
import { PAGE_SIZE, useMessages } from '../queries'
import { formatListDate } from './formatDate'
import Pagination from './Pagination'

interface Props {
  folderPath: string | null
  folderName?: string
  selectedUid: number | null
  onSelect: (uid: number) => void
}

/**
 * Three bands: a heading, the rows, and the pager. Only the middle one scrolls, so both the
 * folder you are in and the way off the page you are on stay on screen — the pager used to sit
 * after the last row, which meant scrolling fifty messages to reach it.
 */
export default function MessageList({ folderPath, folderName, selectedUid, onSelect }: Props) {
  const [page, setPage] = useState(0)

  // A page index means nothing in a different folder.
  useEffect(() => { setPage(0) }, [folderPath])

  const { data, isLoading, isError } = useMessages(folderPath, page)

  if (!folderPath) return <p className="mail-empty">Select a folder</p>

  const lastPage = data ? Math.max(0, Math.ceil(data.total / PAGE_SIZE) - 1) : 0

  function rows() {
    if (isLoading && !data) return <p className="mail-empty">Loading messages…</p>
    if (isError) return <p className="mail-empty">Could not load messages.</p>
    if (!data || data.messages.length === 0) return <p className="mail-empty">No messages</p>

    return (
      <ul className="message-list">
        {data.messages.map(message => {
          const classes = ['message-row']
          if (!message.seen) classes.push('is-unread')
          if (message.uid === selectedUid) classes.push('is-selected')

          return (
            <li key={message.uid}>
              <button type="button" className={classes.join(' ')} onClick={() => onSelect(message.uid)}>
                <div className="message-row-top">
                  {!message.seen && <span className="message-row-unread-dot" />}
                  <span className="message-row-from">{message.fromName || message.fromAddress}</span>
                  {message.hasAttachments && <PaperclipIcon size={13} title="Has attachments" />}
                  <span className="message-row-date">{formatListDate(message.date)}</span>
                </div>
                <div className="message-row-subject">{message.subject || '(no subject)'}</div>
                {/* Always rendered, even empty: a message with no body would otherwise make a
                    shorter row than its neighbours and break the rhythm of the column. The
                    reserved height lives in CSS, so an empty div still occupies its line. */}
                <div className="message-row-preview">{message.preview}</div>
              </button>
            </li>
          )
        })}
      </ul>
    )
  }

  return (
    <>
      {/* Outside the scrolling band, so the column keeps saying which folder it shows. */}
      <h2 className="message-list-heading">{folderName || folderPath}</h2>

      <div className="mail-list-scroll">{rows()}</div>

      {lastPage > 0 && (
        <div className="mail-list-footer">
          <Pagination page={page} lastPage={lastPage} onSelect={setPage} />
        </div>
      )}
    </>
  )
}
