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
import { useDeleteMessages, useEmptyFolder, useFolders, useMoveMessages, useSetFlags } from '../queries'
import MoveMessagesModal from '../MoveMessagesModal'
import EmptyFolderBanner from './EmptyFolderBanner'
import { formatListDate } from './formatDate'
import LoadMoreSentinel from './LoadMoreSentinel'
import { sentinelIndexOf } from './messageStream'
import Pagination from './Pagination'
import SelectionToolbar from './SelectionToolbar'
import { useSelection } from './useSelection'
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
  /** `batch` is the whole set a bulk action removed; the single-row callers omit it (defaults to `[uid]`). */
  onDeparted?: (uid: number, batch?: number[]) => void
}

const COUNT = new Intl.NumberFormat('en-US')
const NO_ARCHIVE = 'Assign the archive folder in Settings → Folders'
const NO_JUNK = 'Assign the junk folder in Settings → Folders'
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
  const emptyFolder = useEmptyFolder(onNotify)
  const { data: folders } = useFolders()
  const roles = useMemo(() => rolePathsOf(folders ?? []), [folders])
  const [expunging, setExpunging] = useState<MailMessageSummary | null>(null)
  const [confirmingBulk, setConfirmingBulk] = useState(false)
  const [confirmingEmpty, setConfirmingEmpty] = useState(false)
  const [picker, setPicker] = useState<{ mode: 'move' | 'copy' } | null>(null)
  const inTrash = folderRole === 'trash'
  const archiveOff = !roles.archive || folderRole === 'archive'
  const archiveReason = folderRole === 'archive' ? 'Already in the archive folder' : NO_ARCHIVE
  const junkOff = !roles.junk || folderRole === 'junk'
  const junkReason = folderRole === 'junk' ? 'Already in the junk folder' : NO_JUNK
  const trashOff = !inTrash && !roles.trash
  const deleteLabel = inTrash ? 'Delete permanently' : 'Delete'
  const purges = folderRole === 'trash' || folderRole === 'junk'
  const emptyReason = total === 0
    ? 'This folder is already empty'
    : (!purges && !roles.trash ? NO_TRASH : undefined)

  // The hook keeps no row list; the effective selection is what it holds intersected with what
  // is on screen, so a departed row stops counting on its own. resetKey clears on folder change
  // and paged-page change, never while streaming more blocks into the same folder.
  const resetKey = `${folderPath}::${paging ? paging.page : 'stream'}`
  const selection = useSelection(resetKey)
  const loadedUids = messages.map(message => message.uid)
  const selectedUids = loadedUids.filter(uid => selection.has(uid))
  const count = selectedUids.length
  const allSelected = count > 0 && count === messages.length
  const indeterminate = count > 0 && !allSelected
  const overCap = count > 200

  // Fires the batch, advances the reader when the open row is in it, then drops the selection.
  // The whole batch is handed on so the reader can skip every departing row, not just the open one.
  function runBulk(uids: number[], fire: () => void) {
    fire()
    if (selectedUid !== null && uids.includes(selectedUid)) onDeparted?.(selectedUid, uids)
    selection.clear()
  }

  function bulkMove(target: string | null, copy: boolean) {
    if (!folderPath || !target || !count) return
    const path = folderPath
    const uids = selectedUids
    if (copy) {  // A copy departs nothing; the rows stay, so only the selection is dropped.
      moveMessages.mutate({ folderPath: path, uids, targetFolderPath: target, copy: true })
      selection.clear()
    } else {
      runBulk(uids, () => moveMessages.mutate({ folderPath: path, uids, targetFolderPath: target, copy: false }))
    }
  }

  function bulkDelete() {
    if (!folderPath || !count) return
    if (inTrash) { setConfirmingBulk(true); return }
    bulkMove(roles.trash, false)
  }

  function expungeBulk() {
    if (!folderPath || !count) return
    const path = folderPath
    const uids = selectedUids
    runBulk(uids, () => deleteMessages.mutate({ folderPath: path, uids }))
    setConfirmingBulk(false)
  }

  function bulkMark(value: boolean) {
    if (!folderPath || !count) return  // Marking read keeps the rows, so the reader never advances.
    setFlags.mutate({ folderPath, uids: selectedUids, flag: 'seen', value })
    selection.clear()
  }

  function pickTarget(target: string) {
    if (!picker) return
    bulkMove(target, picker.mode === 'copy')
    setPicker(null)
  }

  function onListKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === 'Escape' && count > 0) {
      event.stopPropagation()
      selection.clear()
    }
  }

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

  // Trash/junk purge permanently, so they confirm first; elsewhere it's a move to trash, undoable.
  function requestEmpty() {
    if (!folderPath) return
    if (purges) setConfirmingEmpty(true)
    else emptyFolder.mutate({ folderPath, targetFolderPath: roles.trash })
  }

  function confirmEmpty() {
    if (!folderPath) return
    emptyFolder.mutate({ folderPath })
    setConfirmingEmpty(false)
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
        <ul className={`message-list${count > 0 ? ' has-selection' : ''}`}>
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

            const check = (
              <input
                type="checkbox"
                className="message-row-check"
                aria-label={`Select message from ${from}`}
                checked={selection.has(message.uid)}
                onClick={event => {
                  event.stopPropagation()
                  if (event.shiftKey) selection.toggleRange(loadedUids, index)
                  else selection.toggle(message.uid, index)
                }}
                onChange={() => {}}
              />
            )

            const star = (
              <button
                type="button"
                className={`row-btn row-star${message.flagged ? ' is-on' : ''}`}
                aria-label={message.flagged ? 'Unstar' : 'Star'}
                onClick={event => { event.stopPropagation(); toggle(message, 'flagged') }}
              >
                <StarIcon filled={message.flagged} size={18} />
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
                  {message.seen ? <MailIcon size={18} /> : <MailOpenIcon size={18} />}
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
                  <ArchiveIcon size={18} />
                </button>
                <button
                  type="button"
                  className="row-btn is-danger"
                  aria-label={deleteLabel}
                  disabled={trashOff}
                  title={trashOff ? NO_TRASH : deleteLabel}
                  onClick={event => {
                    event.stopPropagation()
                    if (inTrash) setExpunging(message)
                    else moveTo(roles.trash, message.uid)
                  }}
                >
                  <TrashIcon size={18} />
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
                      {check}
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
                      {check}
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
    // display:contents band wrapper: it owns no box, so the three bands still stack under
    // .mail-list, but its keydown catches Escape from the toolbar as well as the rows.
    <div className="message-list-root" onKeyDown={onListKeyDown}>
      {/* Replaces the old heading band: the toolbar names the folder until a selection is on. */}
      <SelectionToolbar
        title={folderName || folderPath}
        count={count}
        allSelected={allSelected}
        indeterminate={indeterminate}
        onToggleAll={() => allSelected ? selection.clear() : selection.selectAll(loadedUids)}
        overCap={overCap}
        deleteLabel={deleteLabel}
        archive={{ onRun: () => bulkMove(roles.archive, false), disabledReason: archiveOff ? archiveReason : undefined }}
        junk={{ onRun: () => bulkMove(roles.junk, false), disabledReason: junkOff ? junkReason : undefined }}
        del={{ onRun: bulkDelete, disabledReason: trashOff ? NO_TRASH : undefined }}
        move={{ onRun: () => setPicker({ mode: 'move' }) }}
        copy={{ onRun: () => setPicker({ mode: 'copy' }) }}
        markRead={{ onRun: () => bulkMark(true) }}
        markUnread={{ onRun: () => bulkMark(false) }}
        emptyFolder={{ onRun: requestEmpty, disabledReason: emptyReason }}
      />

      <EmptyFolderBanner role={folderRole ?? null} total={total} onEmpty={requestEmpty} />

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

      {picker && (
        <MoveMessagesModal
          mode={picker.mode}
          folders={folders ?? []}
          currentFolderPath={folderPath}
          onPick={pickTarget}
          onClose={() => setPicker(null)}
        />
      )}

      {/* Bulk in-trash expunge: the same modal as a single row, named for the whole batch. */}
      {confirmingBulk && (
        <DeleteConfirmModal
          entityLabel={`${count} message${count === 1 ? '' : 's'}`}
          onConfirm={expungeBulk}
          onClose={() => setConfirmingBulk(false)}
          loading={deleteMessages.isPending}
        />
      )}

      {/* Permanent purge, from trash or junk: a fuller warning worded for the folder, since this
          cannot be undone. Closing is the ✕ alone, like every delete confirm. */}
      {confirmingEmpty && (
        <DeleteConfirmModal
          entityLabel={folderName || folderPath}
          message={
            <>
              This action will permanently delete all emails from the {folderName || folderPath} folder.
              <br />
              This action cannot be interrupted or undone.
            </>
          }
          onConfirm={confirmEmpty}
          onClose={() => setConfirmingEmpty(false)}
          loading={emptyFolder.isPending}
        />
      )}
    </div>
  )
}
