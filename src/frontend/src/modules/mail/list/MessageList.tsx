import { useEffect, useMemo, useRef, useState } from 'react'
import type { KeyboardEvent } from 'react'
import { showPreviewOf, usePreferences } from '../../../hooks/usePreferences'
import type { MailMessageSummary, SpecialUse } from '../api/mailTypes'
import ArchiveIcon from '../../../icons/ArchiveIcon'
import MailIcon from '../../../icons/MailIcon'
import MailOpenIcon from '../../../icons/MailOpenIcon'
import PaperclipIcon from '../../../icons/PaperclipIcon'
import StarIcon from '../../../icons/StarIcon'
import TrashIcon from '../../../icons/TrashIcon'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal.jsx'
import { rolePathsOf } from '../folders/folderNodes'
import { useDeleteMessages, useFolders, useMoveMessages, useSetFlags } from '../queries'
import { formatListDate } from './formatDate'
import LoadMoreSentinel from './LoadMoreSentinel'
import { sentinelIndexOf } from './messageStream'
import Pagination from './Pagination'
import { useMessageList } from './useMessageList'

interface Props {
  folderPath: string | null
  folderName?: string
  /** The open folder's own role: inside the trash, deleting expunges instead of moving. */
  folderRole?: SpecialUse | null
  selectedUid: number | null
  onSelect: (uid: number) => void
  wide?: boolean
  onNotify?: (message: string) => void
  onRows?: (uids: number[]) => void
  onDeparted?: (uid: number) => void
}

const COUNT = new Intl.NumberFormat('en-US')
const NO_ARCHIVE = 'Assign the archive folder in Settings → Folders'
const NO_TRASH = 'Assign the trash folder in Settings → Folders'

/**
 * Three bands: a heading, the rows, and the footer. Only the middle one scrolls — the pager
 * used to sit after the last row, so reaching it meant scrolling past fifty messages.
 */
export default function MessageList(
  { folderPath, folderName, folderRole, selectedUid, onSelect, wide = false, onNotify,
    onRows, onDeparted }: Props) {
  const { messages, total, isLoading, isError, paging, streaming } = useMessageList(folderPath)
  const { data: preferences } = usePreferences()
  const showsPreview = preferences ? showPreviewOf(preferences) : true
  const scrollRef = useRef<HTMLDivElement>(null)
  const setFlags = useSetFlags(onNotify)
  const moveMessages = useMoveMessages(onNotify)
  const deleteMessages = useDeleteMessages(onNotify)
  const { data: folders } = useFolders()
  const roles = useMemo(() => rolePathsOf(folders ?? []), [folders])
  const [expunging, setExpunging] = useState<MailMessageSummary | null>(null)
  const inTrash = folderRole === 'trash'
  const archiveOff = !roles.archive || folderRole === 'archive'
  const archiveReason = folderRole === 'archive' ? 'Already in the archive folder' : NO_ARCHIVE
  const trashOff = !inTrash && !roles.trash
  const deleteLabel = inTrash ? 'Delete permanently' : 'Delete'

  function toggle(message: MailMessageSummary, flag: 'seen' | 'flagged') {
    if (!folderPath) return
    const value = flag === 'seen' ? !message.seen : !message.flagged
    setFlags.mutate({ folderPath, uids: [message.uid], flag, value })
  }

  function moveTo(target: string | null, uid: number) {
    if (!folderPath || !target) return
    moveMessages.mutate({ folderPath, uids: [uid], targetFolderPath: target, copy: false })
    onDeparted?.(uid)
  }

  function expunge() {
    if (!folderPath || !expunging) return
    const uid = expunging.uid
    deleteMessages.mutate({ folderPath, uids: [uid] })
    setExpunging(null)
    onDeparted?.(uid)
  }

  // Inner buttons handle their own keys; the row only opens when the row itself has focus.
  function onRowKey(event: KeyboardEvent<HTMLDivElement>, uid: number) {
    if (event.target !== event.currentTarget) return
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault()
      onSelect(uid)
    }
  }

  // The page index resets on its own; the DOM scroll position does not, and would drop the
  // reader into the middle of a folder whose blocks are not loaded.
  useEffect(() => { if (scrollRef.current) scrollRef.current.scrollTop = 0 }, [folderPath])

  // The rows in view, for whoever has to pick the next selection when one of them leaves.
  useEffect(() => { onRows?.(messages.map(message => message.uid)) }, [messages, onRows])

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

            const from = message.fromName || message.fromAddress
            const subject = message.subject || '(no subject)'
            const when = formatListDate(message.date)
            const seenLabel = message.seen ? 'Mark as unread' : 'Mark as read'
            // role=button is children-presentational: nothing inside the row is exposed on its
            // own, so everything the row states visually has to be said in its name.
            const label = `${message.seen ? '' : 'Unread. '}${from}: ${subject}`
              + `${message.hasAttachments ? ', has attachments' : ''}, ${when}`

            const star = (
              <button
                type="button"
                className={`row-btn row-star${message.flagged ? ' is-on' : ''}`}
                aria-label={message.flagged ? 'Unstar' : 'Star'}
                onClick={event => { event.stopPropagation(); toggle(message, 'flagged') }}
              >
                <StarIcon filled={message.flagged} />
              </button>
            )

            const cluster = (
              <div className="message-row-cluster">
                <button
                  type="button"
                  className="row-btn"
                  aria-label={seenLabel}
                  title={seenLabel}
                  onClick={event => { event.stopPropagation(); toggle(message, 'seen') }}
                >
                  {message.seen ? <MailIcon size={16} /> : <MailOpenIcon size={16} />}
                </button>
                {/* Disabled with its reason, never withheld: a missing button reads as a bug. */}
                <button
                  type="button"
                  className="row-btn"
                  aria-label="Archive"
                  disabled={archiveOff}
                  title={archiveOff ? archiveReason : 'Archive'}
                  onClick={event => { event.stopPropagation(); moveTo(roles.archive, message.uid) }}
                >
                  <ArchiveIcon size={16} />
                </button>
                <button
                  type="button"
                  className="row-btn"
                  aria-label={deleteLabel}
                  disabled={trashOff}
                  title={trashOff ? NO_TRASH : deleteLabel}
                  onClick={event => {
                    event.stopPropagation()
                    if (inTrash) setExpunging(message)
                    else moveTo(roles.trash, message.uid)
                  }}
                >
                  <TrashIcon size={16} />
                </button>
              </div>
            )

            return (
              <li key={message.uid}>
                {streaming && index === sentinelRow && <LoadMoreSentinel onReach={streaming.loadMore} />}
                <div
                  role="button"
                  tabIndex={0}
                  aria-label={label}
                  className={classes.join(' ')}
                  onClick={() => onSelect(message.uid)}
                  onKeyDown={event => onRowKey(event, message.uid)}
                >
                  {wide ? (
                    <>
                      {!message.seen && <span className="message-row-unread-dot" />}
                      <span className="message-row-from">{from}</span>
                      {message.hasAttachments && <PaperclipIcon size={13} title="Has attachments" />}
                      <span className="message-row-line">
                        {subject}
                        {showsPreview && message.preview && (
                          <span className="message-row-line-preview"> — {message.preview}</span>
                        )}
                      </span>
                      <span className="message-row-date">{when}</span>
                      {cluster}
                      {star}
                    </>
                  ) : (
                    <>
                      <div className="message-row-top">
                        {!message.seen && <span className="message-row-unread-dot" />}
                        <span className="message-row-from">{from}</span>
                        {message.hasAttachments && <PaperclipIcon size={13} title="Has attachments" />}
                        <span className="message-row-date">{when}</span>
                        {star}
                      </div>
                      <div className="message-row-subject">{subject}</div>
                      {/* Always rendered when previews are on, even empty: a message with no body
                          would otherwise make a shorter row than its neighbours and break the rhythm
                          of the column. The reserved height lives in CSS. */}
                      {showsPreview && <div className="message-row-preview">{message.preview}</div>}
                      {cluster}
                    </>
                  )}
                </div>
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

      {/* Only inside the trash: everywhere else deleting is a move, and the trash is the undo. */}
      {expunging && (
        <DeleteConfirmModal
          entityLabel={expunging.subject || '(no subject)'}
          onConfirm={expunge}
          onClose={() => setExpunging(null)}
          loading={deleteMessages.isPending}
        />
      )}
    </>
  )
}
