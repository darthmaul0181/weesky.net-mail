import { lazy, Suspense, useCallback, useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { Link, useMatch, useNavigate, useSearchParams } from 'react-router-dom'
import type { SearchCriteria } from './list/searchCriteria'
import { PRIMARY_ACCOUNT_ID, useAuth } from '../../contexts/AuthContext'
import LoadingBlock from '../../components/LoadingBlock'
import Toasts from '../../components/Toasts.jsx'
import { useToasts } from '../../hooks/useToasts.js'
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

/**
 * The mail module's three columns. The shell provides a single outlet, so a module builds its
 * own columns inside it — the same way the settings section does.
 *
 * Selection lives in search params rather than route segments: a folder path may contain '/',
 * which is exactly why folder paths stay out of route segments in the API too. Deep links and
 * the back button still work.
 */
export default function MailLayout() {
  const { t } = useTranslation('mail')
  const [params, setParams] = useSearchParams()
  const composing = useMatch('/mail/compose') != null
  const navigate = useNavigate()
  const accountId = useAccountId()
  const { accountsLoading, activeAccount } = useAuth()
  // Until the list lands, a stored connected id may yet turn out to be stale, and a mailbox drawn
  // from it would be the wrong one. The primary can never be stale, so it never waits.
  const settling = accountsLoading && accountId !== PRIMARY_ACCOUNT_ID
  const { data: folders, isLoading, isError, error } = useFolders(!settling)
  const { refresh, fetching: refreshFetching } = useMailRefresh()
  const { toasts, addToast, removeToast } = useToasts()
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
  // The folder a cross-folder result was opened from; null when the reader shows the URL folder.
  const [resultFolder, setResultFolder] = useState<string | null>(null)

  // A search belongs to the folder it was typed in: navigating away drops it (render-time reset,
  // the useMessageList pattern, not an effect).
  const [searchFolder, setSearchFolder] = useState(folder)
  if (folder !== searchFolder) {
    setSearchFolder(folder)
    setSearch(null)
    setResultFolder(null)
  }
  // The reader closed (departed past the last row, Back): a stale cross-folder origin must not
  // survive to relabel the next open.
  if (uid === null && resultFolder !== null) setResultFolder(null)

  useListRefresh(folder, !settling)

  // The folder the user last named in the tree, with the account whose tree it came from. It
  // discriminates one account change and is consumed by it: the difference between an exit that
  // carries the previous account's folder and one the user just chose in this account's.
  const picked = useRef<{ path: string; accountId: string } | null>(null)

  // On a change, never on mount: the previous mailbox's folder and uid name nothing in the new
  // one, while a deep link into a folder is a legitimate way in. The inbox redirect takes over.
  // Held while composing, ref included: the composer's leave guard blocks this navigation and can
  // refuse it, and a ref advanced by then would swallow a reset that never happened. `composing`
  // is a dependency, so leaving the composer is what fires the held reset.
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
    navigate('/mail', { replace: true })
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

  // Landing on three empty columns asks the user to pick the one folder everybody starts in.
  // The inbox comes from the resolution chain's role rather than the name "INBOX", so a server
  // that names it otherwise still lands right. No uid: which message to read stays the user's
  // call. Replaces the entry, or Back would bounce off the redirect instead of leaving mail.
  // Never while composing: the composer names no folder, and the redirect would be a navigation
  // the leave guard then has to question.
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
    if (composing) navigate(`/mail?folder=${encodeURIComponent(path)}`)
    else setParams({ folder: path })
  }

  const openCompose = useCallback(() => {
    navigate('/mail/compose', { state: { from: folder } })
  }, [navigate, folder])

  function selectMessage(nextUid: number) {
    if (!folder) return
    // A draft opens as an editor, not a reading pane — the row is the account's own unsent text.
    if (folderNode?.specialUse === 'drafts') { void openDraftInComposer(nextUid); return }
    setResultFolder(null)
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
      navigate('/mail/compose', { state: { from: folder, seed } })
    } catch (error) {
      addToast(apiErrorMessage(error, t('layout.draftOpenFailed')), 'error')
    }
  }

  const changeSearch = useCallback((criteria: SearchCriteria | null) => {
    setSearch(criteria)
    if (criteria !== null) return
    // Clearing while a cross-folder result is open: its uid means nothing in the URL folder.
    if (resultFolder !== null) setParams(previous => {
      const path = previous.get('folder')
      return path ? { folder: path } : previous
    })
    setResultFolder(null)
  }, [resultFolder, setParams])

  // A hit from another folder opens where it lives: the URL folder stays put, the reader reads
  // from resultFolder instead.
  const openResult = useCallback((nextUid: number, fromFolder: string) => {
    if (!folder) return
    setResultFolder(fromFolder === folder ? null : fromFolder)
    setParams({ folder, uid: String(nextUid) })
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

  // The row is gone from the cache the instant the action fires, so the selection follows now
  // rather than after a refetch: the next message, or the reader closes.
  const departed = useCallback((open: number, batch: number[] = [open]) => {
    setParams(previous => {
      if (Number(previous.get('uid')) !== open) return previous
      const path = previous.get('folder')
      if (!path) return previous

      // A bulk action removes the whole batch: skip every member, not just the open row.
      const next = nextUidOf(rowsRef.current, open, batch)
      const params: Record<string, string> = { folder: path }
      if (next !== null) params.uid = String(next)
      return params
    })
  }, [setParams])

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

  const list = (selected: number | null) => (
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
    />
  )

  // The reader follows the open cross-folder result, if any, back to the URL folder otherwise.
  const readerFolder = resultFolder ?? folder
  const readerNode = folders && readerFolder
    ? flatten(folders).find(entry => entry.node.path === readerFolder)?.node
    : undefined

  if (settling) return <div className="mail-full-pane"><LoadingBlock /></div>

  // Not a column that failed to load but a mailbox that cannot be opened at all, so the three
  // columns would only frame three copies of the same failure. Never over an open composer: this
  // replaces the subtree by re-render, which no leave guard can see, so a poll answering 409
  // would discard an unsaved draft without asking. It waits until the composer is left.
  // An OAuth mailbox reaches the same 409 with no password anywhere in the story: the consent was
  // withdrawn at the provider, or the cipher holding its refresh token no longer opens.
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
        {/* The tree waits on the preferences too: without that, an account that turned the
            icons on gets a column that appears late and pushes every name sideways. The two
            queries leave together, so it costs nothing in the ordinary case — and an errored
            preferences query still resolves, leaving the tree to draw without icons. */}
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
              <div className="mail-list" style={{ width: listWidth }}>{list(uid)}</div>
              {preferences && (
                <PaneSplitter
                  orientation="vertical" size={listWidth} defaultSize={380} min={240} reserve={320}
                  onResize={setListWidth}
                />
              )}
              <div className="mail-reader">
                <MessageReader folderPath={readerFolder} uid={uid} folderRole={readerNode?.specialUse ?? null}
                  onDeparted={departed} depart={rowExit.depart} onNotify={addToast} />
              </div>
            </div>
          )}

          {pane === 'bottom' && (
            <div className="mail-stack">
              <div className="mail-list" style={{ height: listHeight }}>{list(uid)}</div>
              <PaneSplitter
                orientation="horizontal" size={listHeight} defaultSize={280} min={120} reserve={160}
                onResize={setListHeight}
              />
              <div className="mail-reader">
                <MessageReader folderPath={readerFolder} uid={uid} folderRole={readerNode?.specialUse ?? null}
                  onDeparted={departed} depart={rowExit.depart} onNotify={addToast} />
              </div>
            </div>
          )}

          {pane === 'none' && (
            <>
              {/* Hidden, never unmounted: the scroll position and the streamed blocks live in this
                  subtree. No selected row either — there is no message "open beside". */}
              <div className={`mail-list${uid !== null ? ' is-hidden' : ''}`}>{list(null)}</div>
              {uid !== null && (
                <div className="mail-reader">
                  <MessageReader folderPath={readerFolder} uid={uid} folderRole={readerNode?.specialUse ?? null}
                    bottomActions={viewport === 'phone'}
                    onBack={closeMessage} onDeparted={departed} depart={rowExit.depart} onNotify={addToast} />
                </div>
              )}
            </>
          )}
        </>
      )}

      {/* The reader draws its own bar across the foot of a phone screen, and the button is anchored
          73px up from that same edge: leaving it there puts a 56px disc over the delete and the
          kebab. A tablet at `none` keeps it — its reader has no bar, and the folder column's own
          Compose is behind the drawer. */}
      {!composing && !(viewport === 'phone' && pane === 'none' && uid !== null) && (
        <FloatingAction label={t('layout.newMessage')} onClick={openCompose}>
          <RocketIcon size={22} />
        </FloatingAction>
      )}

      <Toasts toasts={toasts} onRemove={removeToast} />
    </div>
  )
}
