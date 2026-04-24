import { useState, useEffect, useCallback, useRef } from 'react'
import { api, clearToken } from '../api.js'
import logoCircle from '../assets/logo_circle.jpg'
import weeskyLogo from '../assets/weesky_net.png'

function useToasts() {
  const [toasts, setToasts] = useState([])

  const addToast = useCallback((message, type = 'success') => {
    const id = Date.now()
    setToasts(prev => [...prev, { id, message, type }])
    setTimeout(() => setToasts(prev => prev.filter(t => t.id !== id)), 3000)
  }, [])

  return { toasts, addToast }
}

function Toasts({ toasts }) {
  if (!toasts.length) return null
  return (
    <div className="toast-container">
      {toasts.map(t => (
        <div key={t.id} className={`toast toast-${t.type}`}>{t.message}</div>
      ))}
    </div>
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

function ChangePasswordModal({ onClose }) {
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
          <span className="modal-title">Change password</span>
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

function QuotaBlock({ quota }) {
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

function AccountPanel({ initials, fullName, primaryEmail, subDomains, quota, onLogout, onChangePassword }) {
  const [open, setOpen] = useState(false)
  const panelRef = useRef(null)

  useEffect(() => {
    function handleClickOutside(e) {
      if (panelRef.current && !panelRef.current.contains(e.target)) {
        setOpen(false)
      }
    }
    if (open) document.addEventListener('mousedown', handleClickOutside)
    return () => document.removeEventListener('mousedown', handleClickOutside)
  }, [open])

  return (
    <>
      <button className="avatar-btn" onClick={() => setOpen(o => !o)} title={primaryEmail}>
        {initials}
      </button>

      {open && (
        <>
          <div className="panel-overlay" onClick={() => setOpen(false)} />
          <div className="account-panel" ref={panelRef}>
            <div className="panel-fullname">{fullName || primaryEmail}</div>
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

            <div className="panel-actions">
              <button className="panel-link" onClick={() => { setOpen(false); onChangePassword() }}>
                <svg xmlns="http://www.w3.org/2000/svg" width="15" height="15" viewBox="0 0 24 24"
                  fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                  <rect x="3" y="11" width="18" height="11" rx="2" ry="2" />
                  <path d="M7 11V7a5 5 0 0 1 10 0v4" />
                </svg>
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

export default function AliasesPage({ onLogout }) {
  const { toasts, addToast } = useToasts()

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

  const [newName, setNewName] = useState('')
  const [addError, setAddError] = useState(null)
  const [adding, setAdding] = useState(false)

  const [deletingKey, setDeletingKey] = useState(null)
  const [highlightedKey, setHighlightedKey] = useState(null)
  const [changePasswordOpen, setChangePasswordOpen] = useState(false)

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

  async function handleAdd(e) {
    e.preventDefault()
    setAddError(null)
    setAdding(true)
    try {
      await api.createAlias(newName, selectedDomain)
      const key = `${newName}@${selectedDomain}`
      addToast(`${key} added`)
      setNewName('')
      await fetchAliases()
      setHighlightedKey(key)
    } catch (err) {
      setAddError(err.message || 'Failed to create alias.')
    } finally {
      setAdding(false)
    }
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
          className="search-input"
          type="search"
          placeholder="Search…"
          value={search}
          onChange={e => setSearch(e.target.value)}
        />
      </div>

      {listError && <div className="alert alert-error">{listError}</div>}

      {loadingList ? (
        <div className="loading-center">
          <span className="spinner" />
        </div>
      ) : visibleAliases.length === 0 ? (
        <div className="alias-empty-grid">No aliases for this domain.</div>
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

      <div className="add-form">
        <div className="add-form-title">Add an alias</div>

        {addError && <div className="alert alert-error">{addError}</div>}

        <form onSubmit={handleAdd}>
          <div className="add-form-row">
            <div className="field">
              <input
                id="alias-name"
                type="text"
                placeholder="alias"
                value={newName}
                onChange={e => setNewName(e.target.value)}
                required
              />
            </div>
            <button className="btn btn-add" type="submit" disabled={adding || !selectedDomain}>
              {adding ? <span className="spinner" /> : 'Add'}
            </button>
          </div>
        </form>
      </div>

      {changePasswordOpen && (
        <ChangePasswordModal onClose={() => setChangePasswordOpen(false)} />
      )}
    </div>

      <Toasts toasts={toasts} />
    </>
  )
}
