import { useState, type RefObject } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import i18next from 'i18next'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal'
import LoadingBlock from '../../../components/LoadingBlock'
import type { AddToast } from '../../../hooks/useToasts'
import TrashIcon from '../../../icons/TrashIcon'
import PencilIcon from '../../../icons/PencilIcon'
import GlobeIcon from '../../../icons/GlobeIcon'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import AddEditDomainModal from './AddEditDomainModal'
import ListLoadFailed from '../../../components/ListLoadFailed'
import type { AdminDomain } from './adminTypes'
import { useAdminDomains, useDeleteAdminDomain, useListLoad } from './useAdminLists'

interface Props {
  addToast: AddToast
  /** The tab content, where focus goes when a confirmed delete takes the row's own button. */
  returnFocusRef?: RefObject<HTMLElement | null>
}

export function DomainsTab({ addToast, returnFocusRef }: Props) {
  const { t } = useTranslation('admin')
  const domainsQuery = useAdminDomains()
  const domains = domainsQuery.data
  const deleteDomain = useDeleteAdminDomain()
  const [domainToEdit, setDomainToEdit] = useState<AdminDomain | null>(null)
  const [domainToDelete, setDomainToDelete] = useState<AdminDomain | null>(null)
  const [showAddModal, setShowAddModal] = useState(false)
  const firstLoad = useListLoad(domainsQuery, addToast, i18next.t('admin:domains.loadFailed'))

  async function handleDelete() {
    if (!domainToDelete) return
    try {
      // The count came with the listing, so confirming here is the acknowledgement the API asks
      // for: without it the call is refused, which is what keeps a stale list from deleting
      // aliases nobody was shown.
      await deleteDomain.mutateAsync({ id: domainToDelete.id, deleteAliases: domainToDelete.aliasCount > 0 })
      addToast(t('domains.deleted', { name: domainToDelete.name }))
      setDomainToDelete(null)
    } catch (err) {
      addToast(apiErrorMessage(err, t('domains.deleteFailed')), 'error')
    }
  }

  if (firstLoad) return <LoadingBlock />
  if (!domains) return <ListLoadFailed>{t('domains.loadFailedBody')}</ListLoadFailed>

  return (
    <div>
      <div className="admin-list-header">
        <span className="admin-list-title">{t('domains.title', { total: domains.length })}</span>
        <button className="btn btn-primary" style={{ width: 'auto', display: 'inline-flex', alignItems: 'center', gap: '6px' }}
          onClick={() => setShowAddModal(true)}>
          <GlobeIcon /> {t('actions.add', { ns: 'common' })}
        </button>
      </div>
      <div className="admin-list">
        {domains.map(d => (
          <div key={d.id} className="admin-list-item">
            <span className="admin-list-item-email" style={{ minWidth: '60px' }}>{d.id}</span>
            <span className="admin-list-item-name">{d.name}</span>
            <div className="admin-list-item-actions">
              <button className="admin-icon-btn" title={t('actions.edit', { ns: 'common' })}
                onClick={() => setDomainToEdit(d)}>
                <PencilIcon />
              </button>
              <button className="admin-icon-btn is-danger" title={t('actions.delete', { ns: 'common' })}
                onClick={() => setDomainToDelete(d)}>
                <TrashIcon />
              </button>
            </div>
          </div>
        ))}
      </div>
      {showAddModal && (
        <AddEditDomainModal onSave={() => { setShowAddModal(false); addToast(t('domains.created')) }}
          onClose={() => setShowAddModal(false)} />
      )}
      {domainToEdit && (
        <AddEditDomainModal domain={domainToEdit}
          onSave={() => { setDomainToEdit(null); addToast(t('domains.updated')) }}
          onClose={() => setDomainToEdit(null)} />
      )}
      {domainToDelete && (
        <DeleteConfirmModal entityLabel={domainToDelete.name}
          message={domainToDelete.aliasCount > 0 ? (
            <Trans i18nKey="domains.deleteWithAliases" ns="admin" count={domainToDelete.aliasCount}
              components={{ name: <strong>{domainToDelete.name}</strong>, aliases: <strong /> }} />
          ) : undefined}
          onConfirm={() => void handleDelete()} onClose={() => setDomainToDelete(null)} loading={deleteDomain.isPending}
          returnFocusRef={returnFocusRef} />
      )}
    </div>
  )
}

export default DomainsTab
