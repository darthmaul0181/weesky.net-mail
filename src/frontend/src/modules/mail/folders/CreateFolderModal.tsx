import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import FolderPlusIcon from '../../../icons/FolderPlusIcon'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import { useCreateFolder } from '../queries'
import { flatten, indent, sortFolders } from './folderNodes'
import type { MailFolderNode } from '../api/mailTypes'

interface Props {
  folders: MailFolderNode[]
  /** Pre-selected parent — the folder in view when the dialog is opened from the mail column. */
  defaultParent?: string
  onClose: () => void
  onNotify: (message: string, type?: 'success' | 'error') => void
}

/** Shared by the mail column's footer and the folders settings page, so the two cannot drift. */
export default function CreateFolderModal({ folders, defaultParent = '', onClose, onNotify }: Props) {
  const { t } = useTranslation('mail')
  const [name, setName] = useState('')
  const [parent, setParent] = useState(defaultParent)
  const createFolder = useCreateFolder()

  const all = flatten(sortFolders(folders))

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={event => event.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title"><FolderPlusIcon />{t('folders.create.title')}</span>
          <button className="modal-close" aria-label={t('actions.close', { ns: 'common' })} onClick={onClose}>✕</button>
        </div>

        {/* A form, so Enter submits the way it does in the admin dialogs. */}
        <form
          onSubmit={async event => {
            event.preventDefault()
            try {
              await createFolder.mutateAsync({ parentPath: parent, name: name.trim() })
              onNotify(t('folders.create.created', { name: name.trim() }))
              onClose()
            } catch (error) {
              onNotify(apiErrorMessage(error, t('folders.create.failed')), 'error')
            }
          }}
        >
          <div className="field-h">
            <label htmlFor="new-folder-name">{t('folders.create.name')}</label>
            <input
              id="new-folder-name"
              type="text"
              value={name}
              onChange={e => setName(e.target.value)}
              autoFocus
            />
          </div>

          <div className="field-h">
            <label htmlFor="new-folder-parent">{t('folders.create.parent')}</label>
            <select id="new-folder-parent" value={parent} onChange={e => setParent(e.target.value)}>
              <option value="">{t('folders.create.topLevel')}</option>
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
            {createFolder.isPending ? <span className="spinner" /> : t('folders.create.submit')}
          </button>
        </form>
      </div>
    </div>
  )
}
