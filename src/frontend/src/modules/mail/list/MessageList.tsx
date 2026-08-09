import { useEffect, useMemo, useRef, useState } from 'react'
import type { CSSProperties, DragEvent, KeyboardEvent, ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import {
  DEFAULT_ROW_ACTIONS, requestSizeOf, rowActionsOf, showPreviewOf, usePreferences,
} from '../../../hooks/usePreferences'
import type { RowAction } from '../../../hooks/usePreferences'
import type { MailMessageSummary, MailSearchResult, SpecialUse } from '../api/mailTypes'
import ArchiveIcon from '../../../icons/ArchiveIcon'
import JunkIcon from '../../../icons/JunkIcon'
import MailIcon from '../../../icons/MailIcon'
import MailOpenIcon from '../../../icons/MailOpenIcon'
import PaperclipIcon from '../../../icons/PaperclipIcon'
import StarIcon from '../../../icons/StarIcon'
import TrashIcon from '../../../icons/TrashIcon'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal.jsx'
import { rolePathsOf } from '../folders/folderNodes'
import { useDeleteMessages, useEmptyFolder, useFolders, useMoveMessages, useSearchMessages, useSetFlags } from '../queries'
import MoveMessagesModal from '../MoveMessagesModal'
import AdvancedSearchModal from './AdvancedSearchModal'
import EmptyFolderBanner from './EmptyFolderBanner'
import SearchBar from './SearchBar'
import SearchResultsBanner from './SearchResultsBanner'
import { criteriaFromForm, isEmptyCriteria, isStarredOnly, labelOf } from './searchCriteria'
import type { AdvancedForm, SearchCriteria } from './searchCriteria'
import { DRAG_MIME, dragUids, serializeDrag } from './dragMessages'
import { buildDragPill } from './dragImage'
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
  /** Rendered at the head of the toolbar: the drawer's hamburger below 1024px. */
  leading?: ReactNode
  /** Refresh, which loses its home once the folder column is a drawer. */
  onRefresh?: () => void
  onNotify?: (message: string) => void
  onRows?: (uids: number[]) => void
  /** `batch` is the whole set a bulk action removed; the single-row callers omit it (defaults to `[uid]`). */
  onDeparted?: (uid: number, batch?: number[]) => void
  /** The active search, lifted to the layout; null is the ordinary folder view. */
  search: SearchCriteria | null
  onSearchChange: (criteria: SearchCriteria | null) => void
  /** A cross-folder hit opens in the folder it names, not the one on screen. */
  onOpenResult?: (uid: number, folderPath: string) => void
}

/**
 * Three bands: a heading, the rows, and the footer. Only the middle one scrolls — the pager
 * used to sit after the last row, so reaching it meant scrolling past fifty messages.
 */
export default function MessageList(
  { folderPath, folderName, folderRole, selectedUid, onSelect, wide = false, leading, onRefresh,
    onNotify, onRows, onDeparted, search = null, onSearchChange, onOpenResult }: Props) {
  const { t } = useTranslation('mail')
  const list = useMessageList(folderPath)
  const { data: preferences } = usePreferences()
  const showsPreview = preferences ? showPreviewOf(preferences) : true
  const rowActions: readonly RowAction[] = preferences ? rowActionsOf(preferences) : DEFAULT_ROW_ACTIONS

  const [searchOpen, setSearchOpen] = useState(false)
  const [advanced, setAdvanced] = useState<{ subject: string } | null>(null)
  const [searchPage, setSearchPage] = useState(0)

  // Render-time resets, the useMessageList pattern: no effect-lag page or stale-bar frame.
  const [shownSearch, setShownSearch] = useState(search)
  if (search !== shownSearch) { setShownSearch(search); setSearchPage(0) }
  const [shownFolder, setShownFolder] = useState(folderPath)
  if (folderPath !== shownFolder) { setShownFolder(folderPath); setSearchOpen(false); setAdvanced(null) }

  const searchSize = preferences ? requestSizeOf(preferences) : 0
  const searchQuery = useSearchMessages(search, searchPage, searchSize)
  const searching = search !== null
  const crossFolder = searching && search.allFolders
  const starred = searching && search.flagged === true

  // One shape for the render, whichever source fills it — rows/pager/footer never learn which.
  const view = searching
    ? {
        messages: (searchQuery.data?.results ?? []) as MailMessageSummary[],
        total: searchQuery.data?.total ?? 0,
        isLoading: searchQuery.isLoading,
        isError: searchQuery.isError,
        paging: {
          page: searchPage,
          lastPage: searchSize > 0
            ? Math.max(0, Math.ceil((searchQuery.data?.total ?? 0) / searchSize) - 1)
            : 0,
          onSelect: setSearchPage,
        },
        streaming: null,
      }
    : list
  const { messages, total, isLoading, isError, paging, streaming } = view
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
  const [draggingUids, setDraggingUids] = useState<number[] | null>(null)
  const inTrash = folderRole === 'trash'
  const archiveOff = !roles.archive || folderRole === 'archive'
  const archiveReason = t(folderRole === 'archive' ? 'actions.alreadyArchived' : 'actions.noArchiveFolder')
  const junkOff = !roles.junk || folderRole === 'junk'
  const junkReason = t(folderRole === 'junk' ? 'actions.alreadyJunk' : 'actions.noJunkFolder')
  const trashOff = !inTrash && !roles.trash
  const deleteLabel = inTrash
    ? t('actions.deletePermanently') : t('actions.delete', { ns: 'common' })
  const purges = folderRole === 'trash' || folderRole === 'junk'
  const emptyReason = total === 0
    ? t('list.alreadyEmpty')
    : (!purges && !roles.trash ? t('actions.noTrashFolder') : undefined)

  // The hook keeps no row list; the effective selection is what it holds intersected with what
  // is on screen, so a departed row stops counting on its own. resetKey clears on folder change
  // and paged-page change, never while streaming more blocks into the same folder.
  const resetKey = `${folderPath}::${searching ? `search:${searchPage}` : (paging ? paging.page : 'stream')}`
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

  // A drag carries the checked selection when the grabbed row belongs to it, that row alone
  // otherwise. The pill lives off-screen just long enough for the browser to snapshot it.
  function onRowDragStart(event: DragEvent<HTMLDivElement>, uid: number) {
    if (crossFolder || !folderPath) return
    const uids = dragUids(selectedUids, uid)
    event.dataTransfer.setData(DRAG_MIME, serializeDrag({ sourcePath: folderPath, uids }))
    event.dataTransfer.effectAllowed = 'move'
    const pill = buildDragPill(uids.length)
    pill.style.position = 'absolute'
    pill.style.top = '-9999px'
    document.body.appendChild(pill)
    event.dataTransfer.setDragImage(pill, 12, 12)
    setTimeout(() => pill.remove(), 0)
    setDraggingUids(uids)
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
  function onRowKey(event: KeyboardEvent<HTMLDivElement>, message: MailMessageSummary) {
    if (event.target !== event.currentTarget) return
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault()
      openRow(message)
    }
  }

  // A cross-folder hit belongs to another folder: it opens there. Otherwise it is the open folder.
  function openRow(message: MailMessageSummary) {
    if (crossFolder) onOpenResult?.(message.uid, (message as MailSearchResult).folderPath)
    else onSelect(message.uid)
  }

  function toggleSearch() {
    if (searchOpen) closeSearch()
    else setSearchOpen(true)
  }

  function closeSearch() {
    setSearchOpen(false)
    setAdvanced(null)
    onSearchChange(null)
  }

  function quickSearch(text: string) {
    if (folderPath) onSearchChange({ folderPath, allFolders: false, quick: text })
  }

  /** The star writes the same `flagged` criterion the advanced form's checkbox does, on the
      search already running when there is one — so the two are one state, not two filters. */
  function toggleStarred() {
    if (search?.flagged) {
      const rest: SearchCriteria = { ...search }
      delete rest.flagged
      onSearchChange(isEmptyCriteria(rest) ? null : rest)
      return
    }
    if (search) { onSearchChange({ ...search, flagged: true }); return }
    if (folderPath) onSearchChange({ folderPath, allFolders: false, flagged: true })
  }

  function advancedSearch(form: AdvancedForm) {
    setAdvanced(null)
    if (folderPath) onSearchChange(criteriaFromForm(folderPath, form))
  }

  // The page index resets on its own; the DOM scroll position does not, and would drop the
  // reader into the middle of a folder whose blocks are not loaded.
  useEffect(() => { if (scrollRef.current) scrollRef.current.scrollTop = 0 }, [folderPath])

  // The rows in view, for whoever has to pick the next selection when one of them leaves.
  // Cross-folder results carry no navigable uid for this folder, so the reader is handed none.
  useEffect(() => {
    onRows?.(crossFolder ? [] : messages.map(message => message.uid))
  }, [messages, crossFolder, onRows])

  if (!folderPath) return <p className="mail-empty">{t('list.selectFolder')}</p>

  // The drafts folder has no sender worth showing — it's always the account itself — so the row
  // names who the draft is going to instead, with a marker calling out that it isn't sent mail.
  const drafts = folderRole === 'drafts'

  function rows() {
    if (isLoading) return <p className="mail-empty">{t(searching ? 'search.searching' : 'list.loading')}</p>
    if (isError) return <p className="mail-empty">{t('list.loadFailed')}</p>
    if (messages.length === 0) {
      return <p className="mail-empty">{t(searching ? 'list.noResults' : 'list.noMessages')}</p>
    }

    const sentinelRow = streaming?.hasMore ? sentinelIndexOf(messages.length) : -1

    return (
      <>
        <ul className={`message-list${count > 0 ? ' has-selection' : ''}`}>
          {messages.map((message, index) => {
            const classes = ['message-row']
            if (wide) classes.push('is-line')
            if (!message.seen) classes.push('is-unread')
            if (message.uid === selectedUid) classes.push('is-selected')
            if (draggingUids?.includes(message.uid)) classes.push('is-dragging')

            const from = drafts
              ? (message.to.length > 0
                  ? message.to.map(a => a.name || a.address).join(', ')
                  : t('list.noRecipient'))
              : (message.fromName || message.fromAddress)
            const subject = message.subject || t('list.noSubject')
            const when = formatListDate(message.date)
            const seenLabel = t(message.seen ? 'toolbar.markUnread' : 'toolbar.markRead')
            const priorityLabel = message.priority === 'high' ? t('list.highPriority')
              : message.priority === 'low' ? t('list.lowPriority') : null
            // A word in the Draft badge's slot, not a glyph in the subject line: a glyph there sat
            // in the text flow, so a marked row started its subject 17px right of every other one
            // and the column lost the axis the eye scans down.
            const priorityMark = priorityLabel && (
              <span className={`message-row-priority is-${message.priority}`} title={priorityLabel}>
                {t(message.priority === 'high' ? 'list.high' : 'list.low')}
              </span>
            )
            // role=button is children-presentational: nothing inside the row is exposed on its
            // own, so everything the row states visually has to be said in its name.
            const label = t('list.rowLabel', {
              prefix: `${message.seen ? '' : t('list.aria.unread')}`
                + `${drafts ? t('list.aria.draft') : ''}`
                + `${priorityLabel ? t('list.aria.priority', { label: priorityLabel }) : ''}`,
              from,
              subject,
              attachments: message.hasAttachments ? t('list.aria.hasAttachments') : '',
              when,
            })

            // Cross-folder results neutralize row selection and actions: the row lives in another
            // folder, so a checkbox, star or cluster acting on this one would act on the wrong mailbox.
            const check = crossFolder ? null : (
              <input
                type="checkbox"
                className="message-row-check"
                aria-label={t('list.selectMessage', { from })}
                checked={selection.has(message.uid)}
                onClick={event => {
                  event.stopPropagation()
                  if (event.shiftKey) selection.toggleRange(loadedUids, index)
                  else selection.toggle(message.uid, index)
                }}
                onChange={() => {}}
              />
            )

            const star = crossFolder ? null : (
              <button
                type="button"
                className={`row-btn row-star${message.flagged ? ' is-on' : ''}`}
                aria-label={t(message.flagged ? 'list.unstar' : 'list.star')}
                onClick={event => { event.stopPropagation(); toggle(message, 'flagged') }}
              >
                <StarIcon filled={message.flagged} size={18} />
              </button>
            )

            {/* Withheld here is the user's own choice, made in Settings. A button whose role no
                folder holds is still drawn, disabled, with its reason: that absence would read as
                a bug, this one was asked for. */}
            const buttons: Record<RowAction, ReactNode> = {
              seen: (
                <button
                  key="seen"
                  type="button"
                  className="row-btn"
                  aria-label={seenLabel}
                  title={seenLabel}
                  onClick={event => { event.stopPropagation(); toggle(message, 'seen') }}
                >
                  {message.seen ? <MailIcon size={18} /> : <MailOpenIcon size={18} />}
                </button>
              ),
              archive: (
                <button
                  key="archive"
                  type="button"
                  className="row-btn"
                  aria-label={t('toolbar.archive')}
                  disabled={archiveOff}
                  title={archiveOff ? archiveReason : t('toolbar.archive')}
                  onClick={event => { event.stopPropagation(); moveTo(roles.archive, message.uid) }}
                >
                  <ArchiveIcon size={18} />
                </button>
              ),
              junk: (
                <button
                  key="junk"
                  type="button"
                  className="row-btn"
                  aria-label={t('toolbar.junk')}
                  disabled={junkOff}
                  title={junkOff ? junkReason : t('toolbar.junk')}
                  onClick={event => { event.stopPropagation(); moveTo(roles.junk, message.uid) }}
                >
                  <JunkIcon size={18} />
                </button>
              ),
              delete: (
                <button
                  key="delete"
                  type="button"
                  className="row-btn is-danger"
                  aria-label={deleteLabel}
                  disabled={trashOff}
                  title={trashOff ? t('actions.noTrashFolder') : deleteLabel}
                  onClick={event => {
                    event.stopPropagation()
                    if (inTrash) setExpunging(message)
                    else moveTo(roles.trash, message.uid)
                  }}
                >
                  <TrashIcon size={18} />
                </button>
              ),
            }

            // One value behind both the cluster and the width the row reserves for it: the reserve
            // is what ends the line above in an ellipsis, and a count it derived on its own could
            // disagree with what is actually drawn.
            const shownActions = crossFolder ? [] : rowActions
            const cluster = shownActions.length === 0 ? null : (
              <div className="message-row-cluster">{shownActions.map(action => buttons[action])}</div>
            )

            return (
              <li key={message.uid}>
                {streaming && index === sentinelRow && <LoadMoreSentinel onReach={streaming.loadMore} />}
                <div
                  role="button"
                  tabIndex={0}
                  aria-label={label}
                  className={classes.join(' ')}
                  style={{ '--row-actions': shownActions.length } as CSSProperties}
                  draggable={!crossFolder}
                  onClick={() => openRow(message)}
                  onKeyDown={event => onRowKey(event, message)}
                  onDragStart={event => onRowDragStart(event, message.uid)}
                  onDragEnd={() => setDraggingUids(null)}
                >
                  {wide ? (
                    <>
                      {check}
                      {!message.seen && <span className="message-row-unread-dot" />}
                      {drafts && <span className="message-row-draft">{t('list.draft')}</span>}
                      {priorityMark}
                      <span className="message-row-from">{from}</span>
                      {message.hasAttachments && <PaperclipIcon size={13} title={t('list.hasAttachments')} />}
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
                        {drafts && <span className="message-row-draft">{t('list.draft')}</span>}
                        {priorityMark}
                        <span className="message-row-from">{from}</span>
                        {message.hasAttachments && <PaperclipIcon size={13} title={t('list.hasAttachments')} />}
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

        {streaming?.isLoadingMore && <p className="mail-block-state">{t('list.loadingMore')}</p>}
        {streaming?.loadMoreFailed && (
          <p className="mail-block-state">
            {t('list.loadMoreFailed')}{' '}
            <button type="button" className="mail-retry" onClick={streaming.loadMore}>{t('list.retry')}</button>
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
        leading={leading}
        refresh={onRefresh ? { onRun: onRefresh } : undefined}
        title={folderName || folderPath}
        count={count}
        allSelected={allSelected}
        indeterminate={indeterminate}
        onToggleAll={() => allSelected ? selection.clear() : selection.selectAll(loadedUids)}
        overCap={overCap}
        deleteLabel={deleteLabel}
        archive={{ onRun: () => bulkMove(roles.archive, false), disabledReason: archiveOff ? archiveReason : undefined }}
        junk={{ onRun: () => bulkMove(roles.junk, false), disabledReason: junkOff ? junkReason : undefined }}
        del={{ onRun: bulkDelete, disabledReason: trashOff ? t('actions.noTrashFolder') : undefined }}
        move={{ onRun: () => setPicker({ mode: 'move' }) }}
        copy={{ onRun: () => setPicker({ mode: 'copy' }) }}
        markRead={{ onRun: () => bulkMark(true) }}
        markUnread={{ onRun: () => bulkMark(false) }}
        emptyFolder={{ onRun: requestEmpty,
          // Emptying acts on the whole real folder, so it is off under a search: its reason would
          // otherwise read off the search total, and the non-purge branch fires with no confirm.
          disabledReason: searching ? t('list.clearSearchFirst') : emptyReason }}
        searchOpen={searchOpen}
        onToggleSearch={toggleSearch}
        starred={starred}
        onToggleStarred={toggleStarred}
        starredDisabled={!starred && !folderPath}
        selectionDisabled={crossFolder}
      />

      {searchOpen && folderPath && (
        <SearchBar
          folderTitle={folderName || folderPath}
          onSearch={quickSearch}
          onOpenAdvanced={text => setAdvanced({ subject: text })}
          onClose={closeSearch}
        />
      )}

      {/* The lit star is the whole indication when it stands alone, so the heading keeps the
          folder name; any other criterion hands it back to the banner. */}
      {searching && !isStarredOnly(search) && (
        <SearchResultsBanner
          total={searchQuery.data?.total ?? null}
          label={labelOf(search)}
          onClear={closeSearch}
        />
      )}

      {/* The empty-folder offer belongs to the folder itself, not to a search laid over it. */}
      {!searching && <EmptyFolderBanner role={folderRole ?? null} total={total} onEmpty={requestEmpty} />}

      <div className="mail-list-scroll" ref={scrollRef}>{rows()}</div>

      {/* The footer is the pager's alone. Streaming has no page to go to, so it carries no band:
          the rows take the height back, and the loaded count is already the scrollbar's job. */}
      {paging && paging.lastPage > 0 && (
        <div className="mail-list-footer">
          <Pagination page={paging.page} lastPage={paging.lastPage} onSelect={paging.onSelect} />
        </div>
      )}

      {/* Only inside the trash: everywhere else deleting is a move, and the trash is the undo. */}
      {expunging && (
        <DeleteConfirmModal
          entityLabel={expunging.subject || t('list.noSubject')}
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
          entityLabel={t('list.bulkLabel', { count })}
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
              {t('list.emptyConfirmLine1', { folder: folderName || folderPath })}
              <br />
              {t('list.emptyConfirmLine2')}
            </>
          }
          onConfirm={confirmEmpty}
          onClose={() => setConfirmingEmpty(false)}
          loading={emptyFolder.isPending}
        />
      )}

      {advanced && folderPath && (
        <AdvancedSearchModal
          folderTitle={folderName || folderPath}
          initialSubject={advanced.subject}
          onSearch={advancedSearch}
          onClose={() => setAdvanced(null)}
        />
      )}
    </div>
  )
}
