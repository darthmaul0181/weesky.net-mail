import { useEffect, useState } from 'react'
import PaperclipIcon from '../../../icons/PaperclipIcon'
import { PAGE_SIZE, useMessages } from '../queries'
import { formatListDate } from './formatDate'

interface Props {
  folderPath: string | null
  selectedUid: number | null
  onSelect: (uid: number) => void
}

export default function MessageList({ folderPath, selectedUid, onSelect }: Props) {
  const [page, setPage] = useState(0)

  // A page index means nothing in a different folder.
  useEffect(() => { setPage(0) }, [folderPath])

  const { data, isLoading, isError } = useMessages(folderPath, page)

  if (!folderPath) return <p className="mail-empty">Select a folder</p>
  if (isLoading && !data) return <p className="mail-empty">Loading messages…</p>
  if (isError) return <p className="mail-empty">Could not load messages.</p>
  if (!data || data.messages.length === 0) return <p className="mail-empty">No messages</p>

  const lastPage = Math.max(0, Math.ceil(data.total / PAGE_SIZE) - 1)

  return (
    <div>
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
                  {message.hasAttachments && <PaperclipIcon size={13} />}
                  <span className="message-row-date">{formatListDate(message.date)}</span>
                </div>
                <div className="message-row-subject">{message.subject || '(no subject)'}</div>
                {message.preview && <div className="message-row-preview">{message.preview}</div>}
              </button>
            </li>
          )
        })}
      </ul>

      {lastPage > 0 && (
        <div className="message-pager">
          <button type="button" className="btn" disabled={page === 0} onClick={() => setPage(p => p - 1)}>
            Previous page
          </button>
          <span className="message-pager-position">{page + 1} / {lastPage + 1}</span>
          <button type="button" className="btn" disabled={page >= lastPage} onClick={() => setPage(p => p + 1)}>
            Next page
          </button>
        </div>
      )}
    </div>
  )
}
