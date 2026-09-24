import { Fragment, useEffect, useMemo, useRef, useState } from 'react'
import type { CSSProperties, DragEvent, KeyboardEvent, ReactNode, RefObject } from 'react'
import { useTranslation } from 'react-i18next'
import {
  DEFAULT_ROW_ACTIONS, requestSizeOf, rowActionsOf, showPreviewOf, usePreferences,
} from '../../../hooks/usePreferences'
import type { RowAction } from '../../../hooks/usePreferences'
import type { MailMessageSummary, MailSearchResult, SpecialUse } from '../api/mailTypes'
import { hasOpenLayer } from '../../../lib/layerStack'
import { useDeleteMessages, useFolders, useMoveMessages, useSetFlags } from '../queries'
import { useRoleActions } from '../useRoleActions'
import EmptyFolderBanner from './EmptyFolderBanner'
import SearchBar from './SearchBar'
import SearchResultsBanner from './SearchResultsBanner'
import { isStarredOnly, labelOf } from './searchCriteria'
import type { SearchCriteria } from './searchCriteria'
import { DRAG_MIME, dragUids, serializeDrag } from './dragMessages'
import { buildDragPill, setDragPill } from './dragImage'
import MessageRow, { rowUidsOf } from './MessageRow'
import type { CheckGesture, RowCallbacks } from './MessageRow'
import MessageGrid from './MessageGrid'
import ListDialogs from './ListDialogs'
import { memberUids } from './threading'
import LoadMoreSentinel from './LoadMoreSentinel'
import { sentinelIndexOf } from './messageStream'
import Pagination from './Pagination'
import SelectionToolbar from './SelectionToolbar'
import { useSelection } from './useSelection'
import { useBulkActions } from './useBulkActions'
import { useListSearch } from './useListSearch'
import { useKeyedState } from '../../../hooks/useKeyedState'
import { ROW_EXIT_MS } from './useRowExit'
import type { RowExit } from './useRowExit'
import { useMessageList } from './useMessageList'
import { useForwarders } from '../../../hooks/useForwarders'
import { useToday } from '../../../hooks/useToday'
import { usePullToRefresh } from '../../../hooks/usePullToRefresh'

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

// Three bands (heading, rows, footer); only the rows scroll, so the pager never sits past the last row.
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

  const pageSize = preferences ? requestSizeOf(preferences) : 0
  const listSearch = useListSearch(folderPath, search, onSearchChange, pageSize)
  const { searchOpen, searchPage, searching, crossFolder, starred, searchView } = listSearch

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
  const { data: folders } = useFolders()
  const {
    roles, inTrash, archiveOff, archiveReason, junkOff, junkReason, trashOff, trashReason, deleteLabel,
  } = useRoleActions(folders, folderRole)
  // Named for the confirm dialog; the uids are the whole thread when the row is one.
  const [expunging, setExpunging] = useState<{ label: string; uids: number[] } | null>(null)
  // A Set, not the array: `includes` per row is quadratic across the page, and a drag carrying
  // the whole selection is exactly when the page is longest.
  const [draggingUids, setDraggingUids] = useState<Set<number> | null>(null)
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
  const [expanded, setExpanded] = useKeyedState<Set<number>>(() => new Set(), resetKey)
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

  const bulk = useBulkActions({
    folderPath, inTrash, purges, trashPath: roles.trash, selectedUids, selectedUid, selection,
    rowExit, onDeparted, onNotify, moveMessages, deleteMessages, setFlags,
  })

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
    const [first] = uids
    if (first === undefined) return
    if (uids.length === 1) { onDeparted?.(first); return }
    if (selectedUid !== null && uids.includes(selectedUid)) onDeparted?.(selectedUid, uids)
    else onDeparted?.(first, uids)
  }

  // The reader is told at the click and the list takes its time: only the rows are animating, and
  // an open message that waits 300ms to be replaced reads as the action having missed.
  function moveTo(target: string | null, uids: number[]) {
    if (!folderPath || !target) return
    rowExit.depart(uids, () =>
      moveMessages.mutate({ folderPath, uids, targetFolderPath: target, copy: false }))
    reportDeparted(uids)
  }

  function onRowDragStart(event: DragEvent<HTMLDivElement>, rowUids: number[]) {
    if (crossFolder || !folderPath) return
    const uids = dragUids(selectedUids, rowUids)
    event.dataTransfer.setData(DRAG_MIME, serializeDrag({ sourcePath: folderPath, uids }))
    event.dataTransfer.effectAllowed = 'move'
    setDragPill(event.dataTransfer, buildDragPill(uids.length))
    setDraggingUids(new Set(uids))
  }

  function expunge() {
    if (!folderPath || !expunging) return
    const uids = expunging.uids
    rowExit.depart(uids, () => deleteMessages.mutate({ folderPath, uids }))
    setExpunging(null)
    reportDeparted(uids)
  }

  // A cross-folder hit belongs to another folder: it opens there. Otherwise it is the open folder.
  function openRow(message: MailMessageSummary) {
    if (crossFolder) onOpenResult?.(message.uid, (message as MailSearchResult).folderPath)
    else onSelect(message.uid)
  }

  function checkRow(uids: number[], index: number, { was, whole, shift }: CheckGesture) {
    if (whole) selection.setMany(uids, !was)
    else if (shift) selection.toggleRange(loadedUids, index)
    else if (uids[0] !== undefined) selection.toggle(uids[0], index)
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
    // display:contents: no box of its own, so the bands still stack under .mail-list, yet its keydown
    // catches Escape from the toolbar too. The exit duration comes from the constant the hook waits on.
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
        archive={{ onRun: () => bulk.bulkMove(roles.archive, false), disabledReason: archiveOff ? archiveReason : undefined }}
        junk={{ onRun: () => bulk.bulkMove(roles.junk, false), disabledReason: junkOff ? junkReason : undefined }}
        del={{ onRun: bulk.bulkDelete, disabledReason: trashOff ? trashReason : undefined }}
        move={{ onRun: () => bulk.setPicker({ mode: 'move' }) }}
        copy={{ onRun: () => bulk.setPicker({ mode: 'copy' }) }}
        markRead={{ onRun: () => bulk.bulkMark(true) }}
        markUnread={{ onRun: () => bulk.bulkMark(false) }}
        emptyFolder={{ onRun: bulk.requestEmpty,
          // Off under a search, whose total the reason would read and where the non-purge branch has no
          // confirm; off during a purge too, or the greyed banner's action stays live one menu away.
          disabledReason: bulk.emptying ? t('list.emptying')
            : searching ? t('list.clearSearchFirst') : emptyReason }}
        searchOpen={searchOpen}
        onToggleSearch={listSearch.toggleSearch}
        starred={starred}
        onToggleStarred={listSearch.toggleStarred}
        starredDisabled={!starred && !folderPath}
        selectionDisabled={crossFolder}
      />

      {searchOpen && folderPath && (
        <SearchBar
          folderTitle={folderName || folderPath}
          onSearch={listSearch.quickSearch}
          onOpenAdvanced={text => listSearch.setAdvanced({ subject: text })}
          onClose={listSearch.closeSearch}
        />
      )}

      {/* The lit star is the whole indication when it stands alone, so the heading keeps the
          folder name; any other criterion hands it back to the banner. */}
      {search && !isStarredOnly(search) && (
        <SearchResultsBanner
          total={listSearch.searchTotal}
          label={labelOf(search)}
          onClear={listSearch.closeSearch}
        />
      )}

      {/* The empty-folder offer belongs to the folder itself, not to a search laid over it. */}
      {!searching && (
        <EmptyFolderBanner role={folderRole ?? null} total={total} onEmpty={bulk.requestEmpty}
          busy={bulk.emptying} />
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

      <ListDialogs
        folderPath={folderPath}
        folderTitle={folderName || folderPath}
        folders={folders ?? []}
        regionRef={regionRef}
        expunging={expunging}
        onExpunge={expunge}
        onCloseExpunge={() => setExpunging(null)}
        deleting={deleteMessages.isPending}
        bulk={bulk}
        advanced={listSearch.advanced}
        onAdvancedSearch={listSearch.advancedSearch}
        onCloseAdvanced={() => listSearch.setAdvanced(null)}
      />
    </div>
  )
}
