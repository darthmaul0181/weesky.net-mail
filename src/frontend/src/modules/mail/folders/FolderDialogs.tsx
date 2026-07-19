import { useState } from 'react'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal.jsx'
import {
  useCreateFolder,
  useDeleteFolder,
  useRenameFolder,
  useSetFolderSubscription,
} from '../queries'
import type { MailFolderNode } from '../api/mailTypes'

interface Props {
  folders: MailFolderNode[]
  selectedPath: string | null
  onNotify: (message: string, type?: 'success' | 'error') => void
}

/** Flattens the tree so the parent picker and the manage list can show every folder. */
export function flatten(nodes: MailFolderNode[], depth = 0): Array<{ node: MailFolderNode; depth: number }> {
  return nodes.flatMap(node => [{ node, depth }, ...flatten(node.children, depth + 1)])
}

/**
 * Derives a folder's parent path by removing its leaf name. Works for any hierarchy
 * separator, because the leaf name is known and cannot contain one — the backend rejects
 * names that do.
 */
export function parentOf(folder: MailFolderNode): string {
  return folder.path.length > folder.name.length
    ? folder.path.slice(0, folder.path.length - folder.name.length - 1)
    : ''
}

export default function FolderDialogs({ folders, selectedPath, onNotify }: Props) {
  const [creating, setCreating] = useState(false)
  const [newName, setNewName] = useState('')
  const [newParent, setNewParent] = useState('')
  const [renaming, setRenaming] = useState<MailFolderNode | null>(null)
  const [renameValue, setRenameValue] = useState('')
  const [pendingDelete, setPendingDelete] = useState<MailFolderNode | null>(null)
  const [managing, setManaging] = useState(false)

  const createFolder = useCreateFolder()
  const renameFolder = useRenameFolder()
  const deleteFolder = useDeleteFolder()
  const setSubscription = useSetFolderSubscription()

  const all = flatten(folders)

  async function run(action: () => Promise<unknown>, success: string, failure: string): Promise<boolean> {
    try {
      await action()
      onNotify(success)
      return true
    } catch (error) {
      onNotify(error instanceof Error ? error.message : failure, 'error')
      return false
    }
  }

  return (
    <div>
      <div className="folder-actions">
        <button
          type="button"
          className="btn"
          onClick={() => { setCreating(true); setNewParent(selectedPath ?? '') }}
        >
          New folder
        </button>
        <button type="button" className="btn" onClick={() => setManaging(value => !value)}>
          {managing ? 'Done' : 'Manage'}
        </button>
      </div>

      {creating && (
        <div className="folder-form">
          <label>
            Name
            <input value={newName} onChange={e => setNewName(e.target.value)} autoFocus />
          </label>
          <label>
            Parent
            <select value={newParent} onChange={e => setNewParent(e.target.value)}>
              <option value="">(top level)</option>
              {all.map(({ node, depth }) => (
                <option key={node.path} value={node.path}>{' '.repeat(depth * 2)}{node.name}</option>
              ))}
            </select>
          </label>
          <div className="folder-actions">
            <button
              type="button"
              className="btn btn-primary"
              disabled={!newName.trim() || createFolder.isPending}
              onClick={async () => {
                const ok = await run(
                  () => createFolder.mutateAsync({ parentPath: newParent, name: newName.trim() }),
                  `Folder "${newName.trim()}" created`, 'Could not create the folder')
                if (ok) { setCreating(false); setNewName('') }
              }}
            >
              Create
            </button>
            <button type="button" className="btn" onClick={() => { setCreating(false); setNewName('') }}>
              Cancel
            </button>
          </div>
        </div>
      )}

      {managing && (
        <ul className="folder-manage-list">
          {all.map(({ node, depth }) => (
            <li key={node.path} style={{ paddingLeft: depth * 12 }}>
              <label>
                <input
                  type="checkbox"
                  checked={node.subscribed}
                  aria-label={`Show ${node.name}`}
                  onChange={e => run(
                    () => setSubscription.mutateAsync({ path: node.path, subscribed: e.target.checked }),
                    e.target.checked ? `"${node.name}" is now visible` : `"${node.name}" is now hidden`,
                    'Could not change the folder visibility')}
                />
                {node.name}
              </label>
              <button
                type="button"
                className="btn"
                aria-label={`Rename ${node.name}`}
                onClick={() => { setRenaming(node); setRenameValue(node.name) }}
              >
                Rename
              </button>
              {node.specialUse !== 'inbox' && (
                <button
                  type="button"
                  className="btn btn-danger"
                  aria-label={`Delete ${node.name}`}
                  onClick={() => setPendingDelete(node)}
                >
                  Delete
                </button>
              )}
            </li>
          ))}
        </ul>
      )}

      {renaming && (
        <div className="folder-form">
          <label>
            New name
            <input value={renameValue} onChange={e => setRenameValue(e.target.value)} autoFocus />
          </label>
          <div className="folder-actions">
            <button
              type="button"
              className="btn btn-primary"
              disabled={!renameValue.trim() || renameFolder.isPending}
              onClick={async () => {
                const ok = await run(
                  () => renameFolder.mutateAsync({
                    path: renaming.path,
                    newParentPath: parentOf(renaming),
                    newName: renameValue.trim(),
                  }),
                  'Folder renamed', 'Could not rename the folder')
                if (ok) setRenaming(null)
              }}
            >
              Rename
            </button>
            <button type="button" className="btn" onClick={() => setRenaming(null)}>Cancel</button>
          </div>
        </div>
      )}

      {pendingDelete && (
        <DeleteConfirmModal
          entityLabel={pendingDelete.name}
          loading={deleteFolder.isPending}
          onClose={() => setPendingDelete(null)}
          onConfirm={async () => {
            const ok = await run(
              () => deleteFolder.mutateAsync({ path: pendingDelete.path }),
              `Folder "${pendingDelete.name}" deleted`, 'Could not delete the folder')
            if (ok) setPendingDelete(null)
          }}
        />
      )}
    </div>
  )
}
