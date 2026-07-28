import { useState, useEffect, useCallback } from 'react'
import { api } from '../../../api.js'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal.jsx'
import TrashIcon from '../../../icons/TrashIcon.jsx'
import PencilIcon from '../../../icons/PencilIcon.jsx'
import GlobeIcon from '../../../icons/GlobeIcon.jsx'
import AddEditDomainModal from './AddEditDomainModal.jsx'

export function DomainsTab({ addToast }) {
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
      addToast('Failed to load domains', 'error')
    } finally {
      setLoading(false)
    }
  }, [addToast])

  useEffect(() => { load() }, [load])

  async function handleDelete() {
    setDeleting(true)
    try {
      await api.adminDeleteDomain(domainToDelete.id)
      addToast(`Domain ${domainToDelete.name} deleted`)
      setDomainToDelete(null)
      load()
    } catch (err) {
      addToast(err.message || 'Failed to delete domain', 'error')
    } finally {
      setDeleting(false)
    }
  }

  if (loading) return <div style={{ textAlign: 'center', padding: '32px' }}><span className="spinner" /></div>

  return (
    <div>
      <div className="admin-list-header">
        <span className="admin-list-title">Domains ({domains.length})</span>
        <button className="btn btn-primary" style={{ width: 'auto', display: 'inline-flex', alignItems: 'center', gap: '6px' }}
          onClick={() => setShowAddModal(true)}>
          <GlobeIcon /> Add
        </button>
      </div>
      <div className="admin-list">
        {domains.map(d => (
          <div key={d.id} className="admin-list-item">
            <span className="admin-list-item-email" style={{ minWidth: '60px' }}>{d.id}</span>
            <span className="admin-list-item-name">{d.name}</span>
            <div className="admin-list-item-actions">
              <button className="admin-icon-btn" title="Edit" onClick={() => setDomainToEdit(d)}>
                <PencilIcon />
              </button>
              <button className="admin-icon-btn is-danger" title="Delete" onClick={() => setDomainToDelete(d)}>
                <TrashIcon />
              </button>
            </div>
          </div>
        ))}
      </div>
      {showAddModal && (
        <AddEditDomainModal onSave={() => { setShowAddModal(false); load(); addToast('Domain created') }}
          onClose={() => setShowAddModal(false)} />
      )}
      {domainToEdit && (
        <AddEditDomainModal domain={domainToEdit}
          onSave={() => { setDomainToEdit(null); load(); addToast('Domain updated') }}
          onClose={() => setDomainToEdit(null)} />
      )}
      {domainToDelete && (
        <DeleteConfirmModal entityLabel={domainToDelete.name}
          onConfirm={handleDelete} onClose={() => setDomainToDelete(null)} loading={deleting} />
      )}
    </div>
  )
}

export default DomainsTab
