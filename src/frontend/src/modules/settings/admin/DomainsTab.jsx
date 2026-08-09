import { useState, useEffect, useCallback } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import i18next from 'i18next'
import { api } from '../../../api.js'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal.jsx'
import TrashIcon from '../../../icons/TrashIcon.jsx'
import PencilIcon from '../../../icons/PencilIcon.jsx'
import GlobeIcon from '../../../icons/GlobeIcon.jsx'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import AddEditDomainModal from './AddEditDomainModal.jsx'

export function DomainsTab({ addToast }) {
  const { t } = useTranslation('admin')
  const [domains, setDomains] = useState([])
  const [loading, setLoading] = useState(true)
  const [domainToEdit, setDomainToEdit] = useState(null)
  const [domainToDelete, setDomainToDelete] = useState(null)
  const [showAddModal, setShowAddModal] = useState(false)
  const [deleting, setDeleting] = useState(false)

  // useCallback so the effect can depend on it: addToast is memoised, so load keeps one identity.
  const load = useCallback(async () => {
    setLoading(true)
    try {
      setDomains(await api.adminGetDomains() ?? [])
    } catch {
      addToast(i18next.t('admin:domains.loadFailed'), 'error')
    } finally {
      setLoading(false)
    }
  }, [addToast])

  useEffect(() => { load() }, [load])

  async function handleDelete() {
    setDeleting(true)
    try {
      // The count came with the listing, so confirming here is the acknowledgement the API asks
      // for: without it the call is refused, which is what keeps a stale list from deleting
      // aliases nobody was shown.
      await api.adminDeleteDomain(domainToDelete.id, domainToDelete.aliasCount > 0)
      addToast(t('domains.deleted', { name: domainToDelete.name }))
      setDomainToDelete(null)
      load()
    } catch (err) {
      addToast(apiErrorMessage(err, t('domains.deleteFailed')), 'error')
    } finally {
      setDeleting(false)
    }
  }

  if (loading) return <div style={{ textAlign: 'center', padding: '32px' }}><span className="spinner" /></div>

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
        <AddEditDomainModal onSave={() => { setShowAddModal(false); load(); addToast(t('domains.created')) }}
          onClose={() => setShowAddModal(false)} />
      )}
      {domainToEdit && (
        <AddEditDomainModal domain={domainToEdit}
          onSave={() => { setDomainToEdit(null); load(); addToast(t('domains.updated')) }}
          onClose={() => setDomainToEdit(null)} />
      )}
      {domainToDelete && (
        <DeleteConfirmModal entityLabel={domainToDelete.name}
          message={domainToDelete.aliasCount > 0 ? (
            <Trans i18nKey="domains.deleteWithAliases" ns="admin" count={domainToDelete.aliasCount}
              components={{ name: <strong>{domainToDelete.name}</strong>, aliases: <strong /> }} />
          ) : undefined}
          onConfirm={handleDelete} onClose={() => setDomainToDelete(null)} loading={deleting} />
      )}
    </div>
  )
}

export default DomainsTab
