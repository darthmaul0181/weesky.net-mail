import { useState } from 'react'
import FolderPlusIcon from '../../../icons/FolderPlusIcon'
import { useCreateFolder } from '../queries'
import { flatten, indent } from './folderNodes'
import type { MailFolderNode } from '../api/mailTypes'

interface Props {
  folders: MailFolderNode[]
  /** Pre-selected parent — the folder in view when the dialog is opened from the mail column. */
  defaultParent?: string
  onClose: () => void
  onNotify: (message: string, type?: 'success' | 'error') => void
}

/**
 * Creating a folder is reachable from two places — the mail column's footer, where it is a
 * quick action beside the tree, and the folders settings page. One component, so the two can
 * never drift into two dialects of the same dialog.
 */
export default function CreateFolderModal({ folders, defaultParent = '', onClose, onNotify }: Props) {
  const [name, setName] = useState('')
  const [parent, setParent] = useState(defaultParent)
  const createFolder = useCreateFolder()

  const all = flatten(folders)

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" style={{ maxWidth: '560px' }} onClick={event => event.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title"><FolderPlusIcon />New folder</span>
          <button className="modal-close" aria-label="Close" onClick={onClose}>✕</button>
        </div>

        {/* A form, so Enter submits the way it does in the admin dialogs. */}
        <form
          onSubmit={async event => {
            event.preventDefault()
            try {
              await createFolder.mutateAsync({ parentPath: parent, name: name.trim() })
              onNotify(`Folder "${name.trim()}" created`)
              onClose()
            } catch (error) {
              onNotify(
                error instanceof Error ? error.message : 'Could not create the folder', 'error')
            }
          }}
        >
          <div className="field-h">
            <label htmlFor="new-folder-name">Name</label>
            <input
              id="new-folder-name"
              type="text"
              value={name}
              onChange={e => setName(e.target.value)}
              autoFocus
            />
          </div>

          <div className="field-h">
            <label htmlFor="new-folder-parent">Parent</label>
            <select id="new-folder-parent" value={parent} onChange={e => setParent(e.target.value)}>
              <option value="">(top level)</option>
              {all.map(({ node, depth }) => (
                <option key={node.path} value={node.path}>{indent(depth)}{node.name}</option>
              ))}
            </select>
          </div>

          <button
            type="submit"
            className="btn btn-primary"
            style={{ marginTop: '8px' }}
            disabled={!name.trim() || createFolder.isPending}
          >
            {createFolder.isPending ? <span className="spinner" /> : 'Create folder'}
          </button>
        </form>
      </div>
    </div>
  )
}
