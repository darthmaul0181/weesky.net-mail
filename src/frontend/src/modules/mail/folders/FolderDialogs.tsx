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
 * The folder column's footer actions.
 *
 * Two icons rather than labelled buttons: creating and managing folders are rare next to the
 * constant business of reading, and labelled buttons at the top were taking a band of the
 * column away from the tree every time it was not being used.
 *
 * Creating stays here — it is a quick action, reached while looking at the tree it will change.
 * Managing leads to the folders settings page: a row per folder with a switch and two actions
 * is not something a 240px column can lay out legibly.
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
