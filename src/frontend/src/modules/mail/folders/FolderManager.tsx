import { useState } from 'react'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal.jsx'
import PencilIcon from '../../../icons/PencilIcon.jsx'
import TrashIcon from '../../../icons/TrashIcon.jsx'
import { useDeleteFolder, useRenameFolder, useSetFolderSubscription } from '../queries'
import { roleLabel } from '../roleLabel'
import { flatten, isSystemFolder, parentOf, sortFolders } from './folderNodes'
import type { MailFolderNode } from '../api/mailTypes'

interface Props {
  folders: MailFolderNode[]
  onNotify: (message: string, type?: 'success' | 'error') => void
}

/**
 * Every folder in one flat, indented list: visibility, rename, delete.
 *
 * A folder holding a well-known role carries no control at all — hiding it strands whatever
 * gets filed into it, and renaming or deleting it breaks the role for every client on the
 * mailbox. Its role is named on the row instead, because controls that are merely absent read
 * as a fault rather than as a rule. The role itself is changed in the system-folders dialog.
 */
export default function FolderManager({ folders, onNotify }: Props) {
  const [renaming, setRenaming] = useState<MailFolderNode | null>(null)
  const [renameValue, setRenameValue] = useState('')
  const [pendingDelete, setPendingDelete] = useState<MailFolderNode | null>(null)

  const renameFolder = useRenameFolder()
  const deleteFolder = useDeleteFolder()
  const setSubscription = useSetFolderSubscription()

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
    <>
      <ul className="folder-manage-list">
        {flatten(sortFolders(folders)).map(({ node, depth }) => {
          const isInbox = node.specialUse === 'inbox'
          const isSystem = isSystemFolder(node)

          return (
            <li
              key={node.path}
              className={`folder-manage-row${isSystem ? ' is-system' : ''}`}
              style={{ paddingLeft: 8 + depth * 18 }}
            >
              <label className="toggle-switch">
                <input
                  type="checkbox"
                  // The inbox is always visible and its subscription flag is meaningless —
                  // Dovecot leaves it unsubscribed — so showing it as off would invite the user
                  // to "show" a folder that is always shown.
                  checked={isInbox ? true : node.subscribed}
                  disabled={isSystem}
                  aria-label={`Show ${node.name}`}
                  onChange={e => run(
                    () => setSubscription.mutateAsync({ path: node.path, subscribed: e.target.checked }),
                    e.target.checked ? `"${node.name}" is now visible` : `"${node.name}" is now hidden`,
                    'Could not change the folder visibility')}
                />
                <span className="toggle-track" />
              </label>

              <span className="folder-manage-label">{node.name}</span>

              {isSystem ? (
                <span className="folder-manage-role">{roleLabel(node.specialUse!)}</span>
              ) : (
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
                  <button
                    type="button"
                    className="folder-action is-danger"
                    aria-label={`Delete ${node.name}`}
                    title="Delete"
                    onClick={() => setPendingDelete(node)}
                  >
                    <TrashIcon size={15} />
                  </button>
                </div>
              )}
            </li>
          )
        })}
      </ul>

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
