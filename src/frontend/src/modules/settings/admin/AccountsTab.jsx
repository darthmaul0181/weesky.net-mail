import { useState, useEffect } from 'react'
import { api } from '../../../api.js'
import { QuotaMini } from '../../../components/QuotaBlock.jsx'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal.jsx'
import TrashIcon from '../../../icons/TrashIcon.jsx'
import PencilIcon from '../../../icons/PencilIcon.jsx'
import PersonPlusIcon from '../../../icons/PersonPlusIcon.jsx'
import AddEditUserModal from './AddEditUserModal.jsx'

export function AccountsTab({ addToast }) {
  const [users, setUsers] = useState([])
  const [domains, setDomains] = useState([])
  const [loading, setLoading] = useState(true)
  const [quotas, setQuotas] = useState({})
  const [search, setSearch] = useState('')
  const [userToEdit, setUserToEdit] = useState(null)
  const [userToDelete, setUserToDelete] = useState(null)
  const [showAddModal, setShowAddModal] = useState(false)
  const [deleting, setDeleting] = useState(false)

  async function load() {
    setLoading(true)
    setQuotas({})
    try {
      const [u, d] = await Promise.all([api.adminGetUsers(), api.adminGetDomains()])
      const allUsers = u ?? []
      setUsers(allUsers)
      setDomains(d ?? [])
      setLoading(false)
      allUsers.forEach(async (user) => {
        try {
          const q = await api.adminGetUserQuota(user.id)
          setQuotas(prev => ({ ...prev, [user.id]: q }))
        } catch { /* quota unavailable for this user */ }
      })
    } catch {
      addToast('Failed to load accounts', 'error')
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [])

  async function handleDelete() {
    setDeleting(true)
    try {
      await api.adminDeleteUser(userToDelete.id)
      addToast(`${userToDelete.userName}@${userToDelete.domainName} deleted`)
      setUserToDelete(null)
      load()
    } catch (err) {
      addToast(err.message || 'Failed to delete user', 'error')
    } finally {
      setDeleting(false)
    }
  }

  const term = search.trim().toLowerCase()
  const visibleUsers = term
    ? users.filter(u =>
        `${u.userName}@${u.domainName}`.toLowerCase().includes(term) ||
        (u.fullName ?? '').toLowerCase().includes(term))
    : users

  if (loading) return <div style={{ textAlign: 'center', padding: '32px' }}><span className="spinner" /></div>

  return (
    <div>
      <div className="admin-list-header">
        <span className="admin-list-title">Accounts ({visibleUsers.length}{term ? ` / ${users.length}` : ''})</span>
        <input
          className="search-input"
          type="search"
          placeholder="Search…"
          value={search}
          onChange={e => setSearch(e.target.value)}
          style={{ marginLeft: '30px', width: '180px', padding: '6px 10px', fontSize: '13px' }}
        />
        <button className="btn btn-primary" style={{ marginLeft: 'auto', width: 'auto', display: 'inline-flex', alignItems: 'center', gap: '6px' }}
          onClick={() => setShowAddModal(true)}>
          <PersonPlusIcon /> Add
        </button>
      </div>
      <div className="admin-list">
        {visibleUsers.map(u => (
          <div key={u.id} className="admin-list-item">
            <span className="admin-list-item-email">{u.userName}@{u.domainName}</span>
            <span className="admin-list-item-name" style={{ paddingLeft: '30px' }}>{u.fullName}</span>
            <div className="admin-list-item-quota"><QuotaMini quota={quotas[u.id]} /></div>
            <div className="admin-list-item-actions">
              <button className="admin-icon-btn" title="Edit" onClick={() => setUserToEdit(u)}>
                <PencilIcon />
              </button>
              <button className="admin-icon-btn is-danger" title="Delete" onClick={() => setUserToDelete(u)}>
                <TrashIcon />
              </button>
            </div>
          </div>
        ))}
      </div>
      {showAddModal && (
        <AddEditUserModal domains={domains} onSave={() => { setShowAddModal(false); load(); addToast('Account created') }}
          onClose={() => setShowAddModal(false)} />
      )}
      {userToEdit && (
        <AddEditUserModal user={userToEdit} domains={domains}
          onSave={() => { setUserToEdit(null); load(); addToast('Account updated') }}
          onClose={() => setUserToEdit(null)} />
      )}
      {userToDelete && (
        <DeleteConfirmModal entityLabel={`${userToDelete.userName}@${userToDelete.domainName}`}
          onConfirm={handleDelete} onClose={() => setUserToDelete(null)} loading={deleting} />
      )}
    </div>
  )
}

export default AccountsTab
