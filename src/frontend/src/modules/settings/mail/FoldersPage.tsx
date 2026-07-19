import { useState } from 'react'
import Toasts from '../../../components/Toasts.jsx'
import { useToasts } from '../../../hooks/useToasts.js'
import FolderPlusIcon from '../../../icons/FolderPlusIcon'
import SlidersIcon from '../../../icons/SlidersIcon'
import CreateFolderModal from '../../mail/folders/CreateFolderModal'
import FolderManager from '../../mail/folders/FolderManager'
import { useFolders } from '../../mail/queries'
import SystemFoldersModal from './SystemFoldersModal'

/**
 * Everything about folders in one place: the full list with its per-folder controls, plus the
 * two dialogs that act across the whole set. The mail column keeps its own New folder button —
 * creating a folder while reading is a quick action, not a trip to the settings — but its
 * Manage button leads here rather than opening a list a 240px column cannot lay out.
 */
export default function FoldersPage() {
  const { data: folders, isLoading, isError } = useFolders()
  const { toasts, addToast, removeToast } = useToasts()
  const [creating, setCreating] = useState(false)
  const [assigningRoles, setAssigningRoles] = useState(false)

  return (
    <div className="settings-page">
      <h1>Folders</h1>
      <p className="folders-page-hint">
        Turning a folder off hides it from the mail view. Nothing in it is deleted. Folders
        holding a system role are locked here — use System folders to change which folder plays
        which role.
      </p>

      <div className="folders-page-actions">
        {/* btn-ghost, not a bare btn: `.btn` alone carries no border and no background, so both
            of these rendered as plain text and read as headings rather than controls. */}
        <button type="button" className="btn btn-ghost" onClick={() => setCreating(true)}>
          <FolderPlusIcon size={15} />New folder
        </button>
        <button type="button" className="btn btn-ghost" onClick={() => setAssigningRoles(true)}>
          <SlidersIcon size={15} />System folders
        </button>
      </div>

      {isLoading && <p>Loading…</p>}
      {!isLoading && (isError || !folders) && <p>Could not load the folders.</p>}
      {!isLoading && !isError && folders && (
        <FolderManager folders={folders} onNotify={addToast} />
      )}

      {creating && folders && (
        <CreateFolderModal
          folders={folders}
          onClose={() => setCreating(false)}
          onNotify={addToast}
        />
      )}

      {assigningRoles && (
        <SystemFoldersModal onClose={() => setAssigningRoles(false)} onNotify={addToast} />
      )}

      <Toasts toasts={toasts} onRemove={removeToast} />
    </div>
  )
}
