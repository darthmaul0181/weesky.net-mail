import { useState, useEffect, useCallback, useRef } from 'react'
import { Button, Alert, Spin, Form, Input } from 'antd'
import {
  LockOutlined, DeleteOutlined, EditOutlined, CheckOutlined, CloseOutlined,
  SafetyCertificateOutlined, UserAddOutlined, GlobalOutlined, LogoutOutlined,
} from '@ant-design/icons'
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
                  {saving ? <Spin size="small" /> : <CheckOutlined />}
                </button>
                <button className="panel-fullname-btn panel-fullname-cancel" onClick={handleCancel} disabled={saving} title="Cancel">
                  <CloseOutlined />
                </button>
              </div>
            ) : (
              <div className="panel-fullname-row">
                <span className="panel-fullname">{fullName || primaryEmail}</span>
                <button className="panel-fullname-pencil" onClick={handleEdit} title="Edit name">
                  <EditOutlined />
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
                  <span aria-hidden="true"><SafetyCertificateOutlined /></span>
                  Administration
                </button>
              )}
              <button className="panel-link" onClick={() => { setOpen(false); onChangePassword() }}>
                <span aria-hidden="true"><LockOutlined /></span>
                Change password
              </button>
              <button className="panel-link panel-link-danger" onClick={onLogout}>
                <span aria-hidden="true"><LogoutOutlined /></span>
                Sign out
              </button>
            </div>
          </div>
        </>
      )}
    </>
  )
}

export function ChangePasswordModal({ onClose }) {
  const [error, setError] = useState(null)
  const [success, setSuccess] = useState(false)
  const [loading, setLoading] = useState(false)
  const [form] = Form.useForm()

  async function handleSubmit(values) {
    if (values.newPassword !== values.confirm) {
      setError('Passwords do not match.')
      return
    }
    setError(null)
    setLoading(true)
    try {
      await api.changePassword(values.oldPassword, values.newPassword)
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
          <span className="modal-title"><LockOutlined /> Change password</span>
          <button className="modal-close" onClick={onClose}>✕</button>
        </div>

        {success ? (
          <div className="modal-success">Password changed successfully.</div>
        ) : (
          <Form form={form} layout="vertical" onFinish={handleSubmit}>
            {error && <Alert type="error" message={error} showIcon style={{ marginBottom: 16 }} />}
            <Form.Item label="Current password" name="oldPassword" style={{ marginBottom: 12 }}>
              <Input.Password autoFocus />
            </Form.Item>
            <Form.Item label="New password" name="newPassword" style={{ marginBottom: 12 }}>
              <Input.Password />
            </Form.Item>
            <Form.Item label="Confirm new password" name="confirm" style={{ marginBottom: 16 }}>
              <Input.Password />
            </Form.Item>
            <Button type="primary" htmlType="submit" loading={loading} block>
              Update password
            </Button>
          </Form>
        )}
      </div>
    </div>
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
          <Button onClick={onClose} disabled={loading}>Cancel</Button>
          <Button type="primary" danger onClick={onConfirm} loading={loading}>
            Delete
          </Button>
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
          <span className="modal-title">
            {isEdit ? <><EditOutlined /> Edit account</> : <><UserAddOutlined /> Add account</>}
          </span>
          <button className="modal-close" onClick={onClose}>✕</button>
        </div>
        <form onSubmit={handleSubmit}>
          {error && <Alert type="error" message={error} showIcon style={{ marginBottom: 16 }} />}
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
          <Button type="primary" htmlType="submit"
            loading={loading}
            disabled={!userName.trim() || (!isEdit && !password.trim())}
            block
            style={{ marginTop: '8px' }}>
            {isEdit ? 'Save changes' : 'Create account'}
          </Button>
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
          <span className="modal-title">{isEdit ? 'Edit domain' : 'Add domain'}</span>
          <button className="modal-close" onClick={onClose}>✕</button>
        </div>
        <form onSubmit={handleSubmit}>
          {error && <Alert type="error" message={error} showIcon style={{ marginBottom: 16 }} />}
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
          <Button type="primary" htmlType="submit"
            loading={loading}
            disabled={!id.trim() || !nameValid}
            block>
            {isEdit ? 'Save changes' : 'Create domain'}
          </Button>
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
        } catch { /* quota unavailable */ }
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

  if (loading) return <div style={{ textAlign: 'center', padding: '32px' }}><Spin /></div>

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
        <Button type="primary" icon={<UserAddOutlined />} style={{ marginLeft: 'auto' }}
          onClick={() => setShowAddModal(true)}>
          Add
        </Button>
      </div>
      <div className="admin-list">
        {visibleUsers.map(u => (
          <div key={u.id} className="admin-list-item">
            <span className="admin-list-item-email">{u.userName}@{u.domainName}</span>
            <span className="admin-list-item-name" style={{ paddingLeft: '30px' }}>{u.fullName}</span>
            <div className="admin-list-item-quota"><QuotaMini quota={quotas[u.id]} /></div>
            <div className="admin-list-item-actions">
              <Button size="small" title="Edit" icon={<EditOutlined />} onClick={() => setUserToEdit(u)} />
              <Button size="small" title="Delete" danger icon={<DeleteOutlined />} onClick={() => setUserToDelete(u)} />
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

  if (loading) return <div style={{ textAlign: 'center', padding: '32px' }}><Spin /></div>

  return (
    <div>
      <div className="admin-list-header">
        <span className="admin-list-title">Domains ({domains.length})</span>
        <Button type="primary" icon={<GlobalOutlined />} onClick={() => setShowAddModal(true)}>
          Add
        </Button>
      </div>
      <div className="admin-list">
        {domains.map(d => (
          <div key={d.id} className="admin-list-item">
            <span className="admin-list-item-email" style={{ minWidth: '60px' }}>{d.id}</span>
            <span className="admin-list-item-name">{d.name}</span>
            <div className="admin-list-item-actions">
              <Button size="small" title="Edit" icon={<EditOutlined />} onClick={() => setDomainToEdit(d)} />
              <Button size="small" title="Delete" danger icon={<DeleteOutlined />} onClick={() => setDomainToDelete(d)} />
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

export function OwnershipTab({ addToast }) {
  const [ownerships, setOwnerships] = useState([])
  const [users, setUsers] = useState([])
  const [loading, setLoading] = useState(true)
  const [editingDomainId, setEditingDomainId] = useState(null)
  const [searchQuery, setSearchQuery] = useState('')
  const [saving, setSaving] = useState(false)
  const editRef = useRef(null)

  async function load() {
    setLoading(true)
    try {
      const [o, u] = await Promise.all([api.adminGetOwnerships(), api.adminGetUsers()])
      setOwnerships(o ?? [])
      setUsers(u ?? [])
    } catch {
      addToast('Failed to load ownerships', 'error')
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
      await api.adminSetOwnership(domainId, userId)
      setEditingDomainId(null)
      setSearchQuery('')
      load()
    } catch (err) {
      addToast(err.message || 'Failed to set owner', 'error')
    } finally {
      setSaving(false)
    }
  }

  async function handleUnlink(domainId) {
    setSaving(true)
    try {
      await api.adminDeleteOwnership(domainId)
      setEditingDomainId(null)
      load()
    } catch (err) {
      addToast(err.message || 'Failed to remove owner', 'error')
    } finally {
      setSaving(false)
    }
  }

  const term = searchQuery.trim().toLowerCase()
  const filteredUsers = term
    ? users.filter(u => {
        const email = `${u.userName}@${u.domainName}`.toLowerCase()
        const name = (u.fullName ?? '').toLowerCase()
        return email.includes(term) || name.includes(term)
      })
    : []

  if (loading) return <div style={{ textAlign: 'center', padding: '32px' }}><Spin /></div>

  return (
    <div>
      <div className="admin-list-header">
        <span className="admin-list-title">Extra domains ({ownerships.length})</span>
      </div>
      <div className="admin-list">
        {ownerships.length === 0 && (
          <div style={{ padding: '24px', textAlign: 'center', color: 'var(--text-muted)', fontSize: '13px' }}>
            No extra domains
          </div>
        )}
        {ownerships.map(o => (
          <div key={o.domainId} className="admin-list-item">
            <span className="admin-list-item-email">{o.domainName} <span style={{ color: 'var(--text-muted)', fontWeight: 400 }}>({o.domainId})</span></span>
            {editingDomainId === o.domainId ? (
              <div ref={editRef} style={{ flex: 1, position: 'relative', display: 'flex', alignItems: 'center', gap: '6px', paddingLeft: '30px' }}>
                <div style={{ position: 'relative', flex: 1 }}>
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
                {o.ownerId != null && (
                  <Button
                    size="small"
                    title="Remove owner"
                    danger
                    icon={<DeleteOutlined />}
                    disabled={saving}
                    onMouseDown={e => { e.preventDefault(); handleUnlink(o.domainId) }}
                  />
                )}
              </div>
            ) : (
              <span className="admin-list-item-name" style={{ flex: 1, paddingLeft: '30px' }}>
                {o.ownerEmail ?? '—'}
              </span>
            )}
            <div className="admin-list-item-actions">
              {editingDomainId !== o.domainId && (
                <Button size="small" title="Edit owner" icon={<EditOutlined />} onClick={() => {
                  setEditingDomainId(o.domainId)
                  setSearchQuery('')
                }} />
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
          <span className="modal-title"><SafetyCertificateOutlined /> Administration</span>
          <button className="modal-close" onClick={onClose}>✕</button>
        </div>
        <div className="admin-modal-body">
          <nav className="admin-tab-bar">
            <button className={`admin-tab${activeTab === 'accounts' ? ' is-active' : ''}`}
              onClick={() => setActiveTab('accounts')}>Accounts</button>
            <button className={`admin-tab${activeTab === 'domains' ? ' is-active' : ''}`}
              onClick={() => setActiveTab('domains')}>Domains</button>
            <button className={`admin-tab${activeTab === 'ownerships' ? ' is-active' : ''}`}
              onClick={() => setActiveTab('ownerships')}>Ownerships</button>
          </nav>
          <div className="admin-tab-content">
            {activeTab === 'accounts' && <AccountsTab addToast={addToast} />}
            {activeTab === 'domains' && <DomainsTab addToast={addToast} />}
            {activeTab === 'ownerships' && <OwnershipTab addToast={addToast} />}
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
          <Button
            type="primary"
            onClick={handleAdd}
            loading={adding}
            disabled={!selectedDomain || !search.trim() || search.length > 30}
          >
            Create alias
          </Button>
        </div>

        {listError && <Alert type="error" message={listError} showIcon style={{ marginBottom: 16 }} />}

        {loadingList ? (
          <div className="loading-center">
            <Spin />
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
                            {deletingKey === key ? <Spin size="small" /> : <DeleteOutlined />}
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
                    {deletingKey === key ? <Spin size="small" /> : <DeleteOutlined />}
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
