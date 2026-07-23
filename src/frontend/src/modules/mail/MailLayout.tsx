import { useCallback, useEffect, useRef } from 'react'
import { useSearchParams } from 'react-router-dom'
import Toasts from '../../components/Toasts.jsx'
import { useToasts } from '../../hooks/useToasts.js'
import FolderDialogs from './folders/FolderDialogs'
import { flatten } from './folders/folderNodes'
import FolderTree from './folders/FolderTree'
import MessageList from './list/MessageList'
import { nextUidOf } from './list/nextUid'
import { useListRefresh } from './list/useListRefresh'
import MessageReader from './reader/MessageReader'
import { useFolders } from './queries'
import { roleLabel } from './roleLabel'
import { readingPaneOf, usePreferences } from '../../hooks/usePreferences'
import PaneSplitter from './split/PaneSplitter'
import { usePaneSize } from './split/usePaneSize'

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
  const { data: folders, isLoading, isError } = useFolders()
  const { toasts, addToast, removeToast } = useToasts()

  const folder = params.get('folder')
  const uidParam = params.get('uid')
  const uid = uidParam ? Number(uidParam) : null

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
  useEffect(() => {
    if (folder || !folders) return

    const inbox = flatten(folders).find(entry => entry.node.specialUse === 'inbox')
    if (inbox) setParams({ folder: inbox.node.path }, { replace: true })
  }, [folder, folders, setParams])

  function selectFolder(path: string) {
    // Drops uid: a message id means nothing in another folder.
    setParams({ folder: path })
  }

  function selectMessage(nextUid: number) {
    if (!folder) return
    setParams({ folder, uid: String(nextUid) })
  }

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
    />
  )

  return (
    <div className={`mail-layout is-${pane}`}>
      {/* Each column is a band stack: what scrolls is the middle band only, so the folder
          actions and the pager stay put instead of hiding below their own content. */}
      <div className="mail-folders">
        <div className="mail-folders-scroll">
          {isLoading && <p className="mail-empty">Loading folders…</p>}
          {isError && <p className="mail-empty">Could not load folders.</p>}
          {folders && <FolderTree folders={folders} selectedPath={folder} onSelect={selectFolder} />}
        </div>

        {folders && (
          <div className="mail-folders-footer">
            <FolderDialogs folders={folders} selectedPath={folder} onNotify={addToast} />
          </div>
        )}
      </div>

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
            <MessageReader folderPath={folder} uid={uid} folderRole={folderNode?.specialUse ?? null}
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
            <MessageReader folderPath={folder} uid={uid} folderRole={folderNode?.specialUse ?? null}
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
              <MessageReader folderPath={folder} uid={uid} folderRole={folderNode?.specialUse ?? null}
                onBack={closeMessage} onDeparted={departed} onNotify={addToast} />
            </div>
          )}
        </>
      )}

      <Toasts toasts={toasts} onRemove={removeToast} />
    </div>
  )
}
