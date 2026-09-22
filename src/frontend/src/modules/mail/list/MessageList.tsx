import { Fragment, useEffect, useMemo, useRef, useState } from 'react'
import type {
  CSSProperties, DragEvent, KeyboardEvent, ReactNode, RefObject,
} from 'react'
import { useTranslation } from 'react-i18next'
import {
  DEFAULT_ROW_ACTIONS, requestSizeOf, rowActionsOf, showPreviewOf, usePreferences,
} from '../../../hooks/usePreferences'
import type { RowAction } from '../../../hooks/usePreferences'
import type { MailMessageSummary, MailSearchResult, SpecialUse } from '../api/mailTypes'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal.jsx'
import { hasOpenLayer } from '../../../lib/layerStack'
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
import MessageRow, { rowUidsOf } from './MessageRow'
import type { CheckGesture, RowCallbacks } from './MessageRow'
import { memberUids } from './threading'
import LoadMoreSentinel from './LoadMoreSentinel'
import { sentinelIndexOf } from './messageStream'
import Pagination from './Pagination'
import SelectionToolbar from './SelectionToolbar'
import { useSelection } from './useSelection'
import { ROW_EXIT_MS } from './useRowExit'
import type { RowExit } from './useRowExit'
import { useMessageList } from './useMessageList'
import { useForwarders } from '../../../hooks/useForwarders'
import { useGridNav } from '../../../hooks/useGridNav'
import { useToday } from '../../../hooks/useToday'
import { usePullToRefresh } from '../../../hooks/usePullToRefresh'

/**
 * The rows' container. It is a component of its own because `useGridNav`'s effect is keyed on the
 * ref alone: a grid that is absent when its owner first lays out never gets the hook at all, and
 * this element comes and goes with the rows — a loading, failed or empty list draws none.
 */
function MessageGrid({ label, rowCount, selecting, children }: {
  label: string
  /** The folder's own row total, or -1 where it is not knowable in rows. */
  rowCount: number
  selecting: boolean
  children: ReactNode
}) {
  const grid = useRef<HTMLDivElement>(null)
  useGridNav({ ref: grid })
  return (
    <div
      className={`message-list${selecting ? ' has-selection' : ''}`}
      role="grid"
      aria-label={label}
      aria-rowcount={rowCount}
      ref={grid}
    >
      {children}
    </div>
  )
}

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
  /** The pull-to-refresh gesture's handler, bound at every width — a touch laptop has it too. */
  onRefresh?: () => void
  /** The folder column is behind a drawer, so its RefreshButton is out of reach and the kebab
      takes the action. On desktop that button is on screen and a second entry is a duplicate. */
  inDrawer?: boolean
  onNotify?: (message: string) => void
  onRows?: (uids: number[]) => void
  /** `batch` is the whole set a bulk action removed; the single-row callers omit it (defaults to `[uid]`). */
  onDeparted?: (uid: number, batch?: number[]) => void
  /** Lives in the layout, which also owns the drop and the reader — the three share one set. */
  rowExit: RowExit
  /** The active search, lifted to the layout; null is the ordinary folder view. */
  search: SearchCriteria | null
  onSearchChange: (criteria: SearchCriteria | null) => void
  /** A cross-folder hit opens in the folder it names, not the one on screen. */
  onOpenResult?: (uid: number, folderPath: string) => void
  /** The column itself, where focus goes when a confirmed delete takes its own button with it. */
  regionRef?: RefObject<HTMLElement | null>
}

/**
 * Three bands: a heading, the rows, and the footer. Only the middle one scrolls — the pager
 * used to sit after the last row, so reaching it meant scrolling past fifty messages.
 */
export default function MessageList(
  { folderPath, folderName, folderRole, selectedUid, onSelect, wide = false, leading, onRefresh,
    inDrawer = false, onNotify, onRows, onDeparted, rowExit, search = null, onSearchChange,
    onOpenResult, regionRef }: Props) {
  const { t } = useTranslation('mail')
  const today = useToday()
  const list = useMessageList(folderPath)
  const { data: preferences } = usePreferences()
  const showsPreview = preferences ? showPreviewOf(preferences) : true
  // A fresh array on every render would re-render every memoised row on every render.
  const rowActions = useMemo<readonly RowAction[]>(
    () => (preferences ? rowActionsOf(preferences) : DEFAULT_ROW_ACTIONS), [preferences])

  const [searchOpen, setSearchOpen] = useState(false)
  const [advanced, setAdvanced] = useState<{ subject: string } | null>(null)
  const [searchPage, setSearchPage] = useState(0)

  // Render-time resets, the useMessageList pattern: no effect-lag page or stale-bar frame.
  const [shownSearch, setShownSearch] = useState(search)
  if (search !== shownSearch) { setShownSearch(search); setSearchPage(0) }
  const [shownFolder, setShownFolder] = useState(folderPath)
  if (folderPath !== shownFolder) { setShownFolder(folderPath); setSearchOpen(false); setAdvanced(null) }

  const pageSize = preferences ? requestSizeOf(preferences) : 0
  const searchQuery = useSearchMessages(search, searchPage, pageSize)
  const searching = search !== null
  const crossFolder = searching && search.allFolders
  const starred = searching && search.flagged === true

  // Memoised rather than rebuilt: a fresh `groups` would rebuild `loadedUids` and `selectedUids`
  // with it on every render. No row is ever handed `members` here — a hit is its own group.
  const searchView = useMemo(() => {
    const results = (searchQuery.data?.results ?? []) as MailMessageSummary[]
    const found = searchQuery.data?.total ?? 0
    return {
      // Search hits are never threaded: each result is its own one-member group.
      groups: results.map(result => ({ key: result.uid, messages: [result] })),
      messages: results,
      total: found,
      isLoading: searchQuery.isLoading,
      isError: searchQuery.isError,
      paging: {
        page: searchPage,
        lastPage: pageSize > 0 ? Math.max(0, Math.ceil(found / pageSize) - 1) : 0,
        onSelect: setSearchPage,
      },
      streaming: null,
      // Results are never threaded, whatever the grouping setting says: one hit, one row.
      rowTotal: found,
    }
  }, [searchQuery.data, searchQuery.isLoading, searchQuery.isError, searchPage, pageSize])

  // One shape for the render, whichever source fills it — rows/pager/footer never learn which.
  const view = searching ? searchView : list
  const { groups, messages, total, rowTotal, isLoading, isError, paging, streaming } = view

  // aria-rowindex is 1-based over the whole folder, never over the loaded slice: the pages behind
  // this one hold exactly `pageSize` rows each, since `expanded` resets with the page. Streaming
  // loads from the folder's first row, so its own offset is nothing.
  const rowOffset = (paging?.page ?? 0) * pageSize
  const scrollRef = useRef<HTMLDivElement>(null)
  const { pull, armed } = usePullToRefresh(scrollRef, () => onRefresh?.())
  const setFlags = useSetFlags(onNotify)
  const moveMessages = useMoveMessages(onNotify)
  const deleteMessages = useDeleteMessages(onNotify)
  const { departing } = rowExit
  const emptyFolder = useEmptyFolder(onNotify)
  const { data: folders } = useFolders()
  const roles = useMemo(() => rolePathsOf(folders ?? []), [folders])
  // Named for the confirm dialog; the uids are the whole thread when the row is one.
  const [expunging, setExpunging] = useState<{ label: string; uids: number[] } | null>(null)
  const [confirmingBulk, setConfirmingBulk] = useState(false)
  const [confirmingEmpty, setConfirmingEmpty] = useState(false)
  const [picker, setPicker] = useState<{ mode: 'move' | 'copy' } | null>(null)
  // A Set, not the array: `includes` per row is quadratic across the page, and a drag carrying
  // the whole selection is exactly when the page is longest.
  const [draggingUids, setDraggingUids] = useState<Set<number> | null>(null)
  const inTrash = folderRole === 'trash'
  const archiveOff = !roles.archive || folderRole === 'archive'
  const archiveReason = t(folderRole === 'archive' ? 'actions.alreadyArchived' : 'actions.noArchiveFolder')
  const junkOff = !roles.junk || folderRole === 'junk'
  const junkReason = t(folderRole === 'junk' ? 'actions.alreadyJunk' : 'actions.noJunkFolder')
  const trashOff = !inTrash && !roles.trash
  const trashReason = t('actions.noTrashFolder')
  const deleteLabel = inTrash
    ? t('actions.deletePermanently') : t('actions.delete', { ns: 'common' })
  const purges = folderRole === 'trash' || folderRole === 'junk'
  const emptyReason = total === 0
    ? t('list.alreadyEmpty')
    : (!purges && !roles.trash ? trashReason : undefined)

  // Clears the selection, expanded threads and scroll on a folder, page or search change (the
  // criteria too: two searches both sit on `search:0`), never while streaming into one folder.
  const resetKey = `${folderPath}::${searching ? `search:${searchPage}` : (paging ? paging.page : 'stream')}`
    + `::${JSON.stringify(search ?? null)}`
  const selection = useSelection(resetKey)
  const { selected } = selection
  const loadedUids = useMemo(() => memberUids(groups), [groups])

  // Which threads are unfolded; a look at this folder's page, not a preference, so it resets
  // with the selection: on a folder change, a page change, and entering or leaving a search.
  const [expanded, setExpanded] = useState<Set<number>>(() => new Set())
  useEffect(() => { setExpanded(new Set()) }, [resetKey])
  const toggleExpanded = (key: number) => setExpanded(prev => {
    const next = new Set(prev)
    if (next.has(key)) next.delete(key); else next.add(key)
    return next
  })
  // aria-rowcount counts rows, and an unfolded conversation draws members the folder's own count
  // never knew about — the pages behind this one hold none, since `expanded` resets with the page.
  const unfolded = groups.reduce((extra, group) =>
    extra + (group.messages.length > 1 && expanded.has(group.key) ? group.messages.length : 0), 0)
  const rowCount = rowTotal < 0 ? -1 : rowTotal + unfolded
  const selectedUids = useMemo(
    () => loadedUids.filter(uid => selected.has(uid)), [loadedUids, selected])
  const count = selectedUids.length
  const allSelected = count > 0 && count === messages.length
  const indeterminate = count > 0 && !allSelected
  const overCap = count > 200

  // Fires the batch, advances the reader when the open row is in it, then drops the selection.
  // The whole batch is handed on so the reader can skip every departing row, not just the open one.
  // The rows leave together — a stagger over a batch of fifty is two seconds of waiting.
  function runBulk(uids: number[], fire: () => void) {
    rowExit.depart(uids, fire)
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

  // The dialogs render inside this root, so their Escape bubbles here: it belongs to whatever
  // layer is open, not to the selection. Asking the stack rather than listing the dialogs keeps
  // the sixth one from being forgotten here.
  function onListKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (event.key === 'Escape' && count > 0 && !hasOpenLayer()) {
      event.stopPropagation()
      selection.clear()
    }
  }

  function toggle(uids: number[], flag: 'seen' | 'flagged', value: boolean) {
    if (!folderPath) return
    setFlags.mutate({ folderPath, uids, flag, value })
  }

  // A single row reports itself; a thread hands the whole batch over, led by the open member
  // when it holds one — the layout only advances the reader off the uid that is actually open.
  function reportDeparted(uids: number[]) {
    if (uids.length === 1) { onDeparted?.(uids[0]); return }
    if (selectedUid !== null && uids.includes(selectedUid)) onDeparted?.(selectedUid, uids)
    else onDeparted?.(uids[0], uids)
  }

  // The reader is told at the click and the list takes its time: only the rows are animating, and
  // an open message that waits 300ms to be replaced reads as the action having missed.
  function moveTo(target: string | null, uids: number[]) {
    if (!folderPath || !target) return
    rowExit.depart(uids, () =>
      moveMessages.mutate({ folderPath, uids, targetFolderPath: target, copy: false }))
    reportDeparted(uids)
  }

  // The selection-or-row rule lives in dragUids; the pill lives off-screen just long enough
  // for the browser to snapshot it.
  function onRowDragStart(event: DragEvent<HTMLDivElement>, rowUids: number[]) {
    if (crossFolder || !folderPath) return
    const uids = dragUids(selectedUids, rowUids)
    event.dataTransfer.setData(DRAG_MIME, serializeDrag({ sourcePath: folderPath, uids }))
    event.dataTransfer.effectAllowed = 'move'
    const pill = buildDragPill(uids.length)
    pill.style.position = 'absolute'
    pill.style.top = '-9999px'
    document.body.appendChild(pill)
    event.dataTransfer.setDragImage(pill, 12, 12)
    setTimeout(() => pill.remove(), 0)
    setDraggingUids(new Set(uids))
  }

  function expunge() {
    if (!folderPath || !expunging) return
    const uids = expunging.uids
    rowExit.depart(uids, () => deleteMessages.mutate({ folderPath, uids }))
    setExpunging(null)
    reportDeparted(uids)
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

  function checkRow(uids: number[], index: number, { was, whole, shift }: CheckGesture) {
    if (whole) selection.setMany(uids, !was)
    else if (shift) selection.toggleRange(loadedUids, index)
    else selection.toggle(uids[0], index)
  }

  function removeRow(uids: number[], label: string) {
    if (inTrash) setExpunging({ label, uids })
    else moveTo(roles.trash, uids)
  }

  // What a row calls, as one object built once and never rebuilt: a row handed a callback this
  // render created would re-render whenever anything did.
  const rowOn: RowCallbacks = useForwarders({
    open: openRow,
    check: checkRow,
    setFlag: toggle,
    archive: (uids: number[]) => moveTo(roles.archive, uids),
    junk: (uids: number[]) => moveTo(roles.junk, uids),
    remove: removeRow,
    toggleThread: toggleExpanded,
    dragStart: onRowDragStart,
    dragEnd: () => setDraggingUids(null),
  })

  // The page index resets on its own; the DOM scroll position does not, and would drop the
  // reader into the middle of a folder whose blocks are not loaded, or leave a fresh search
  // scrolled to wherever the previous list or search left it.
  useEffect(() => { if (scrollRef.current) scrollRef.current.scrollTop = 0 }, [resetKey])

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

    // The sentinel counts list rows, and a collapsed thread is one row: groups, not messages.
    const sentinelRow = streaming?.hasMore ? sentinelIndexOf(groups.length) : -1
    let rowIndex = 0
    let drawn = 0
    const nextRow = () => rowOffset + (drawn += 1)

    /** One row; with `members` a whole conversation. `index` runs over the flattened members,
        the order `loadedUids` publishes, so a shift-range stays coherent across both shapes. */
    const row = (message: MailMessageSummary, groupKey: number, index: number,
      members?: MailMessageSummary[], member = false) => {
      const uids = rowUidsOf(message, members)
      return (
        <MessageRow
          key={message.uid}
          message={message}
          members={members}
          groupKey={groupKey}
          expanded={expanded.has(groupKey)}
          member={member}
          rowIndex={index}
          ariaRow={nextRow()}
          today={today}
          wide={wide}
          drafts={drafts}
          crossFolder={crossFolder}
          showsPreview={showsPreview}
          rowActions={rowActions}
          checked={uids.every(uid => selected.has(uid))}
          // The collapsed row stands for every member, so it highlights whichever of them is
          // open — reading an older member then collapsing must not leave the list unselected.
          open={selectedUid !== null && uids.includes(selectedUid)}
          // Fading out: the star and read/unread write flags straight away and would race the
          // move this row is playing out. The three cluster actions are already inert.
          leaving={uids.some(uid => departing.has(uid))}
          dragging={draggingUids?.has(message.uid) ?? false}
          archiveOff={archiveOff}
          archiveReason={archiveReason}
          junkOff={junkOff}
          junkReason={junkReason}
          trashOff={trashOff}
          trashReason={trashReason}
          deleteLabel={deleteLabel}
          on={rowOn}
        />
      )
    }

    return (
      <>
        <MessageGrid label={t('list.gridLabel')} rowCount={rowCount} selecting={count > 0}>
          {groups.map((group, groupIndex) => {
            const single = group.messages.length === 1
            const startIndex = rowIndex
            rowIndex += group.messages.length
            const isOpen = expanded.has(group.key)
            return (
              <Fragment key={group.key}>
                {streaming && groupIndex === sentinelRow && <LoadMoreSentinel onReach={streaming.loadMore} />}
                {row(group.messages[0], group.key, startIndex, single ? undefined : group.messages)}
                {/* The parent stands for the thread; unfolded, every member gets its own line,
                    the latest included. */}
                {isOpen && !single &&
                  group.messages.map((m, i) => row(m, group.key, startIndex + i, undefined, true))}
              </Fragment>
            )
          })}
        </MessageGrid>

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
    // The exit's duration is written once, here, from the constant the hook waits on: the keyframes
    // hold the split as percentages, so the two can never drift.
    <div
      className="message-list-root"
      role="presentation"
      style={{ '--row-exit': `${ROW_EXIT_MS}ms` } as CSSProperties}
      onKeyDown={onListKeyDown}
    >
      {/* Replaces the old heading band: the toolbar names the folder until a selection is on. */}
      <SelectionToolbar
        leading={leading}
        refresh={onRefresh && inDrawer ? { onRun: onRefresh } : undefined}
        title={folderName || folderPath}
        count={count}
        allSelected={allSelected}
        indeterminate={indeterminate}
        onToggleAll={() => allSelected ? selection.clear() : selection.selectAll(loadedUids)}
        overCap={overCap}
        deleteLabel={deleteLabel}
        archive={{ onRun: () => bulkMove(roles.archive, false), disabledReason: archiveOff ? archiveReason : undefined }}
        junk={{ onRun: () => bulkMove(roles.junk, false), disabledReason: junkOff ? junkReason : undefined }}
        del={{ onRun: bulkDelete, disabledReason: trashOff ? trashReason : undefined }}
        move={{ onRun: () => setPicker({ mode: 'move' }) }}
        copy={{ onRun: () => setPicker({ mode: 'copy' }) }}
        markRead={{ onRun: () => bulkMark(true) }}
        markUnread={{ onRun: () => bulkMark(false) }}
        emptyFolder={{ onRun: requestEmpty,
          // Emptying acts on the whole real folder, so it is off under a search: its reason would
          // otherwise read off the search total, and the non-purge branch fires with no confirm.
          // A purge already on the wire closes this door too, or the banner beside it is greyed
          // while the same action stays live one menu away.
          disabledReason: emptyFolder.isPending ? t('list.emptying')
            : searching ? t('list.clearSearchFirst') : emptyReason }}
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
      {!searching && (
        <EmptyFolderBanner role={folderRole ?? null} total={total} onEmpty={requestEmpty}
          busy={emptyFolder.isPending} />
      )}

      <div className="mail-list-scroll" ref={scrollRef}>
        {/* Not aria-live: the region would carry its text before it ever changes, so most stacks
            announce nothing, and where one does it fires on every wobble of a gesture a
            screen-reader user in explore-by-touch cannot perform anyway. */}
        {pull > 0 && (
          <div className="mail-pull" style={{ height: pull }}>
            {t(armed ? 'list.release' : 'list.pull')}
          </div>
        )}
        {rows()}
      </div>

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
          entityLabel={expunging.label}
          onConfirm={expunge}
          onClose={() => setExpunging(null)}
          loading={deleteMessages.isPending}
          returnFocusRef={regionRef}
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
          returnFocusRef={regionRef}
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
          returnFocusRef={regionRef}
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
