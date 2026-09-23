import { useState, type RefObject } from 'react'
import { useTranslation } from 'react-i18next'
import i18next from 'i18next'
import { QuotaMini } from '../../../components/QuotaBlock'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal'
import LoadingBlock from '../../../components/LoadingBlock'
import type { AddToast } from '../../../hooks/useToasts'
import TrashIcon from '../../../icons/TrashIcon'
import PencilIcon from '../../../icons/PencilIcon'
import PersonPlusIcon from '../../../icons/PersonPlusIcon'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import AddEditUserModal from './AddEditUserModal'
import ListLoadFailed from '../../../components/ListLoadFailed'
import type { AdminUser } from './adminTypes'
import {
  useAdminDomains, useAdminUserQuotas, useAdminUsers, useDeleteAdminUser, useListLoad,
} from './useAdminLists'

interface Props {
  addToast: AddToast
  /** The tab content, where focus goes when a confirmed delete takes the row's own button. */
  returnFocusRef?: RefObject<HTMLElement | null>
}

export function AccountsTab({ addToast, returnFocusRef }: Props) {
  const { t } = useTranslation('admin')
  const usersQuery = useAdminUsers()
  const domainsQuery = useAdminDomains()
  const users = usersQuery.data ?? []
  const domains = domainsQuery.data ?? []
  const quotas = useAdminUserQuotas(users.map(u => u.id))
  const deleteUser = useDeleteAdminUser()
  const [search, setSearch] = useState('')
  const [userToEdit, setUserToEdit] = useState<AdminUser | null>(null)
  const [userToDelete, setUserToDelete] = useState<AdminUser | null>(null)
  const [showAddModal, setShowAddModal] = useState(false)
  const usersFirstLoad = useListLoad(usersQuery, addToast, i18next.t('admin:accounts.loadFailed'))
  const domainsFirstLoad = useListLoad(
    domainsQuery, addToast, i18next.t('admin:accounts.domainsFailed'), usersQuery)
  const firstLoad = usersFirstLoad || domainsFirstLoad

  async function handleDelete() {
    if (!userToDelete) return
    try {
      await deleteUser.mutateAsync(userToDelete.id)
      addToast(t('accounts.deleted', { account: `${userToDelete.userName}@${userToDelete.domainName}` }))
      setUserToDelete(null)
    } catch (err) {
      addToast(apiErrorMessage(err, t('accounts.deleteFailed')), 'error')
    }
  }

  const term = search.trim().toLowerCase()
  const visibleUsers = term
    ? users.filter(u =>
        `${u.userName}@${u.domainName}`.toLowerCase().includes(term) ||
        (u.fullName ?? '').toLowerCase().includes(term))
    : users

  if (firstLoad) return <LoadingBlock />
  if (!usersQuery.data) return <ListLoadFailed>{t('accounts.loadFailedBody')}</ListLoadFailed>

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
            <div className="admin-list-item-quota"><QuotaMini quota={quotas.get(u.id) ?? null} /></div>
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
        <AddEditUserModal domains={domains} onSave={() => { setShowAddModal(false); addToast(t('accounts.created')) }}
          onClose={() => setShowAddModal(false)} />
      )}
      {userToEdit && (
        <AddEditUserModal user={userToEdit} domains={domains}
          onSave={() => { setUserToEdit(null); addToast(t('accounts.updated')) }}
          onClose={() => setUserToEdit(null)} />
      )}
      {userToDelete && (
        <DeleteConfirmModal entityLabel={`${userToDelete.userName}@${userToDelete.domainName}`}
          onConfirm={() => void handleDelete()} onClose={() => setUserToDelete(null)} loading={deleteUser.isPending}
          returnFocusRef={returnFocusRef} />
      )}
    </div>
  )
}

export default AccountsTab
