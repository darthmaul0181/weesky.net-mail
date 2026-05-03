import { useState, useEffect, useCallback, useRef } from 'react'
import { api, clearToken } from '../api.js'
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

function Toasts({ toasts, onRemove }) {
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

function AccountPanel({ initials, fullName, primaryEmail, subDomains, quota, onLogout, onChangePassword, alphaMode, onAlphaModeChange, onFullNameChange }) {
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
    </div>

      <Toasts toasts={toasts} onRemove={removeToast} />
    </>
  )
}
