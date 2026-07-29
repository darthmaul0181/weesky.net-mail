import { useEffect, useState, type JSX } from 'react'
import { useAuth } from '../../../contexts/AuthContext'
import { api } from '../../../api.js'
import QuotaBlock from '../../../components/QuotaBlock.jsx'
import { useToasts } from '../../../hooks/useToasts.js'
import Toasts from '../../../components/Toasts.jsx'
import PencilIcon from '../../../icons/PencilIcon.jsx'
import UserIcon from '../../../icons/UserIcon'
import ChangePasswordSection from './ChangePasswordSection'

interface Quota {
  storageBytesUsed: number
  storageBytesLimit: number
}

function CheckIcon(): JSX.Element {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width="13" height="13" viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="20 6 9 17 4 12" />
    </svg>
  )
}

function XIcon(): JSX.Element {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width="13" height="13" viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
      <line x1="18" y1="6" x2="6" y2="18" />
      <line x1="6" y1="6" x2="18" y2="18" />
    </svg>
  )
}

export default function AccountPage() {
  const { identity, account, refreshAccount } = useAuth()
  const { toasts, addToast, removeToast } = useToasts()
  const [quota, setQuota] = useState<Quota | null>(null)
  const [editingName, setEditingName] = useState(false)
  const [nameValue, setNameValue] = useState('')
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    api.getQuota().then(setQuota).catch(() => {})
  }, [])

  function startEdit() {
    setNameValue(account?.fullName ?? '')
    setEditingName(true)
  }

  function cancelEdit() {
    setEditingName(false)
  }

  async function saveName() {
    setSaving(true)
    try {
      await api.changeFullName(nameValue.trim())
      await refreshAccount()
      setEditingName(false)
      addToast('Name updated')
    } catch {
      addToast('Failed to update name', 'error')
    } finally {
      setSaving(false)
    }
  }

  if (!identity) return null

  return (
    <div className="settings-page account-page">
      <div className="settings-page-header">
        <h1 className="settings-page-title"><UserIcon size={17} />Account</h1>
      </div>

      <section className="account-section">
        <h2>Identity</h2>
        {editingName ? (
          <div className="panel-fullname-edit">
            <input
              className="panel-fullname-input"
              value={nameValue}
              onChange={e => setNameValue(e.target.value)}
              onKeyDown={e => { if (e.key === 'Enter') saveName() }}
              maxLength={255}
              disabled={saving}
              autoFocus
            />
            <button
              className="panel-fullname-btn panel-fullname-confirm"
              onClick={saveName}
              disabled={saving}
              aria-label="Save"
              title="Save"
            >
              {saving ? <span className="spinner spinner-sm" /> : <CheckIcon />}
            </button>
            <button
              className="panel-fullname-btn panel-fullname-cancel"
              onClick={cancelEdit}
              disabled={saving}
              aria-label="Cancel"
              title="Cancel"
            >
              <XIcon />
            </button>
          </div>
        ) : (
          <div className="panel-fullname-row">
            <span className="panel-fullname">{identity.displayName}</span>
            <button
              className="panel-fullname-pencil"
              onClick={startEdit}
              aria-label="Edit name"
              title="Edit name"
            >
              <PencilIcon />
            </button>
          </div>
        )}
        <div className="panel-mailbox-row">
          <span className="panel-mailbox-label">Primary email</span>
          <span className="panel-mailbox-sep">&nbsp;:&nbsp;</span>
          <span className="panel-mailbox-value">{identity.email}</span>
        </div>
      </section>

      {identity.subDomains.length > 0 && (
        <section className="account-section">
          <h2>Other domains</h2>
          <ul className="account-domains">
            {identity.subDomains.map(d => <li key={d.id}>{d.name}</li>)}
          </ul>
        </section>
      )}

      <section className="account-section">
        <h2>Storage</h2>
        <QuotaBlock quota={quota} />
      </section>

      <section className="account-section">
        <h2>Password</h2>
        <ChangePasswordSection onDone={() => addToast('Password changed')} />
      </section>

      <Toasts toasts={toasts} onRemove={removeToast} />
    </div>
  )
}
