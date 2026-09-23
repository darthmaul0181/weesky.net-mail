import { lazy, Suspense, useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import type { CSSProperties } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Link, useLocation, useMatch, useNavigate, useSearchParams } from 'react-router'
import type { SearchCriteria } from './list/searchCriteria'
import { PRIMARY_ACCOUNT_ID, useAuth } from '../../contexts/AuthContext'
import LoadingBlock from '../../components/LoadingBlock'
import Toasts from '../../components/Toasts'
import { useToasts } from '../../hooks/useToasts'
import { flatten } from './folders/folderNodes'
import FolderTree from './folders/FolderTree'
import RocketIcon from '../../icons/RocketIcon'
import IdentityMenu from '../../layouts/IdentityMenu'
import MessageList from './list/MessageList'
import { nextUidOf } from './list/nextUid'
import { useListRefresh } from './list/useListRefresh'
import { useRowExit } from './list/useRowExit'
import MessageReader from './reader/MessageReader'
import type { DragPayload } from './list/dragMessages'
import {
  identitiesQueryOptions, useAccountId, useFolders, useIdentities, useMailRefresh,
  useMoveMessages, useOpenDraft,
} from './queries'
import RefreshButton from './folders/RefreshButton'
import { buildDraftSeed } from './compose/composeSeed'
import { roleLabel } from './roleLabel'
import { readingPaneOf, showFolderIconsOf, usePreferences } from '../../hooks/usePreferences'
import { apiErrorMessage } from '../../lib/apiErrorMessage'
import PaneSplitter from './split/PaneSplitter'
import { usePaneSize } from './split/usePaneSize'
import ContextDrawer, { DrawerToggle, useContextDrawer } from '../../layouts/ContextDrawer'
import FloatingAction from '../../components/FloatingAction'
import { useViewport } from '../../hooks/useViewport'
import { reachable } from '../../hooks/useLayer'
import { effectivePane } from './effectivePane'

// Lazy: pulls in squire-rte, which every /mail visitor would otherwise download unread.
const ComposeView = lazy(() => import('./compose/ComposeView'))

/** A connected account whose stored password no longer decrypts. The backend answers 409 and
    not 401 on purpose: the global 401 handler signs the whole session out, which is the wrong
    answer to one account's problem. */
function needsAccountPassword(error: unknown): boolean {
  const failure = error as { status?: number; code?: string } | null
  return failure?.status === 409 && failure.code === 'connected_credentials_invalid'
}

function resultFolderOf(state: unknown): string | null {
  return typeof state === 'object' && state !== null && 'resultFolder' in state
    && typeof state.resultFolder === 'string' ? state.resultFolder : null
}

// Selection lives in search params, not route segments: a folder path may contain '/'.
export default function MailLayout() {
  const { t } = useTranslation('mail')
  const [params, setParams] = useSearchParams()
  // A functional `setParams` updater that returns its input still navigates (one more Back),
  // so callbacks decide from this, kept current before any event can read it.
  const paramsRef = useRef(params)
  useLayoutEffect(() => { paramsRef.current = params }, [params])
  const composing = useMatch('/mail/compose') != null
  const navigate = useNavigate()
  const accountId = useAccountId()
  const { accountsLoading, activeAccount } = useAuth()
  // Until the list lands, a stored connected id may yet turn out to be stale, and a mailbox drawn
  // from it would be the wrong one. The primary can never be stale, so it never waits.
  const settling = accountsLoading && accountId !== PRIMARY_ACCOUNT_ID
  const { data: folders, isLoading, isError, error } = useFolders(!settling)
  const { refresh, fetching: refreshFetching } = useMailRefresh()
  const { toasts, addToast, removeToast, pauseToast, resumeToast } = useToasts()
  const moveMessages = useMoveMessages(addToast)
  // Owned here rather than in the list: the drop on a folder and the reader's own actions remove
  // rows too, and three sets would let one surface animate a row another has already dropped.
  const rowExit = useRowExit()
  const openDraft = useOpenDraft()
  const { data: identityList } = useIdentities()
  const queryClient = useQueryClient()

  const folder = params.get('folder')
  const uidParam = params.get('uid')
  const uid = uidParam ? Number(uidParam) : null

  const [search, setSearch] = useState<SearchCriteria | null>(null)
  // The folder a cross-folder result was opened from, carried by its history entry so it lands with
  // the uid. Honoured only while a search stands and a message is open, or it would contradict the
  // URL folder's list or name a hit nothing can wind back to.
  const entryState: unknown = useLocation().state
  const resultFolder = search && uid !== null ? resultFolderOf(entryState) : null

  // A search belongs to the folder it was typed in: navigating away drops it (render-time reset,
  // the useMessageList pattern, not an effect).
  const [searchFolder, setSearchFolder] = useState(folder)
  if (folder !== searchFolder) {
    setSearchFolder(folder)
    setSearch(null)
  }

  useListRefresh(folder, !settling)

  // The folder the user last named in the tree, with the account whose tree it came from. It
  // discriminates one account change and is consumed by it: the difference between an exit that
  // carries the previous account's folder and one the user just chose in this account's.
  const picked = useRef<{ path: string; accountId: string } | null>(null)

  // On a change, never on mount: the old mailbox's folder and uid name nothing in the new one. Held
  // while composing, ref included, since the leave guard may refuse this navigation; `composing` is a
  // dependency, so leaving the composer fires the held reset.
  const lastAccount = useRef(accountId)
  useEffect(() => {
    if (composing || lastAccount.current === accountId) return
    lastAccount.current = accountId
    // Consumed, not merely read: a pick left standing would spare a later switch the reset it
    // needs — same folder, same account, and the other mailbox's uid rides back in.
    const pick = picked.current
    picked.current = null
    // A folder the user picked in *this* account's tree already names something it has: the URL
    // no longer points at the previous mailbox, and resetting would throw the click away. A uid
    // never survives either way — a message id means nothing in another mailbox.
    if (uid === null && folder !== null
      && pick?.accountId === accountId && pick.path === folder) return
    void navigate('/mail', { replace: true })
  }, [accountId, composing, folder, uid, navigate])

  // The list heading shows the same label as the tree: the role label when the folder has a
  // role, the leaf name otherwise — never the full path, which reads "INBOX.Linux server"
  // under a '.' separator.
  const folderNode = folders && folder
    ? flatten(folders).find(entry => entry.node.path === folder)?.node
    : undefined
  const folderName = folderNode
    ? (folderNode.specialUse ? roleLabel(folderNode.specialUse, t) : folderNode.name)
    : undefined

  // Found by role rather than the name "INBOX"; no uid, which message to read stays the user's call.
  // Replaces the entry so Back leaves mail. Never while composing: the leave guard would have to
  // question the redirect.
  useEffect(() => {
    if (composing || folder || !folders) return

    const inbox = flatten(folders).find(entry => entry.node.specialUse === 'inbox')
    if (inbox) setParams({ folder: inbox.node.path }, { replace: true })
  }, [composing, folder, folders, setParams])

  function selectFolder(path: string) {
    // Recorded with the tree it came from, not acted on here: the blocker may refuse this
    // navigation, and only the URL that actually lands tells the reset whether it still has
    // something to drop.
    picked.current = { path, accountId }
    // While composing this is a navigation out of /mail/compose; the ComposeView blocker owns
    // the "discard?" question. Otherwise it drops uid: a message id means nothing elsewhere.
    if (composing) void navigate(`/mail?folder=${encodeURIComponent(path)}`)
    else setParams({ folder: path })
  }

  const openCompose = useCallback(() => {
    void navigate('/mail/compose', { state: { from: folder } })
  }, [navigate, folder])

  function selectMessage(nextUid: number) {
    if (!folder) return
    // A draft opens as an editor, not a reading pane — the row is the account's own unsent text.
    if (folderNode?.specialUse === 'drafts') { void openDraftInComposer(nextUid); return }
    setParams({ folder, uid: String(nextUid) })
  }

  async function openDraftInComposer(draftUid: number) {
    // A double-click stages the draft's parts twice; the losing set is left to the TTL sweeper.
    if (openDraft.isPending) return
    try {
      const opened = await openDraft.mutateAsync({ folder: folder!, uid: draftUid })
      // An unresolved query is not "no identities": seeding from [] rewrites the draft's From to
      // the default. A failed fetch still falls back rather than blocking the draft from opening.
      const identities = identityList ?? await queryClient
        .ensureQueryData(identitiesQueryOptions(accountId))
        .then(list => list.identities, () => [])
      const seed = buildDraftSeed(
        opened, identities, { folderPath: folder!, uid: draftUid }, accountId)
      void navigate('/mail/compose', { state: { from: folder, seed } })
    } catch (error) {
      addToast(apiErrorMessage(error, t('layout.draftOpenFailed')), 'error')
    }
  }

  const changeSearch = useCallback((criteria: SearchCriteria | null) => {
    setSearch(criteria)
    if (criteria !== null) return
    // Clearing while a cross-folder result is open: its uid means nothing in the URL folder.
    if (resultFolder === null) return
    const path = paramsRef.current.get('folder')
    if (path) setParams({ folder: path })
  }, [resultFolder, setParams])

  // A hit from another folder opens where it lives: the URL folder stays put, the reader reads
  // from resultFolder instead.
  const openResult = useCallback((nextUid: number, fromFolder: string) => {
    if (!folder) return
    setParams({ folder, uid: String(nextUid) },
      fromFolder === folder ? undefined : { state: { resultFolder: fromFolder } })
  }, [folder, setParams])

  const viewport = useViewport()
  const { data: preferences, isLoading: preferencesLoading } = usePreferences()
  // Until the preferences answer, today's layout — the list already waits on the same query,
  // so nothing meaningful can flash in the wrong arrangement.
  const pane = effectivePane(preferences ? readingPaneOf(preferences) : 'right', viewport)
  const drawer = useContextDrawer()
  const [listWidth, setListWidth] = usePaneSize('mail.split.right', 380, 240)
  const [listHeight, setListHeight] = usePaneSize('mail.split.bottom', 280, 120)

  function closeMessage() {
    if (folder) setParams({ folder })
  }

  // A ref, not state: which rows are on screen only matters at the moment one of them leaves,
  // and re-rendering the whole module on every list refresh would be a needless cost.
  const rowsRef = useRef<number[]>([])
  const keepRows = useCallback((uids: number[]) => { rowsRef.current = uids }, [])
  // Set when a departure closes the reader. The URL lands a commit after the confirm that handed
  // focus to the reader column, so the column may leave holding it: the list takes it back then.
  const readerClosing = useRef(false)

  // The row is gone from the cache the instant the action fires, so the selection follows now
  // rather than after a refetch: the next message, or the reader closes.
  const departed = useCallback((open: number, batch: number[] = [open]) => {
    const previous = paramsRef.current
    if (Number(previous.get('uid')) !== open) return
    const path = previous.get('folder')
    if (!path) return

    // A bulk action removes the whole batch: skip every member, not just the open row.
    const next = nextUidOf(rowsRef.current, open, batch)
    const params: Record<string, string> = { folder: path }
    if (next !== null) params.uid = String(next)
    readerClosing.current = next === null
    setParams(params, { state: entryState })
  }, [entryState, setParams])

  // A drop reuses the same optimistic move the toolbar fires; the payload already names its source
  // folder. If the open message is in the batch, the reader advances past it like any bulk action.
  const dropMessages = useCallback((targetFolderPath: string, payload: DragPayload) => {
    rowExit.depart(payload.uids, () => moveMessages.mutate({
      folderPath: payload.sourcePath, uids: payload.uids, targetFolderPath, copy: false,
    }))
    if (uid !== null && payload.uids.includes(uid)) departed(uid, payload.uids)
  }, [moveMessages, rowExit, uid, departed])

  // `wide` is the one-line row layout, whose .message-row-from is pinned at 180px — half of a
  // 360px screen for the sender alone. A phone always takes the stacked one.
  const wideRows = viewport !== 'phone' && pane !== 'right'

  // The columns a confirmed delete hands focus back to when it takes its own button with it — the
  // way `CalendarLayout` holds one for `.calendar-main`. Only one arrangement below ever mounts.
  const listRegion = useRef<HTMLDivElement>(null)
  const readerRegion = useRef<HTMLDivElement>(null)
  // In `none` neither column is always there: the list is `display: none` while a message is open,
  // and the reader leaves with the last one. Resolved when focus is handed back, not at render.
  const noSplitRegion = useMemo(
    () => ({ get current() { return readerRegion.current ?? listRegion.current } }), [])
  useLayoutEffect(() => {
    if (uid !== null || !readerClosing.current) return
    readerClosing.current = false
    if (!reachable(document.activeElement)) listRegion.current?.focus()
  }, [uid])

  // One place for the column: its region ref and `tabIndex` would otherwise be written in three
  // branches and forgotten in one.
  const listColumn = (selected: number | null, style?: CSSProperties, hidden = false) => (
    <div className={`mail-list${hidden ? ' is-hidden' : ''}`} ref={listRegion} tabIndex={-1}
      style={style}>
      <MessageList
        folderPath={folder}
        folderName={folderName}
        folderRole={folderNode?.specialUse ?? null}
        selectedUid={selected}
        onSelect={selectMessage}
        wide={wideRows}
        leading={drawer.inDrawer ? <DrawerToggle onClick={drawer.toggle} /> : null}
        onRefresh={refresh}
        inDrawer={drawer.inDrawer}
        onNotify={addToast}
        onRows={keepRows}
        onDeparted={departed}
        rowExit={rowExit}
        search={search}
        onSearchChange={changeSearch}
        onOpenResult={openResult}
        regionRef={listRegion}
      />
    </div>
  )

  // The reader follows the open cross-folder result, if any, back to the URL folder otherwise.
  const readerFolder = resultFolder ?? folder
  const readerNode = folders && readerFolder
    ? flatten(folders).find(entry => entry.node.path === readerFolder)?.node
    : undefined

  if (settling) return <div className="mail-full-pane"><LoadingBlock /></div>

  // A mailbox that cannot be opened at all. Never over an open composer: this replaces the subtree by
  // re-render, which no leave guard sees, so a poll answering 409 would discard an unsaved draft.
  // An OAuth mailbox gets the same 409 when consent was withdrawn or its token cipher no longer opens.
  if (!composing && needsAccountPassword(error)) {
    const byConsent = activeAccount?.authMode === 'OAuth2'
    return (
      <div className="mail-full-pane">
        <h2>{t(byConsent ? 'identity.signInNeeded' : 'identity.passwordNeeded', { ns: 'common' })}</h2>
        <p>{t(byConsent ? 'blocked.consentBody' : 'blocked.passwordBody')}</p>
        <Link className="btn btn-primary" to="/settings/accounts">
          {t(byConsent ? 'blocked.reconnect' : 'blocked.enterPassword')}
        </Link>
      </div>
    )
  }

  // Each column is a band stack: what scrolls is the middle band only, so the folder actions and
  // the pager stay put instead of hiding below their own content.
  const folderColumn = (
    <div className="mail-folders">
      <div className="column-actions">
        <button type="button" className="btn btn-primary column-actions-main" onClick={openCompose}>
          <RocketIcon size={15} /> {t('layout.newMessage')}
        </button>
        <RefreshButton fetching={refreshFetching} onRefresh={refresh} />
      </div>
      <div className="mail-folders-scroll">
        {/* The tree waits on the preferences too, or an account with folder icons on sees the names
            shift late. An errored preferences query still resolves, and the tree draws without icons. */}
        {(isLoading || preferencesLoading) && <p className="mail-empty">{t('folders.loading')}</p>}
        {isError && <p className="mail-empty">{t('folders.loadFailed')}</p>}
        {folders && !preferencesLoading && (
          <FolderTree folders={folders} selectedPath={folder} onSelect={selectFolder}
            onDropMessages={dropMessages}
            showIcons={preferences ? showFolderIconsOf(preferences) : false} />
        )}
      </div>

      {/* Shows even while the tree loads: the account block does not depend on it. */}
      <div className="mail-folders-footer">
        <IdentityMenu />
      </div>
    </div>
  )

  return (
    <div className={`mail-layout is-${pane}`}>
      {drawer.inDrawer
        ? <ContextDrawer open={drawer.open} onClose={drawer.close}>{folderColumn}</ContextDrawer>
        : folderColumn}

      {/* Composing takes the whole list+reader side; the folder tree stays where it was. */}
      {composing ? (
        <div className="mail-compose">
          <Suspense fallback={null}><ComposeView onNotify={addToast} /></Suspense>
        </div>
      ) : (
        <>
          {pane === 'right' && (
            <div className="mail-row">
              {listColumn(uid, { width: listWidth })}
              {preferences && (
                <PaneSplitter
                  orientation="vertical" size={listWidth} defaultSize={380} min={240} reserve={320}
                  onResize={setListWidth}
                />
              )}
              <div className="mail-reader">
                <MessageReader folderPath={readerFolder} uid={uid} folderRole={readerNode?.specialUse ?? null}
                  onDeparted={departed} depart={rowExit.depart} onNotify={addToast}
                  regionRef={listRegion} />
              </div>
            </div>
          )}

          {pane === 'bottom' && (
            <div className="mail-stack">
              {listColumn(uid, { height: listHeight })}
              <PaneSplitter
                orientation="horizontal" size={listHeight} defaultSize={280} min={120} reserve={160}
                onResize={setListHeight}
              />
              <div className="mail-reader">
                <MessageReader folderPath={readerFolder} uid={uid} folderRole={readerNode?.specialUse ?? null}
                  onDeparted={departed} depart={rowExit.depart} onNotify={addToast}
                  regionRef={listRegion} />
              </div>
            </div>
          )}

          {pane === 'none' && (
            <>
              {/* Hidden, never unmounted: the scroll position and the streamed blocks live in this
                  subtree. No selected row either — there is no message "open beside". */}
              {listColumn(null, undefined, uid !== null)}
              {uid !== null && (
                <div className="mail-reader" ref={readerRegion} tabIndex={-1}>
                  <MessageReader folderPath={readerFolder} uid={uid} folderRole={readerNode?.specialUse ?? null}
                    bottomActions={viewport === 'phone'} regionRef={noSplitRegion}
                    onBack={closeMessage} onDeparted={departed} depart={rowExit.depart} onNotify={addToast} />
                </div>
              )}
            </>
          )}
        </>
      )}

      {/* Not over a phone reader: its bar owns the foot of the screen and the button would cover
          delete and the kebab. A tablet at `none` keeps it: no bar, and the column's Compose is hidden. */}
      {!composing && !(viewport === 'phone' && pane === 'none' && uid !== null) && (
        <FloatingAction label={t('layout.newMessage')} onClick={openCompose}>
          <RocketIcon size={22} />
        </FloatingAction>
      )}

      <Toasts toasts={toasts} onRemove={removeToast} onPause={pauseToast} onResume={resumeToast} />
    </div>
  )
}
