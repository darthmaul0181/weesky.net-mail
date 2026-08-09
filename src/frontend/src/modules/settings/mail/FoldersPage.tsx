import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import LoadingBlock from '../../../components/LoadingBlock'
import Toasts from '../../../components/Toasts.jsx'
import { useToasts } from '../../../hooks/useToasts.js'
import FolderIcon from '../../../icons/FolderIcon'
import FolderPlusIcon from '../../../icons/FolderPlusIcon'
import SlidersIcon from '../../../icons/SlidersIcon'
import CreateFolderModal from '../../mail/folders/CreateFolderModal'
import FolderManager from '../../mail/folders/FolderManager'
import { useFolders } from '../../mail/queries'
import SystemFoldersModal from './SystemFoldersModal'

/**
 * Everything about folders: the full list, plus the two dialogs acting across the whole set.
 * The mail column keeps its own New folder button; its Manage button leads here.
 */
export default function FoldersPage() {
  const { t } = useTranslation('settings')
  const { data: folders, isLoading, isError } = useFolders()
  const { toasts, addToast, removeToast } = useToasts()
  const [creating, setCreating] = useState(false)
  const [assigningRoles, setAssigningRoles] = useState(false)

  return (
    <div className="settings-page">
      <div className="settings-page-header">
        <h1 className="settings-page-title"><FolderIcon size={17} />{t('nav.folders')}</h1>
      </div>
      <p className="folders-page-hint">{t('folders.hint')}</p>

      <div className="folders-page-actions">
        <button type="button" className="btn btn-primary" onClick={() => setCreating(true)}>
          <FolderPlusIcon size={15} />{t('folders.newFolder')}
        </button>
        {/* btn-ghost: a bare `.btn` has no border and no background, and reads as text. */}
        <button type="button" className="btn btn-ghost" onClick={() => setAssigningRoles(true)}>
          <SlidersIcon size={15} />{t('folders.systemFolders')}
        </button>
      </div>

      {isLoading && <LoadingBlock />}
      {!isLoading && (isError || !folders) && <p>{t('folders.loadFailed')}</p>}
      {!isLoading && !isError && folders && (
        <FolderManager folders={folders} onNotify={addToast} />
      )}

      {creating && folders && (
        <CreateFolderModal
          folders={folders}
          onClose={() => setCreating(false)}
          onNotify={addToast}
        />
      )}

      {assigningRoles && (
        <SystemFoldersModal onClose={() => setAssigningRoles(false)} onNotify={addToast} />
      )}

      <Toasts toasts={toasts} onRemove={removeToast} />
    </div>
  )
}
