import { useState } from 'react'
import FolderPlusIcon from '../../../icons/FolderPlusIcon'
import CreateFolderModal from './CreateFolderModal'
import type { MailFolderNode } from '../api/mailTypes'

interface Props {
  folders: MailFolderNode[]
  selectedPath: string | null
  onNotify: (message: string, type?: 'success' | 'error') => void
}

/**
 * The column footer's create shortcut, beside the account block. Managing folders is not repeated
 * here: a row per folder with a switch and two actions needs width a 240px column does not have,
 * so it lives on the settings page the rail's gear leads to.
 */
export default function FolderDialogs({ folders, selectedPath, onNotify }: Props) {
  const [creating, setCreating] = useState(false)

  return (
    <>
      <button
        type="button"
        className="folder-action"
        aria-label="New folder"
        title="New folder"
        onClick={() => setCreating(true)}
      >
        <FolderPlusIcon size={17} />
      </button>

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
