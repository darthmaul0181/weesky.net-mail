import { useRef, useState } from 'react'
import MenuSelect from '../../../components/MenuSelect'
import { useTranslation } from 'react-i18next'
import Modal from '../../../components/Modal'
import FolderPlusIcon from '../../../icons/FolderPlusIcon'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import { useCreateFolder } from '../queries'
import { flatten, sortFolders } from './folderNodes'
import type { MailFolderNode } from '../api/mailTypes'

interface Props {
  folders: MailFolderNode[]
  /** Pre-selected parent, for a caller that has a folder already in view; `FoldersPage`, the only
      caller today, leaves it unset. */
  defaultParent?: string
  onClose: () => void
  onNotify: (message: string, type?: 'success' | 'error') => void
}

/** Reached from `FoldersPage` alone: folder management has no shortcut in the mail column. */
export default function CreateFolderModal({ folders, defaultParent = '', onClose, onNotify }: Props) {
  const { t } = useTranslation('mail')
  const [name, setName] = useState('')
  const [parent, setParent] = useState(defaultParent)
  const createFolder = useCreateFolder()
  const nameRef = useRef<HTMLInputElement>(null)

  const all = flatten(sortFolders(folders))

  async function create() {
    try {
      await createFolder.mutateAsync({ parentPath: parent, name: name.trim() })
      onNotify(t('folders.create.created', { name: name.trim() }))
      onClose()
    } catch (error) {
      onNotify(apiErrorMessage(error, t('folders.create.failed')), 'error')
    }
  }

  return (
    <Modal
      icon={<FolderPlusIcon />}
      title={t('folders.create.title')}
      onClose={onClose}
      initialFocusRef={nameRef}
    >
      {/* A form, so Enter submits the way it does in the admin dialogs. */}
      <form
        onSubmit={event => {
          event.preventDefault()
          void create()
        }}
      >
        <div className="field-h">
          <label htmlFor="new-folder-name">{t('folders.create.name')}</label>
          <input
            id="new-folder-name"
            type="text"
            value={name}
            onChange={e => setName(e.target.value)}
            ref={nameRef}
          />
        </div>

        <div className="field-h">
          <label htmlFor="new-folder-parent">{t('folders.create.parent')}</label>
          <MenuSelect id="new-folder-parent" value={parent} onChange={setParent} options={[
            { value: '', label: t('folders.create.topLevel') },
            ...all.map(({ node, depth }) => ({ value: node.path, label: node.name, depth })),
          ]} />
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
    </Modal>
  )
}
