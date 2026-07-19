import { useState } from 'react'
import { Link } from 'react-router-dom'
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

/** Nesting shown in a flat <select>, where indentation is the only cue available. */
function indent(depth: number): string {
  return ' '.repeat(depth * 3)
}

/**
 * Folder actions and the dialogs they open.
 *
 * The two actions are icons in the column's footer rather than labelled buttons at its top:
 * creating and managing folders are rare next to the constant business of reading, and they
 * were taking a band of the column away from the tree every time it was not being used.
 *
 * The dialogs follow the admin module's shape — `.field-h` rows, `.toggle-switch` for a
 * boolean, one primary submit, closing on the ✕ — because a webmail with two dialect of
 * dialog looks like two applications.
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
          <div className="modal" style={{ maxWidth: '560px' }} onClick={event => event.stopPropagation()}>
            <div className="modal-header">
              <span className="modal-title"><FolderPlusIcon />New folder</span>
              <button className="modal-close" aria-label="Close" onClick={closeCreate}>✕</button>
            </div>

            {/* A form, so Enter submits the way it does in the admin dialogs. */}
            <form
              onSubmit={async event => {
                event.preventDefault()
                const ok = await run(
                  () => createFolder.mutateAsync({ parentPath: newParent, name: newName.trim() }),
                  `Folder "${newName.trim()}" created`, 'Could not create the folder')
                if (ok) closeCreate()
              }}
            >
              <div className="field-h">
                <label htmlFor="new-folder-name">Name</label>
                <input
                  id="new-folder-name"
                  type="text"
                  value={newName}
                  onChange={e => setNewName(e.target.value)}
                  autoFocus
                />
              </div>

              <div className="field-h">
                <label htmlFor="new-folder-parent">Parent</label>
                <select
                  id="new-folder-parent"
                  value={newParent}
                  onChange={e => setNewParent(e.target.value)}
                >
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
                disabled={!newName.trim() || createFolder.isPending}
              >
                {createFolder.isPending ? <span className="spinner" /> : 'Create folder'}
              </button>
            </form>
          </div>
        </div>
      )}

      {managing && (
        <div className="modal-overlay" onClick={() => setManaging(false)}>
          <div className="modal modal-folders" onClick={event => event.stopPropagation()}>
            <div className="modal-header">
              <span className="modal-title"><SlidersIcon />Manage folders</span>
              <button className="modal-close" aria-label="Close" onClick={() => setManaging(false)}>✕</button>
            </div>

            <p className="modal-hint">
              Turning a folder off hides it from the tree. Nothing in it is deleted. To choose
              which folders act as Sent, Drafts, Trash, Junk and Archive, use{' '}
              <Link to="/settings/system-folders">system folders</Link>.
            </p>

            <ul className="folder-manage-list">
              {all.map(({ node, depth }) => {
                const isInbox = node.specialUse === 'inbox'

                return (
                  <li key={node.path} className="folder-manage-row" style={{ paddingLeft: 8 + depth * 18 }}>
                    <label className="toggle-switch">
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
                      <span className="toggle-track" />
                    </label>

                    <span className="folder-manage-label">{node.name}</span>

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
          <div className="modal" style={{ maxWidth: '560px' }} onClick={event => event.stopPropagation()}>
            <div className="modal-header">
              <span className="modal-title"><PencilIcon />Rename folder</span>
              <button className="modal-close" aria-label="Close" onClick={() => setRenaming(null)}>✕</button>
            </div>

            <form
              onSubmit={async event => {
                event.preventDefault()
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
              <div className="field-h">
                <label htmlFor="rename-folder-name">New name</label>
                <input
                  id="rename-folder-name"
                  type="text"
                  value={renameValue}
                  onChange={e => setRenameValue(e.target.value)}
                  autoFocus
                />
              </div>

              <button
                type="submit"
                className="btn btn-primary"
                style={{ marginTop: '8px' }}
                disabled={!renameValue.trim() || renameFolder.isPending}
              >
                {renameFolder.isPending ? <span className="spinner" /> : 'Rename'}
              </button>
            </form>
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
