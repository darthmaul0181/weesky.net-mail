import { lazy, Suspense, useCallback, useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useMatch, useNavigate, useSearchParams } from 'react-router-dom'
import type { SearchCriteria } from './list/searchCriteria'
import Toasts from '../../components/Toasts.jsx'
import { useToasts } from '../../hooks/useToasts.js'
import { flatten } from './folders/folderNodes'
import FolderTree from './folders/FolderTree'
import RocketIcon from '../../icons/RocketIcon'
import IdentityMenu from '../../layouts/IdentityMenu'
import MessageList from './list/MessageList'
import { nextUidOf } from './list/nextUid'
import { useListRefresh } from './list/useListRefresh'
import MessageReader from './reader/MessageReader'
import type { DragPayload } from './list/dragMessages'
import {
  identitiesQueryOptions, useAccountId, useFolders, useIdentities, useMoveMessages, useOpenDraft,
} from './queries'
import { buildDraftSeed } from './compose/composeSeed'
import { roleLabel } from './roleLabel'
import { readingPaneOf, usePreferences } from '../../hooks/usePreferences'
import PaneSplitter from './split/PaneSplitter'
import { usePaneSize } from './split/usePaneSize'

// Lazy: pulls in squire-rte, which every /mail visitor would otherwise download unread.
const ComposeView = lazy(() => import('./compose/ComposeView'))

/**
 * The mail module's three columns. The shell provides a single outlet, so a module builds its
 * own columns inside it — the same way the settings section does.
 *
 * Selection lives in search params rather than route segments: a folder path may contain '/',
 * which is exactly why folder paths stay out of route segments in the API too. Deep links and
 * the back button still work.
 */
export default function MailLayout() {
  const [params, setParams] = useSearchParams()
  const composing = useMatch('/mail/compose') != null
  const navigate = useNavigate()
  const { data: folders, isLoading, isError } = useFolders()
  const { toasts, addToast, removeToast } = useToasts()
  const moveMessages = useMoveMessages(addToast)
  const openDraft = useOpenDraft()
  const { data: identityList } = useIdentities()
  const queryClient = useQueryClient()
  const accountId = useAccountId()

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

  useListRefresh(folder)

  // The list heading shows the same label as the tree: the role label when the folder has a
  // role, the leaf name otherwise — never the full path, which reads "INBOX.Linux server"
  // under a '.' separator.
  const folderNode = folders && folder
    ? flatten(folders).find(entry => entry.node.path === folder)?.node
    : undefined
  const folderName = folderNode
    ? (folderNode.specialUse ? roleLabel(folderNode.specialUse) : folderNode.name)
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
      const seed = buildDraftSeed(opened, identities, { folderPath: folder!, uid: draftUid })
      navigate('/mail/compose', { state: { from: folder, seed } })
    } catch (error) {
      addToast((error as Error).message || 'Could not open the draft', 'error')
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

  const { data: preferences } = usePreferences()
  // Until the preferences answer, today's layout — the list already waits on the same query,
  // so nothing meaningful can flash in the wrong arrangement.
  const pane = preferences ? readingPaneOf(preferences) : 'right'
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
    moveMessages.mutate({
      folderPath: payload.sourcePath, uids: payload.uids, targetFolderPath, copy: false,
    })
    if (uid !== null && payload.uids.includes(uid)) departed(uid, payload.uids)
  }, [moveMessages, uid, departed])

  const list = (selected: number | null, wide: boolean) => (
    <MessageList
      folderPath={folder}
      folderName={folderName}
      folderRole={folderNode?.specialUse ?? null}
      selectedUid={selected}
      onSelect={selectMessage}
      wide={wide}
      onNotify={addToast}
      onRows={keepRows}
      onDeparted={departed}
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

  return (
    <div className={`mail-layout is-${pane}`}>
      {/* Each column is a band stack: what scrolls is the middle band only, so the folder
          actions and the pager stay put instead of hiding below their own content. */}
      <div className="mail-folders">
        <div className="mail-folders-compose">
          <button type="button" className="btn btn-primary mail-compose-btn" onClick={openCompose}>
            <RocketIcon size={15} /> New message
          </button>
        </div>
        <div className="mail-folders-scroll">
          {isLoading && <p className="mail-empty">Loading folders…</p>}
          {isError && <p className="mail-empty">Could not load folders.</p>}
          {folders && (
            <FolderTree folders={folders} selectedPath={folder} onSelect={selectFolder}
              onDropMessages={dropMessages} />
          )}
        </div>

        {/* Shows even while the tree loads: the account block does not depend on it. */}
        <div className="mail-folders-footer">
          <IdentityMenu />
        </div>
      </div>

      {/* Composing takes the whole list+reader side; the folder tree stays where it was. */}
      {composing ? (
        <div className="mail-compose">
          <Suspense fallback={null}><ComposeView onNotify={addToast} /></Suspense>
        </div>
      ) : (
        <>
          {pane === 'right' && (
            <div className="mail-row">
              <div className="mail-list" style={{ width: listWidth }}>{list(uid, false)}</div>
              {preferences && (
                <PaneSplitter
                  orientation="vertical" size={listWidth} defaultSize={380} min={240} reserve={320}
                  onResize={setListWidth}
                />
              )}
              <div className="mail-reader">
                <MessageReader folderPath={readerFolder} uid={uid} folderRole={readerNode?.specialUse ?? null}
                  onDeparted={departed} onNotify={addToast} />
              </div>
            </div>
          )}

          {pane === 'bottom' && (
            <div className="mail-stack">
              <div className="mail-list" style={{ height: listHeight }}>{list(uid, true)}</div>
              <PaneSplitter
                orientation="horizontal" size={listHeight} defaultSize={280} min={120} reserve={160}
                onResize={setListHeight}
              />
              <div className="mail-reader">
                <MessageReader folderPath={readerFolder} uid={uid} folderRole={readerNode?.specialUse ?? null}
                  onDeparted={departed} onNotify={addToast} />
              </div>
            </div>
          )}

          {pane === 'none' && (
            <>
              {/* Hidden, never unmounted: the scroll position and the streamed blocks live in this
                  subtree. No selected row either — there is no message "open beside". */}
              <div className={`mail-list${uid !== null ? ' is-hidden' : ''}`}>{list(null, true)}</div>
              {uid !== null && (
                <div className="mail-reader">
                  <MessageReader folderPath={readerFolder} uid={uid} folderRole={readerNode?.specialUse ?? null}
                    onBack={closeMessage} onDeparted={departed} onNotify={addToast} />
                </div>
              )}
            </>
          )}
        </>
      )}

      <Toasts toasts={toasts} onRemove={removeToast} />
    </div>
  )
}
