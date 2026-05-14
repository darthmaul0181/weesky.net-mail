import { useState, useEffect, useCallback, useRef } from 'react'
import { api, clearToken, setIsAdmin } from '../api.js'
import logoCircle from '../assets/logo_circle.jpg'
import weeskyLogo from '../assets/weesky_net.png'

function useToasts() {
  const [toasts, setToasts] = useState([])

  const removeToast = useCallback((id) => {
    setToasts(prev => prev.filter(t => t.id !== id))
  }, [])

  const addToast = useCallback((message, type = 'success') => {
    const id = Date.now()
    setToasts(prev => [...prev, { id, message, type }])
    if (type !== 'error') {
      setTimeout(() => setToasts(prev => prev.filter(t => t.id !== id)), 3000)
    }
  }, [])

  return { toasts, addToast, removeToast }
}

export function Toasts({ toasts, onRemove }) {
  if (!toasts.length) return null
  return (
    <div className="toast-container">
      {toasts.map(t => (
        <div key={t.id} className={`toast toast-${t.type}`}>
          <span>{t.message}</span>
          {t.type === 'error' && (
            <button className="toast-close" onClick={() => onRemove(t.id)}>✕</button>
          )}
        </div>
      ))}
    </div>
  )
}

function LockIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width="15" height="15" viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <rect x="3" y="11" width="18" height="11" rx="2" ry="2" />
      <path d="M7 11V7a5 5 0 0 1 10 0v4" />
    </svg>
  )
}

function TrashIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width="15" height="15" viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="3 6 5 6 21 6" />
      <path d="M19 6l-1 14H6L5 6" />
      <path d="M10 11v6" />
      <path d="M14 11v6" />
      <path d="M9 6V4h6v2" />
    </svg>
  )
}

function PencilIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width="13" height="13" viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
      <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z" />
    </svg>
  )
}

function CheckIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width="13" height="13" viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="20 6 9 17 4 12" />
    </svg>
  )
}

function XIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width="13" height="13" viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
      <line x1="18" y1="6" x2="6" y2="18" />
      <line x1="6" y1="6" x2="18" y2="18" />
    </svg>
  )
}

function ShieldIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width="15" height="15" viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
    </svg>
  )
}

function PersonPlusIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width="15" height="15" viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2" />
      <circle cx="9" cy="7" r="4" />
      <line x1="19" y1="8" x2="19" y2="14" />
      <line x1="22" y1="11" x2="16" y2="11" />
    </svg>
  )
}

function GlobeIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width="15" height="15" viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="12" cy="12" r="10" />
      <line x1="2" y1="12" x2="22" y2="12" />
      <path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z" />
    </svg>
  )
}

export function ChangePasswordModal({ onClose }) {
  const [oldPassword, setOldPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [error, setError] = useState(null)
  const [success, setSuccess] = useState(false)
  const [loading, setLoading] = useState(false)

  async function handleSubmit(e) {
    e.preventDefault()
    if (newPassword !== confirm) {
      setError('Passwords do not match.')
      return
    }
    setError(null)
    setLoading(true)
    try {
      await api.changePassword(oldPassword, newPassword)
      setSuccess(true)
    } catch {
      setError('Current password is incorrect.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title"><LockIcon />Change password</span>
          <button className="modal-close" onClick={onClose}>✕</button>
        </div>

        {success ? (
          <div className="modal-success">Password changed successfully.</div>
        ) : (
          <form onSubmit={handleSubmit}>
            {error && <div className="alert alert-error">{error}</div>}
            <div className="field">
              <label htmlFor="old-password">Current password</label>
              <input id="old-password" type="password" value={oldPassword}
                onChange={e => setOldPassword(e.target.value)} required autoFocus />
            </div>
            <div className="field">
              <label htmlFor="new-password">New password</label>
              <input id="new-password" type="password" value={newPassword}
                onChange={e => setNewPassword(e.target.value)} required />
            </div>
            <div className="field">
              <label htmlFor="confirm-password">Confirm new password</label>
              <input id="confirm-password" type="password" value={confirm}
                onChange={e => setConfirm(e.target.value)} required />
            </div>
            <button className="btn btn-primary" type="submit" disabled={loading}>
              {loading ? <span className="spinner" /> : 'Update password'}
            </button>
          </form>
        )}
      </div>
    </div>
  )
}

const MB = 1024 * 1024
const GB = 1024 * MB

export function QuotaBlock({ quota }) {
  if (!quota || !quota.storageBytesLimit) return null

  const useGb = Math.max(quota.storageBytesUsed, quota.storageBytesLimit) >= GB
  const divisor = useGb ? GB : MB
  const unit = useGb ? 'GB' : 'MB'
  const used = quota.storageBytesUsed / divisor
  const total = quota.storageBytesLimit / divisor
  const percent = Math.min(100, Math.max(0, (quota.storageBytesUsed / quota.storageBytesLimit) * 100))
  const format = v => (v >= 100 ? v.toFixed(0) : v.toFixed(1))
  const levelClass = percent >= 90 ? 'is-danger' : percent >= 75 ? 'is-warn' : ''

  return (
    <div className="panel-quota">
      <div className="panel-quota-label">Storage</div>
      <div className="panel-quota-values">
        <span className="panel-quota-used">{format(used)} {unit}</span>
        <span className="panel-quota-sep"> / </span>
        <span className="panel-quota-total">{format(total)} {unit}</span>
        <span className="panel-quota-percent">{percent.toFixed(0)}%</span>
      </div>
      <div className={`panel-quota-bar ${levelClass}`}>
        <div className="panel-quota-bar-fill" style={{ width: `${percent}%` }} />
      </div>
    </div>
  )
}

export function QuotaMini({ quota }) {
  if (!quota || !quota.storageBytesLimit) return <span style={{ fontSize: '12px', color: 'var(--text-muted)' }}>—</span>

  const useGb = Math.max(quota.storageBytesUsed, quota.storageBytesLimit) >= GB
  const divisor = useGb ? GB : MB
  const unit = useGb ? 'GB' : 'MB'
  const used = quota.storageBytesUsed / divisor
  const total = quota.storageBytesLimit / divisor
  const percent = Math.min(100, Math.max(0, (quota.storageBytesUsed / quota.storageBytesLimit) * 100))
  const format = v => (v >= 100 ? v.toFixed(0) : v.toFixed(1))
  const levelClass = percent >= 90 ? 'is-danger' : percent >= 75 ? 'is-warn' : ''

  return (
    <div style={{ width: '145px' }}>
      <div style={{ fontSize: '11px', color: 'var(--text-muted)', marginBottom: '4px' }}>
        {format(used)} / {format(total)} {unit}
      </div>
      <div className={`panel-quota-bar ${levelClass}`}>
        <div className="panel-quota-bar-fill" style={{ width: `${percent}%` }} />
      </div>
    </div>
  )
}

export function AccountPanel({ initials, fullName, primaryEmail, subDomains, quota, onLogout, onChangePassword, onAdmin, isAdmin, alphaMode, onAlphaModeChange, onFullNameChange }) {
  const [open, setOpen] = useState(false)
  const [editing, setEditing] = useState(false)
  const [editValue, setEditValue] = useState('')
  const [saving, setSaving] = useState(false)
  const panelRef = useRef(null)
  const inputRef = useRef(null)

  useEffect(() => {
    function handleClickOutside(e) {
      if (panelRef.current && !panelRef.current.contains(e.target)) {
        setOpen(false)
      }
    }
    if (open) document.addEventListener('mousedown', handleClickOutside)
    return () => document.removeEventListener('mousedown', handleClickOutside)
  }, [open])

  function handleEdit() {
    setEditValue(fullName)
    setEditing(true)
    setTimeout(() => inputRef.current?.focus(), 0)
  }

  function handleCancel() {
    setEditing(false)
  }

  async function handleConfirm() {
    setSaving(true)
    try {
      await api.changeFullName(editValue.trim())
      onFullNameChange(editValue.trim())
      setEditing(false)
    } catch {
      // stay in edit mode on error
    } finally {
      setSaving(false)
    }
  }

  return (
    <>
      <button className="avatar-btn" onClick={() => setOpen(o => !o)} title={primaryEmail}>
        {initials}
      </button>

      {open && (
        <>
          <div className="panel-overlay" onClick={() => setOpen(false)} />
          <div className="account-panel" ref={panelRef}>
            {editing ? (
              <div className="panel-fullname-edit">
                <input
                  ref={inputRef}
                  className="panel-fullname-input"
                  value={editValue}
                  onChange={e => setEditValue(e.target.value)}
                  onKeyDown={e => { if (e.key === 'Enter') handleConfirm() }}
                  maxLength={255}
                  disabled={saving}
                />
                <button className="panel-fullname-btn panel-fullname-confirm" onClick={handleConfirm} disabled={saving} title="Confirm">
                  {saving ? <span className="spinner spinner-sm" /> : <CheckIcon />}
                </button>
                <button className="panel-fullname-btn panel-fullname-cancel" onClick={handleCancel} disabled={saving} title="Cancel">
                  <XIcon />
                </button>
              </div>
            ) : (
              <div className="panel-fullname-row">
                <span className="panel-fullname">{fullName || primaryEmail}</span>
                <button className="panel-fullname-pencil" onClick={handleEdit} title="Edit name">
                  <PencilIcon />
                </button>
              </div>
            )}
            <div className="panel-mailbox-row">
              <span className="panel-mailbox-label">Main mailbox</span>
              <span className="panel-mailbox-sep">&nbsp;:&nbsp;</span>
              <span className="panel-mailbox-value">{primaryEmail}</span>
            </div>

            {subDomains.length > 0 && (
              <div className="panel-subdomains">
                <div className="panel-subdomains-label">Other domains</div>
                {subDomains.map(d => (
                  <div key={d.id} className="panel-subdomain-item">{d.name}</div>
                ))}
              </div>
            )}

            <QuotaBlock quota={quota} />

            <div className="panel-settings">
              <div className="panel-quota-label" style={{ marginBottom: '10px' }}>Options</div>
              <div className="toggle-row">
                <span className="toggle-label">Alphabetical mode</span>
                <label className="toggle-switch">
                  <input
                    type="checkbox"
                    checked={alphaMode}
                    onChange={e => onAlphaModeChange(e.target.checked)}
                  />
                  <span className="toggle-track" />
                </label>
              </div>
            </div>

            <div className="panel-actions">
              {isAdmin && (
                <button className="panel-link" onClick={() => { setOpen(false); onAdmin() }}>
                  <ShieldIcon />
                  Administration
                </button>
              )}
              <button className="panel-link" onClick={() => { setOpen(false); onChangePassword() }}>
                <LockIcon />
                Change password
              </button>
              <button className="panel-link panel-link-danger" onClick={onLogout}>
                <svg xmlns="http://www.w3.org/2000/svg" width="15" height="15" viewBox="0 0 24 24"
                  fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                  <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
                  <polyline points="16 17 21 12 16 7" />
                  <line x1="21" y1="12" x2="9" y2="12" />
                </svg>
                Sign out
              </button>
            </div>
          </div>
        </>
      )}
    </>
  )
}

export function DeleteConfirmModal({ entityLabel, onConfirm, onClose, loading }) {
  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">Confirm deletion</span>
          <button className="modal-close" onClick={onClose}>✕</button>
        </div>
        <p style={{ margin: '0 0 20px', fontSize: '14px' }}>
          Delete <strong>{entityLabel}</strong>? This action cannot be undone.
        </p>
        <div style={{ display: 'flex', gap: '8px', justifyContent: 'flex-end' }}>
          <button className="btn" onClick={onClose} disabled={loading}>Cancel</button>
          <button className="btn btn-primary" style={{ width: 'auto', background: 'var(--danger, #dc2626)', borderColor: 'var(--danger, #dc2626)' }}
            onClick={onConfirm} disabled={loading}>
            {loading ? <span className="spinner" /> : 'Delete'}
          </button>
        </div>
      </div>
    </div>
  )
}

export function AddEditUserModal({ user, domains, onSave, onClose }) {
  const [userName, setUserName] = useState(user?.userName ?? '')
  const [domainId, setDomainId] = useState(user?.domainId ?? domains[0]?.id ?? '')
  const [password, setPassword] = useState('')
  const [fullName, setFullName] = useState(user?.fullName ?? '')
  const [quotaMb, setQuotaMb] = useState(user?.quotaMb ?? 1024)
  const [active, setActive] = useState(user?.active ?? true)
  const [admin, setAdmin] = useState(user?.admin ?? false)
  const [error, setError] = useState(null)
  const [loading, setLoading] = useState(false)
  const isEdit = !!user

  function handleQuotaSlider(v) {
    const n = Math.max(1, Math.min(10240, Number(v)))
    setQuotaMb(n)
  }

  async function handleSubmit(e) {
    e.preventDefault()
    if (!isEdit && !password) { setError('Password is required'); return }
    setError(null)
    setLoading(true)
    try {
      const payload = {
        userName,
        domainId,
        password: password || null,
        fullName,
        quotaMb,
        active,
        admin,
      }
      if (isEdit) {
        await api.adminUpdateUser(user.id, payload)
      } else {
        await api.adminCreateUser(payload)
      }
      onSave()
    } catch (err) {
      setError(err.message || 'An error occurred')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" style={{ maxWidth: '600px' }} onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">{isEdit ? <PencilIcon /> : <PersonPlusIcon />}{isEdit ? 'Edit account' : 'Add account'}</span>
          <button className="modal-close" onClick={onClose}>✕</button>
        </div>
        <form onSubmit={handleSubmit}>
          {error && <div className="alert alert-error">{error}</div>}
          <div className="field-h">
            <label>Username</label>
            <input type="text" value={userName} onChange={e => setUserName(e.target.value)}
              disabled={isEdit} required />
          </div>
          <div className="field-h">
            <label>Domain</label>
            <select value={domainId} onChange={e => setDomainId(e.target.value)} disabled={isEdit}>
              {domains.map(d => <option key={d.id} value={d.id}>{d.name}</option>)}
            </select>
          </div>
          <div className="field-h">
            <label>Password</label>
            <input type="password" value={password} onChange={e => setPassword(e.target.value)}
              placeholder={isEdit ? 'leave blank to keep' : ''} />
          </div>
          <div className="field-h">
            <label>Full name</label>
            <input type="text" value={fullName} onChange={e => setFullName(e.target.value)} />
          </div>
          <div className="field-h">
            <label>Quota (MB)</label>
            <div className="quota-field">
              <input type="range" min={1} max={10240} value={quotaMb}
                onChange={e => handleQuotaSlider(e.target.value)} />
              <input type="number" min={1} max={10240} value={quotaMb}
                onChange={e => handleQuotaSlider(e.target.value)} />
              <span className="quota-field-unit">MB</span>
            </div>
          </div>
          <div className="field-h">
            <label>Active</label>
            <label className="toggle-switch">
              <input type="checkbox" checked={active} onChange={e => setActive(e.target.checked)} />
              <span className="toggle-track" />
            </label>
          </div>
          <div className="field-h">
            <label>Administrator</label>
            <label className="toggle-switch">
              <input type="checkbox" checked={admin} onChange={e => setAdmin(e.target.checked)} />
              <span className="toggle-track" />
            </label>
          </div>
          <button className="btn btn-primary" type="submit"
            disabled={loading || !userName.trim() || (!isEdit && !password.trim())}
            style={{ marginTop: '8px' }}>
            {loading ? <span className="spinner" /> : (isEdit ? 'Save changes' : 'Create account')}
          </button>
        </form>
      </div>
    </div>
  )
}

const DOMAIN_RE = /^([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,}$/

export function AddEditDomainModal({ domain, onSave, onClose }) {
  const [id, setId] = useState(domain?.id ?? '')
  const [name, setName] = useState(domain?.name ?? '')
  const [error, setError] = useState(null)
  const [loading, setLoading] = useState(false)
  const isEdit = !!domain
  const nameValid = DOMAIN_RE.test(name)

  async function handleSubmit(e) {
    e.preventDefault()
    setError(null)
    setLoading(true)
    try {
      if (isEdit) {
        await api.adminUpdateDomain(domain.id, { id, name })
      } else {
        await api.adminCreateDomain({ id, name })
      }
      onSave()
    } catch (err) {
      setError(err.message || 'An error occurred')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" style={{ maxWidth: '380px' }} onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">{isEdit ? <><PencilIcon /> Edit domain</> : <><GlobeIcon /> Add domain</>}</span>
          <button className="modal-close" onClick={onClose}>✕</button>
        </div>
        <form onSubmit={handleSubmit}>
          {error && <div className="alert alert-error">{error}</div>}
          <div className="field">
            <label>ID (3 chars max)</label>
            <input type="text" value={id} onChange={e => setId(e.target.value.toUpperCase())}
              maxLength={3} disabled={isEdit} required />
          </div>
          <div className="field">
            <label>Domain name</label>
            <input type="text" value={name} onChange={e => setName(e.target.value)} required
              className={name && !nameValid ? 'is-error' : undefined} />
          </div>
          <button className="btn btn-primary" type="submit"
            disabled={loading || !id.trim() || !nameValid}>
            {loading ? <span className="spinner" /> : (isEdit ? 'Save changes' : 'Create domain')}
          </button>
        </form>
      </div>
    </div>
  )
}

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

export function DomainsTab({ addToast }) {
  const [domains, setDomains] = useState([])
  const [loading, setLoading] = useState(true)
  const [domainToEdit, setDomainToEdit] = useState(null)
  const [domainToDelete, setDomainToDelete] = useState(null)
  const [showAddModal, setShowAddModal] = useState(false)
  const [deleting, setDeleting] = useState(false)

  async function load() {
    setLoading(true)
    try {
      setDomains(await api.adminGetDomains() ?? [])
    } catch {
      addToast('Failed to load domains', 'error')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [])

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

export function VirtualDomainsTab({ addToast }) {
  const [virtualDomains, setVirtualDomains] = useState([])
  const [users, setUsers] = useState([])
  const [loading, setLoading] = useState(true)
  const [editingDomainId, setEditingDomainId] = useState(null)
  const [searchQuery, setSearchQuery] = useState('')
  const [saving, setSaving] = useState(false)
  const editRef = useRef(null)

  async function load() {
    setLoading(true)
    try {
      const [o, u] = await Promise.all([api.adminGetVirtualDomains(), api.adminGetUsers()])
      setVirtualDomains(o ?? [])
      setUsers(u ?? [])
    } catch {
      addToast('Failed to load virtual domains', 'error')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { load() }, [])

  useEffect(() => {
    if (!editingDomainId) return
    function handleClick(e) {
      if (!editRef.current?.contains(e.target)) {
        setEditingDomainId(null)
        setSearchQuery('')
      }
    }
    document.addEventListener('mousedown', handleClick)
    return () => document.removeEventListener('mousedown', handleClick)
  }, [editingDomainId])

  async function handleSelect(domainId, userId) {
    setSaving(true)
    try {
      const updated = await api.adminAddVirtualDomainOwner(domainId, userId)
      setSearchQuery('')
      setVirtualDomains(prev => prev.map(o => o.domainId === domainId ? updated : o))
    } catch (err) {
      addToast(err.message || 'Failed to set owner', 'error')
    } finally {
      setSaving(false)
    }
  }

  async function handleUnlink(domainId, userId) {
    setSaving(true)
    try {
      await api.adminRemoveVirtualDomainOwner(domainId, userId)
      setVirtualDomains(prev => prev.map(o =>
        o.domainId === domainId
          ? { ...o, owners: o.owners.filter(own => own.ownerId !== userId) }
          : o
      ))
    } catch (err) {
      addToast(err.message || 'Failed to remove owner', 'error')
    } finally {
      setSaving(false)
    }
  }

  const editingVirtualDomain = virtualDomains.find(o => o.domainId === editingDomainId)
  const editingOwnerIds = new Set((editingVirtualDomain?.owners ?? []).map(own => own.ownerId))

  const term = searchQuery.trim().toLowerCase()
  const filteredUsers = term
    ? users.filter(u => {
        if (editingOwnerIds.has(u.id)) return false
        const email = `${u.userName}@${u.domainName}`.toLowerCase()
        const name = (u.fullName ?? '').toLowerCase()
        return email.includes(term) || name.includes(term)
      })
    : []

  if (loading) return <div style={{ textAlign: 'center', padding: '32px' }}><span className="spinner" /></div>

  return (
    <div>
      <div className="admin-list-header">
        <span className="admin-list-title">Virtual alias domains ({virtualDomains.length})</span>
      </div>
      <div className="admin-list">
        {virtualDomains.length === 0 && (
          <div style={{ padding: '24px', textAlign: 'center', color: 'var(--text-muted)', fontSize: '13px' }}>
            No virtual alias domains
          </div>
        )}
        {virtualDomains.map(o => (
          <div key={o.domainId} className="admin-list-item" style={{ alignItems: 'flex-start', paddingTop: '10px', paddingBottom: '10px' }}>
            <span className="admin-list-item-email" style={{ paddingTop: '4px' }}>{o.domainName} <span style={{ color: 'var(--text-muted)', fontWeight: 400 }}>({o.domainId})</span></span>
            {editingDomainId === o.domainId ? (
              <div ref={editRef} style={{ flex: 1, paddingLeft: '30px' }}>
                {o.owners.length > 0 && (
                  <div style={{ display: 'flex', flexWrap: 'wrap', gap: '4px', marginBottom: '6px' }}>
                    {o.owners.map(own => (
                      <span key={own.ownerId} className="ownership-tile">
                        {own.ownerEmail}
                        <button
                          className="ownership-tile-remove"
                          title="Remove owner"
                          disabled={saving}
                          onMouseDown={e => { e.preventDefault(); handleUnlink(o.domainId, own.ownerId) }}
                        >
                          <TrashIcon />
                        </button>
                      </span>
                    ))}
                  </div>
                )}
                <div style={{ position: 'relative' }}>
                  <input
                    className="search-input"
                    type="text"
                    placeholder="Search user…"
                    value={searchQuery}
                    onChange={e => setSearchQuery(e.target.value)}
                    autoFocus
                    style={{ width: '100%', padding: '5px 8px', fontSize: '13px' }}
                    onKeyDown={e => {
                      if (e.key === 'Escape') { setEditingDomainId(null); setSearchQuery('') }
                    }}
                  />
                  {filteredUsers.length > 0 && (
                    <div className="ownership-dropdown">
                      {filteredUsers.slice(0, 10).map(u => (
                        <button
                          key={u.id}
                          className="ownership-dropdown-option"
                          disabled={saving}
                          onMouseDown={e => { e.preventDefault(); handleSelect(o.domainId, u.id) }}
                        >
                          <span style={{ fontWeight: 600 }}>{u.userName}@{u.domainName}</span>
                          {u.fullName && <span style={{ color: 'var(--text-muted)', fontSize: '12px', marginLeft: '8px' }}>{u.fullName}</span>}
                        </button>
                      ))}
                    </div>
                  )}
                </div>
              </div>
            ) : (
              <div style={{ flex: 1, paddingLeft: '30px', display: 'flex', flexWrap: 'wrap', gap: '4px', paddingTop: '2px' }}>
                {o.owners.length === 0
                  ? <span style={{ color: 'var(--text-muted)', fontSize: '13px' }}>—</span>
                  : o.owners.map(own => (
                      <span key={own.ownerId} className="ownership-tile">{own.ownerEmail}</span>
                    ))
                }
              </div>
            )}
            <div className="admin-list-item-actions">
              {editingDomainId !== o.domainId && (
                <button className="admin-icon-btn" title="Edit owner" onClick={() => {
                  setEditingDomainId(o.domainId)
                  setSearchQuery('')
                }}>
                  <PencilIcon />
                </button>
              )}
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}

export function AdminModal({ onClose, addToast }) {
  const [activeTab, setActiveTab] = useState('accounts')

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal modal-admin" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title"><ShieldIcon /> Administration</span>
          <button className="modal-close" onClick={onClose}>✕</button>
        </div>
        <div className="admin-modal-body">
          <nav className="admin-tab-bar">
            <button className={`admin-tab${activeTab === 'accounts' ? ' is-active' : ''}`}
              onClick={() => setActiveTab('accounts')}>Accounts</button>
            <button className={`admin-tab${activeTab === 'domains' ? ' is-active' : ''}`}
              onClick={() => setActiveTab('domains')}>Domains</button>
            <button className={`admin-tab${activeTab === 'virtualdomains' ? ' is-active' : ''}`}
              onClick={() => setActiveTab('virtualdomains')}>Virtual domains</button>
          </nav>
          <div className="admin-tab-content">
            {activeTab === 'accounts' && <AccountsTab addToast={addToast} />}
            {activeTab === 'domains' && <DomainsTab addToast={addToast} />}
            {activeTab === 'virtualdomains' && <VirtualDomainsTab addToast={addToast} />}
          </div>
        </div>
      </div>
    </div>
  )
}

export default function AliasesPage({ onLogout }) {
  const { toasts, addToast, removeToast } = useToasts()

  const [domains, setDomains] = useState([])
  const [selectedDomain, setSelectedDomain] = useState('')
  const [greeting, setGreeting] = useState('')
  const [initials, setInitials] = useState('')
  const [fullName, setFullName] = useState('')
  const [primaryEmail, setPrimaryEmail] = useState('')
  const [subDomains, setSubDomains] = useState([])
  const [quota, setQuota] = useState(null)

  const [aliases, setAliases] = useState([])
  const [loadingList, setLoadingList] = useState(true)
  const [listError, setListError] = useState(null)
  const [search, setSearch] = useState('')

  const [adding, setAdding] = useState(false)

  const [deletingKey, setDeletingKey] = useState(null)
  const [highlightedKey, setHighlightedKey] = useState(null)
  const [changePasswordOpen, setChangePasswordOpen] = useState(false)
  const [adminOpen, setAdminOpen] = useState(false)
  const [isAdminUser, setIsAdminUser] = useState(false)
  const [alphaMode, setAlphaMode] = useState(() => localStorage.getItem('alias_alpha_mode') === 'true')

  function handleAlphaModeChange(value) {
    setAlphaMode(value)
    localStorage.setItem('alias_alpha_mode', String(value))
  }

  const scrollRef = useRef(null)
  const groupRefs = useRef({})
  const [activeLetter, setActiveLetter] = useState('')

  useEffect(() => {
    api.getAccount().then(data => {
      const list = data?.domains ?? []
      setDomains(list)

      const primaryDomain = list.find(d => d.id === data.mailbox)
      const defaultDomain = primaryDomain ?? list[0]
      const domainName = defaultDomain?.name ?? ''

      const email = domainName ? `${data.userName}@${domainName}` : data.userName

      setFullName(data.fullName ?? '')
      setGreeting(data.fullName || email)
      setPrimaryEmail(email)
      setInitials(
        (data.userName?.[0] ?? '').toUpperCase() +
        (domainName?.[0] ?? data.mailbox?.[0] ?? '').toUpperCase()
      )
      setSubDomains(primaryDomain ? list.filter(d => d.id !== data.mailbox) : list)
      setIsAdminUser(data?.isAdmin === true)
      setIsAdmin(data?.isAdmin === true)

      if (domainName) setSelectedDomain(domainName)
    }).catch(() => {})

    api.getQuota().then(setQuota).catch(() => {})
  }, [])

  const fetchAliases = useCallback(async () => {
    setLoadingList(true)
    setListError(null)
    try {
      const data = await api.getAliases()
      setAliases(data ?? [])
    } catch {
      setListError('Failed to load aliases.')
    } finally {
      setLoadingList(false)
    }
  }, [])

  useEffect(() => { fetchAliases() }, [fetchAliases])

  const visibleAliases = aliases
    .filter(a => !selectedDomain || a.domain === selectedDomain)
    .filter(a => !search || `${a.name}@${a.domain}`.includes(search.toLowerCase()))
    .sort((a, b) => a.name.localeCompare(b.name))

  const grouped = []
  const groupMap = {}
  for (const a of visibleAliases) {
    const letter = a.name[0]?.toUpperCase() ?? '#'
    if (!groupMap[letter]) {
      groupMap[letter] = []
      grouped.push([letter, groupMap[letter]])
    }
    groupMap[letter].push(a)
  }
  const availableLetters = grouped.map(([l]) => l)
  const effectiveActiveLetter = availableLetters.includes(activeLetter)
    ? activeLetter
    : (availableLetters[0] ?? '')

  async function handleDelete(name, domain) {
    const key = `${name}@${domain}`
    setDeletingKey(key)
    try {
      await api.deleteAlias(name, domain)
      setAliases(prev => prev.filter(a => !(a.name === name && a.domain === domain)))
      addToast(`${key} deleted`)
    } catch {
      fetchAliases()
    } finally {
      setDeletingKey(null)
    }
  }

  async function handleAdd() {
    setAdding(true)
    try {
      await api.createAlias(search, selectedDomain)
      const key = `${search}@${selectedDomain}`
      addToast(`${key} added`)
      setSearch('')
      await fetchAliases()
      setHighlightedKey(key)
    } catch (err) {
      addToast(err.message || 'Failed to create alias.', 'error')
    } finally {
      setAdding(false)
    }
  }

  function handleScroll() {
    const container = scrollRef.current
    if (!container) return
    const containerTop = container.getBoundingClientRect().top
    let current = availableLetters[0] ?? ''
    for (const letter of availableLetters) {
      const el = groupRefs.current[letter]
      if (el && el.getBoundingClientRect().top - containerTop <= 8) current = letter
    }
    if (current !== activeLetter) setActiveLetter(current)
  }

  function scrollToLetter(letter) {
    const el = groupRefs.current[letter]
    const container = scrollRef.current
    if (!el || !container) return
    container.scrollTop += el.getBoundingClientRect().top - container.getBoundingClientRect().top
  }

  function handleFullNameChange(newName) {
    setFullName(newName)
    setGreeting(newName || primaryEmail)
  }

  function handleLogout() {
    clearToken()
    onLogout()
  }

  return (
    <>
      <header className="site-header">
        <div className="site-header-brand">
          <img src={logoCircle} alt="" className="site-header-circle" />
          <img src={weeskyLogo} alt="weesky.net" className="site-header-logo" />
        </div>
        <AccountPanel
          initials={initials}
          fullName={fullName}
          primaryEmail={primaryEmail}
          subDomains={subDomains}
          quota={quota}
          onLogout={handleLogout}
          onChangePassword={() => setChangePasswordOpen(true)}
          onAdmin={() => setAdminOpen(true)}
          isAdmin={isAdminUser}
          onFullNameChange={handleFullNameChange}
          alphaMode={alphaMode}
          onAlphaModeChange={handleAlphaModeChange}
        />
      </header>

    <div className="page-main">
      <div className="header">
        <div>
          <div className="header-title">{greeting ? `Hello ${greeting} !` : 'Aliases'}</div>
          <div className="header-sub">Mail alias management</div>
        </div>
      </div>

      <div className="domain-toolbar">
        {domains.length > 1 && (
          <>
            <label htmlFor="domain-select" className="domain-label">Domain</label>
            <select
              id="domain-select"
              className="domain-select"
              value={selectedDomain}
              onChange={e => setSelectedDomain(e.target.value)}
            >
              {domains.map(d => (
                <option key={d.id} value={d.name}>{d.name}</option>
              ))}
            </select>
          </>
        )}
        <input
          className={`search-input${search.length > 30 ? ' is-error' : ''}`}
          type="search"
          placeholder="Search or create…"
          value={search}
          onChange={e => {
            const val = e.target.value
            if (val.length > 30 && search.length <= 30) {
              addToast('An alias cannot exceed 30 characters', 'error')
            }
            setSearch(val)
          }}
          onKeyDown={e => {
            if (e.key === 'Enter' && !adding && selectedDomain && search.trim() && search.length <= 30) {
              handleAdd()
            }
          }}
        />
        <button
          className="btn btn-add"
          onClick={handleAdd}
          disabled={adding || !selectedDomain || !search.trim() || search.length > 30}
        >
          {adding ? <span className="spinner" /> : 'Create alias'}
        </button>
      </div>

      {listError && <div className="alert alert-error">{listError}</div>}

      {loadingList ? (
        <div className="loading-center">
          <span className="spinner" />
        </div>
      ) : visibleAliases.length === 0 ? (
        <div className="alias-empty-grid">No aliases for this domain.</div>
      ) : alphaMode ? (
        <div className="alias-view-wrapper">
          <div className="alias-scroll-area" ref={scrollRef} onScroll={handleScroll}>
            {grouped.map(([letter, groupAliases]) => (
              <div key={letter} className="alias-group">
                <div
                  className="alias-group-header"
                  ref={el => { groupRefs.current[letter] = el }}
                >
                  <span className="alias-group-letter">{letter}</span>
                  <div className="alias-group-divider" />
                </div>
                <div className="alias-grid">
                  {groupAliases.map(a => {
                    const key = `${a.name}@${a.domain}`
                    const isNew = highlightedKey === key
                    return (
                      <div
                        className={isNew ? 'alias-tile alias-tile-new' : 'alias-tile'}
                        key={key}
                        onAnimationEnd={isNew ? () => setHighlightedKey(null) : undefined}
                      >
                        <span className="alias-tile-name">{a.name}</span>
                        <span className="alias-tile-domain">@{a.domain}</span>
                        <button
                          className="alias-tile-delete"
                          onClick={() => handleDelete(a.name, a.domain)}
                          disabled={deletingKey === key}
                          title="Delete"
                        >
                          {deletingKey === key ? <span className="spinner" /> : <TrashIcon />}
                        </button>
                      </div>
                    )
                  })}
                </div>
              </div>
            ))}
          </div>
          <div className="alpha-nav">
            {availableLetters.map(letter => (
              <button
                key={letter}
                className={`alpha-nav-letter${effectiveActiveLetter === letter ? ' is-active' : ''}`}
                onClick={() => scrollToLetter(letter)}
              >
                {letter}
              </button>
            ))}
          </div>
        </div>
      ) : (
        <div className="alias-grid">
          {visibleAliases.map(a => {
            const key = `${a.name}@${a.domain}`
            const isNew = highlightedKey === key
            return (
              <div
                className={isNew ? 'alias-tile alias-tile-new' : 'alias-tile'}
                key={key}
                onAnimationEnd={isNew ? () => setHighlightedKey(null) : undefined}
              >
                <span className="alias-tile-name">{a.name}</span>
                <span className="alias-tile-domain">@{a.domain}</span>
                <button
                  className="alias-tile-delete"
                  onClick={() => handleDelete(a.name, a.domain)}
                  disabled={deletingKey === key}
                  title="Delete"
                >
                  {deletingKey === key ? <span className="spinner" /> : <TrashIcon />}
                </button>
              </div>
            )
          })}
        </div>
      )}

      {changePasswordOpen && (
        <ChangePasswordModal onClose={() => setChangePasswordOpen(false)} />
      )}
      {adminOpen && (
        <AdminModal onClose={() => setAdminOpen(false)} addToast={addToast} />
      )}
    </div>

      <Toasts toasts={toasts} onRemove={removeToast} />
    </>
  )
}
