import { useEffect, useRef } from 'react'
import { showPreviewOf, usePreferences } from '../../../hooks/usePreferences'
import PaperclipIcon from '../../../icons/PaperclipIcon'
import { formatListDate } from './formatDate'
import LoadMoreSentinel from './LoadMoreSentinel'
import { sentinelIndexOf } from './messageStream'
import Pagination from './Pagination'
import { useMessageList } from './useMessageList'

interface Props {
  folderPath: string | null
  folderName?: string
  selectedUid: number | null
  onSelect: (uid: number) => void
  wide?: boolean
}

const COUNT = new Intl.NumberFormat('en-US')

/**
 * Three bands: a heading, the rows, and the footer. Only the middle one scrolls — the pager
 * used to sit after the last row, so reaching it meant scrolling past fifty messages.
 */
export default function MessageList({ folderPath, folderName, selectedUid, onSelect, wide = false }: Props) {
  const { messages, total, isLoading, isError, paging, streaming } = useMessageList(folderPath)
  const { data: preferences } = usePreferences()
  const showsPreview = preferences ? showPreviewOf(preferences) : true
  const scrollRef = useRef<HTMLDivElement>(null)

  // The page index resets on its own; the DOM scroll position does not, and would drop the
  // reader into the middle of a folder whose blocks are not loaded.
  useEffect(() => { if (scrollRef.current) scrollRef.current.scrollTop = 0 }, [folderPath])

  if (!folderPath) return <p className="mail-empty">Select a folder</p>

  function rows() {
    if (isLoading) return <p className="mail-empty">Loading messages…</p>
    if (isError) return <p className="mail-empty">Could not load messages.</p>
    if (messages.length === 0) return <p className="mail-empty">No messages</p>

    const sentinelRow = streaming?.hasMore ? sentinelIndexOf(messages.length) : -1

    return (
      <>
        <ul className="message-list">
          {messages.map((message, index) => {
            const classes = ['message-row']
            if (wide) classes.push('is-line')
            if (!message.seen) classes.push('is-unread')
            if (message.uid === selectedUid) classes.push('is-selected')

            return (
              <li key={message.uid}>
                {streaming && index === sentinelRow && <LoadMoreSentinel onReach={streaming.loadMore} />}
                <button type="button" className={classes.join(' ')} onClick={() => onSelect(message.uid)}>
                  {wide ? (
                    <>
                      {!message.seen && <span className="message-row-unread-dot" />}
                      <span className="message-row-from">{message.fromName || message.fromAddress}</span>
                      {message.hasAttachments && <PaperclipIcon size={13} title="Has attachments" />}
                      <span className="message-row-line">
                        {message.subject || '(no subject)'}
                        {showsPreview && message.preview && (
                          <span className="message-row-line-preview"> — {message.preview}</span>
                        )}
                      </span>
                      <span className="message-row-date">{formatListDate(message.date)}</span>
                    </>
                  ) : (
                    <>
                      <div className="message-row-top">
                        {!message.seen && <span className="message-row-unread-dot" />}
                        <span className="message-row-from">{message.fromName || message.fromAddress}</span>
                        {message.hasAttachments && <PaperclipIcon size={13} title="Has attachments" />}
                        <span className="message-row-date">{formatListDate(message.date)}</span>
                      </div>
                      <div className="message-row-subject">{message.subject || '(no subject)'}</div>
                      {/* Always rendered when previews are on, even empty: a message with no body
                          would otherwise make a shorter row than its neighbours and break the rhythm
                          of the column. The reserved height lives in CSS. */}
                      {showsPreview && <div className="message-row-preview">{message.preview}</div>}
                    </>
                  )}
                </button>
              </li>
            )
          })}
        </ul>

        {streaming?.isLoadingMore && <p className="mail-block-state">Loading more…</p>}
        {streaming?.loadMoreFailed && (
          <p className="mail-block-state">
            Could not load more.{' '}
            <button type="button" className="mail-retry" onClick={streaming.loadMore}>Retry</button>
          </p>
        )}
      </>
    )
  }

  return (
    <>
      {/* Outside the scrolling band, so the column keeps saying which folder it shows. */}
      <h2 className="message-list-heading">{folderName || folderPath}</h2>

      <div className="mail-list-scroll" ref={scrollRef}>{rows()}</div>

      {paging && paging.lastPage > 0 && (
        <div className="mail-list-footer">
          <Pagination page={paging.page} lastPage={paging.lastPage} onSelect={paging.onSelect} />
        </div>
      )}

      {/* Loaded / total. Removing this block removes the counter and nothing else. */}
      {streaming && total > 0 && (
        <div className="mail-list-footer">
          <span className="mail-list-count">{COUNT.format(messages.length)} of {COUNT.format(total)}</span>
        </div>
      )}
    </>
  )
}
