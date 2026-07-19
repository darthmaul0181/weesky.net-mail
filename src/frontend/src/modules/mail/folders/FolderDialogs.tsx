import { useState } from 'react'
import { Link } from 'react-router-dom'
import FolderPlusIcon from '../../../icons/FolderPlusIcon'
import SlidersIcon from '../../../icons/SlidersIcon'
import CreateFolderModal from './CreateFolderModal'
import type { MailFolderNode } from '../api/mailTypes'

interface Props {
  folders: MailFolderNode[]
  selectedPath: string | null
  onNotify: (message: string, type?: 'success' | 'error') => void
}

/**
 * The folder column's footer. Icons, not labelled buttons: folder work is rare next to reading
 * and was costing the tree a permanent band. Creating stays here as a quick action; managing
 * leads to the settings page, which a 240px column cannot lay out.
 */
export default function FolderDialogs({ folders, selectedPath, onNotify }: Props) {
  const [creating, setCreating] = useState(false)

  return (
    <>
      <div className="folder-actions">
        <button
          type="button"
          className="folder-action"
          aria-label="New folder"
          title="New folder"
          onClick={() => setCreating(true)}
        >
          <FolderPlusIcon size={17} />
        </button>
        <Link
          to="/settings/folders"
          className="folder-action"
          aria-label="Manage folders"
          title="Manage folders"
        >
          <SlidersIcon size={17} />
        </Link>
      </div>

      {creating && (
        <CreateFolderModal
          folders={folders}
          defaultParent={selectedPath ?? ''}
          onClose={() => setCreating(false)}
          onNotify={onNotify}
        />
      )}
    </>
  )
}
