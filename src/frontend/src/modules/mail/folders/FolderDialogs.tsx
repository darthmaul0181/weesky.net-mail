import { useState } from 'react'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal.jsx'
import FolderPlusIcon from '../../../icons/FolderPlusIcon'
import PencilIcon from '../../../icons/PencilIcon.jsx'
import SlidersIcon from '../../../icons/SlidersIcon'
import TrashIcon from '../../../icons/TrashIcon.jsx'
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

/**
 * Folder actions and the dialogs they open.
 *
 * The two actions are icons in the column's footer rather than labelled buttons at its top:
 * creating and managing folders are rare next to the constant business of reading, and they
 * were taking a band of the column away from the tree every time it was not being used.
 *
 * Managing happens in a modal. The list needs a row per folder with a visibility control and
 * two actions, which is more than a 240px column can lay out legibly.
 */
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

  function closeCreate() {
    setCreating(false)
    setNewName('')
  }

  return (
    <>
      <div className="folder-actions">
        <button
          type="button"
          className="folder-action"
          aria-label="New folder"
          title="New folder"
          onClick={() => { setCreating(true); setNewParent(selectedPath ?? '') }}
        >
          <FolderPlusIcon size={17} />
        </button>
        <button
          type="button"
          className="folder-action"
          aria-label="Manage folders"
          title="Manage folders"
          onClick={() => setManaging(true)}
        >
          <SlidersIcon size={17} />
        </button>
      </div>

      {creating && (
        <div className="modal-overlay" onClick={closeCreate}>
          <div className="modal" onClick={event => event.stopPropagation()}>
            <div className="modal-header">
              <span className="modal-title">New folder</span>
              <button className="modal-close" aria-label="Close" onClick={closeCreate}>✕</button>
            </div>

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
                    <option key={node.path} value={node.path}>
                      {' '.repeat(depth * 2)}{node.name}
                    </option>
                  ))}
                </select>
              </label>
            </div>

            <div className="modal-actions">
              <button type="button" className="btn" onClick={closeCreate}>Cancel</button>
              <button
                type="button"
                className="btn btn-primary"
                disabled={!newName.trim() || createFolder.isPending}
                onClick={async () => {
                  const ok = await run(
                    () => createFolder.mutateAsync({ parentPath: newParent, name: newName.trim() }),
                    `Folder "${newName.trim()}" created`, 'Could not create the folder')
                  if (ok) closeCreate()
                }}
              >
                Create
              </button>
            </div>
          </div>
        </div>
      )}

      {managing && (
        <div className="modal-overlay" onClick={() => setManaging(false)}>
          <div className="modal modal-folders" onClick={event => event.stopPropagation()}>
            <div className="modal-header">
              <span className="modal-title">Manage folders</span>
              <button className="modal-close" aria-label="Close" onClick={() => setManaging(false)}>✕</button>
            </div>

            <p className="modal-hint">
              Clearing a folder&rsquo;s checkbox hides it from the tree. Nothing in it is deleted.
            </p>

            <ul className="folder-manage-list">
              {all.map(({ node, depth }) => {
                const isInbox = node.specialUse === 'inbox'

                return (
                  <li key={node.path} className="folder-manage-row">
                    <label className="folder-manage-name" style={{ paddingLeft: depth * 16 }}>
                      <input
                        type="checkbox"
                        // The inbox is always visible and its subscription flag is meaningless —
                        // Dovecot leaves it unsubscribed — so offering to hide it would be a
                        // control that either does nothing or loses the user their mail.
                        checked={isInbox ? true : node.subscribed}
                        disabled={isInbox}
                        aria-label={`Show ${node.name}`}
                        onChange={e => run(
                          () => setSubscription.mutateAsync({ path: node.path, subscribed: e.target.checked }),
                          e.target.checked ? `"${node.name}" is now visible` : `"${node.name}" is now hidden`,
                          'Could not change the folder visibility')}
                      />
                      <span className="folder-manage-label">{node.name}</span>
                    </label>

                    <div className="folder-manage-actions">
                      <button
                        type="button"
                        className="folder-action"
                        aria-label={`Rename ${node.name}`}
                        title="Rename"
                        onClick={() => { setRenaming(node); setRenameValue(node.name) }}
                      >
                        <PencilIcon size={15} />
                      </button>
                      {!isInbox && (
                        <button
                          type="button"
                          className="folder-action is-danger"
                          aria-label={`Delete ${node.name}`}
                          title="Delete"
                          onClick={() => setPendingDelete(node)}
                        >
                          <TrashIcon size={15} />
                        </button>
                      )}
                    </div>
                  </li>
                )
              })}
            </ul>
          </div>
        </div>
      )}

      {renaming && (
        <div className="modal-overlay" onClick={() => setRenaming(null)}>
          <div className="modal" onClick={event => event.stopPropagation()}>
            <div className="modal-header">
              <span className="modal-title">Rename folder</span>
              <button className="modal-close" aria-label="Close" onClick={() => setRenaming(null)}>✕</button>
            </div>

            <div className="folder-form">
              <label>
                New name
                <input value={renameValue} onChange={e => setRenameValue(e.target.value)} autoFocus />
              </label>
            </div>

            <div className="modal-actions">
              <button type="button" className="btn" onClick={() => setRenaming(null)}>Cancel</button>
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
            </div>
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
    </>
  )
}
