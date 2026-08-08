import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal.jsx'
import PencilIcon from '../../../icons/PencilIcon.jsx'
import TrashIcon from '../../../icons/TrashIcon.jsx'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import { useDeleteFolder, useRenameFolder, useSetFolderSubscription } from '../queries'
import { roleLabel } from '../roleLabel'
import { flatten, isSystemFolder, parentOf, sortFolders } from './folderNodes'
import type { MailFolderNode } from '../api/mailTypes'

interface Props {
  folders: MailFolderNode[]
  onNotify: (message: string, type?: 'success' | 'error') => void
}

/**
 * Every folder as one tile, indented by depth. A folder holding a role keeps its controls
 * disabled rather than losing them: withheld, those rows are a different shape from the rest
 * and read as a rendering fault. Its role is changed in the system-folders dialog.
 */
export default function FolderManager({ folders, onNotify }: Props) {
  const { t } = useTranslation('mail')
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
      onNotify(apiErrorMessage(error, failure), 'error')
      return false
    }
  }

  return (
    <>
      <ul className="admin-list folder-list">
        {flatten(sortFolders(folders)).map(({ node, depth }) => {
          const isSystem = isSystemFolder(node)

          return (
            // The tile steps in with the depth; its right edge does not move, so the actions
            // keep one column whatever the nesting.
            <li
              key={node.path}
              className="admin-list-item folder-tile"
              style={{ marginLeft: depth * 18 }}
            >
              <label
                className={`toggle-switch${isSystem ? ' is-locked' : ''}`}
                title={isSystem
                  ? t('folders.manage.cannotHide', { role: roleLabel(node.specialUse!, t) })
                  : undefined}
              >
                <input
                  type="checkbox"
                  // The switch below is disabled for these, so off is the one state it must
                  // never show: Proximus subscribes nothing, which drew every system folder
                  // as hidden with no way to show it.
                  checked={isSystem ? true : node.subscribed}
                  disabled={isSystem}
                  aria-label={t('folders.manage.show', { name: node.name })}
                  onChange={e => run(
                    () => setSubscription.mutateAsync({ path: node.path, subscribed: e.target.checked }),
                    t(e.target.checked ? 'folders.manage.nowVisible' : 'folders.manage.nowHidden',
                      { name: node.name }),
                    t('folders.manage.visibilityFailed'))}
                />
                <span className="toggle-track" />
              </label>

              <span className="admin-list-item-email">{node.name}</span>

              {/* Beside the name: parked in its own column it reads as a row status. Now that
                  every tile's name is bold, this badge alone says a folder holds a role. */}
              {isSystem && (
                <span className="row-tag">{roleLabel(node.specialUse!, t)}</span>
              )}

              {/* `disabled`, so no click or key reaches the handler. The API refuses these
                  three regardless of what this list offers. */}
              <div className="admin-list-item-actions">
                <button
                  type="button"
                  className="admin-icon-btn"
                  aria-label={t('folders.manage.rename', { name: node.name })}
                  title={isSystem
                    ? t('folders.manage.cannotRename', { role: roleLabel(node.specialUse!, t) })
                    : t('folders.manage.renameAction')}
                  disabled={isSystem}
                  onClick={() => { setRenaming(node); setRenameValue(node.name) }}
                >
                  <PencilIcon />
                </button>
                <button
                  type="button"
                  className="admin-icon-btn is-danger"
                  aria-label={t('folders.manage.delete', { name: node.name })}
                  title={isSystem
                    ? t('folders.manage.cannotDelete', { role: roleLabel(node.specialUse!, t) })
                    : t('actions.delete', { ns: 'common' })}
                  disabled={isSystem}
                  onClick={() => setPendingDelete(node)}
                >
                  <TrashIcon />
                </button>
              </div>
            </li>
          )
        })}
      </ul>

      {renaming && (
        <div className="modal-overlay" onClick={() => setRenaming(null)}>
          <div className="modal" onClick={event => event.stopPropagation()}>
            <div className="modal-header">
              <span className="modal-title"><PencilIcon />{t('folders.manage.renameTitle')}</span>
              <button className="modal-close" aria-label={t('actions.close', { ns: 'common' })} onClick={() => setRenaming(null)}>✕</button>
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
                  t('folders.manage.renamed'), t('folders.manage.renameFailed'))
                if (ok) setRenaming(null)
              }}
            >
              <div className="field-h">
                <label htmlFor="rename-folder-name">{t('folders.manage.newName')}</label>
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
                {renameFolder.isPending ? <span className="spinner" /> : t('folders.manage.renameAction')}
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
              t('folders.manage.deleted', { name: pendingDelete.name }),
              t('folders.manage.deleteFailed'))
            if (ok) setPendingDelete(null)
          }}
        />
      )}
    </>
  )
}
