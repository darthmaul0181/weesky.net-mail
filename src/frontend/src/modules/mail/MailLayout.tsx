import { useSearchParams } from 'react-router-dom'
import Toasts from '../../components/Toasts.jsx'
import { useToasts } from '../../hooks/useToasts.js'
import FolderDialogs, { flatten } from './folders/FolderDialogs'
import FolderTree from './folders/FolderTree'
import MessageList from './list/MessageList'
import MessageReader from './reader/MessageReader'
import { useFolders } from './queries'

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

  // The list heading wants the leaf name, not the full path: under a '.' separator the path
  // reads "INBOX.Linux server", which is not what the user called the folder.
  const folderName = folders && folder
    ? flatten(folders).find(entry => entry.node.path === folder)?.node.name
    : undefined

  function selectFolder(path: string) {
    // Drops uid: a message id means nothing in another folder.
    setParams({ folder: path })
  }

  function selectMessage(nextUid: number) {
    if (!folder) return
    setParams({ folder, uid: String(nextUid) })
  }

  return (
    <div className="mail-layout">
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

      <div className="mail-list">
        <MessageList
          folderPath={folder}
          folderName={folderName}
          selectedUid={uid}
          onSelect={selectMessage}
        />
      </div>

      <div className="mail-reader">
        <MessageReader folderPath={folder} uid={uid} />
      </div>

      <Toasts toasts={toasts} onRemove={removeToast} />
    </div>
  )
}
