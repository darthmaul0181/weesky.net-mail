import { useState } from 'react'
import LoadingBlock from '../../../components/LoadingBlock'
import Toasts from '../../../components/Toasts.jsx'
import { useToasts } from '../../../hooks/useToasts.js'
import FolderPlusIcon from '../../../icons/FolderPlusIcon'
import SlidersIcon from '../../../icons/SlidersIcon'
import CreateFolderModal from '../../mail/folders/CreateFolderModal'
import FolderManager from '../../mail/folders/FolderManager'
import { useFolders } from '../../mail/queries'
import SystemFoldersModal from './SystemFoldersModal'

/**
 * Everything about folders: the full list, plus the two dialogs acting across the whole set.
 * The mail column keeps its own New folder button; its Manage button leads here.
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
        <button type="button" className="btn btn-primary" onClick={() => setCreating(true)}>
          <FolderPlusIcon size={15} />New folder
        </button>
        {/* btn-ghost: a bare `.btn` has no border and no background, and reads as text. */}
        <button type="button" className="btn btn-ghost" onClick={() => setAssigningRoles(true)}>
          <SlidersIcon size={15} />System folders
        </button>
      </div>

      {isLoading && <LoadingBlock />}
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
