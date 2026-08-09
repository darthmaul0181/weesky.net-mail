import { useState, useEffect, useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import i18next from 'i18next'
import { api } from '../../../api.js'
import { QuotaMini } from '../../../components/QuotaBlock.jsx'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal.jsx'
import TrashIcon from '../../../icons/TrashIcon.jsx'
import PencilIcon from '../../../icons/PencilIcon.jsx'
import PersonPlusIcon from '../../../icons/PersonPlusIcon.jsx'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import AddEditUserModal from './AddEditUserModal.jsx'

export function AccountsTab({ addToast }) {
  const { t } = useTranslation('admin')
  const [users, setUsers] = useState([])
  const [domains, setDomains] = useState([])
  const [loading, setLoading] = useState(true)
  const [quotas, setQuotas] = useState({})
  const [search, setSearch] = useState('')
  const [userToEdit, setUserToEdit] = useState(null)
  const [userToDelete, setUserToDelete] = useState(null)
  const [showAddModal, setShowAddModal] = useState(false)
  const [deleting, setDeleting] = useState(false)

  // useCallback so the effect can depend on it: addToast is memoised, so load keeps one identity.
  const load = useCallback(async () => {
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
      addToast(i18next.t('admin:accounts.loadFailed'), 'error')
      setLoading(false)
    }
  }, [addToast])

  useEffect(() => { load() }, [load])

  async function handleDelete() {
    setDeleting(true)
    try {
      await api.adminDeleteUser(userToDelete.id)
      addToast(t('accounts.deleted', { account: `${userToDelete.userName}@${userToDelete.domainName}` }))
      setUserToDelete(null)
      load()
    } catch (err) {
      addToast(apiErrorMessage(err, t('accounts.deleteFailed')), 'error')
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
        <span className="admin-list-title">
          {term
            ? t('accounts.titleFiltered', { shown: visibleUsers.length, total: users.length })
            : t('accounts.title', { total: users.length })}
        </span>
        <input
          className="search-input"
          type="search"
          placeholder={t('search')}
          value={search}
          onChange={e => setSearch(e.target.value)}
          style={{ marginLeft: '30px', width: '180px', padding: '6px 10px', fontSize: '13px' }}
        />
        <button className="btn btn-primary" style={{ marginLeft: 'auto', width: 'auto', display: 'inline-flex', alignItems: 'center', gap: '6px' }}
          onClick={() => setShowAddModal(true)}>
          <PersonPlusIcon /> {t('actions.add', { ns: 'common' })}
        </button>
      </div>
      <div className="admin-list">
        {visibleUsers.map(u => (
          <div key={u.id} className="admin-list-item">
            <span className="admin-list-item-email">{u.userName}@{u.domainName}</span>
            <span className="admin-list-item-name" style={{ paddingLeft: '30px' }}>{u.fullName}</span>
            <div className="admin-list-item-quota"><QuotaMini quota={quotas[u.id]} /></div>
            <div className="admin-list-item-actions">
              <button className="admin-icon-btn" title={t('actions.edit', { ns: 'common' })}
                onClick={() => setUserToEdit(u)}>
                <PencilIcon />
              </button>
              <button className="admin-icon-btn is-danger" title={t('actions.delete', { ns: 'common' })}
                onClick={() => setUserToDelete(u)}>
                <TrashIcon />
              </button>
            </div>
          </div>
        ))}
      </div>
      {showAddModal && (
        <AddEditUserModal domains={domains} onSave={() => { setShowAddModal(false); load(); addToast(t('accounts.created')) }}
          onClose={() => setShowAddModal(false)} />
      )}
      {userToEdit && (
        <AddEditUserModal user={userToEdit} domains={domains}
          onSave={() => { setUserToEdit(null); load(); addToast(t('accounts.updated')) }}
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
