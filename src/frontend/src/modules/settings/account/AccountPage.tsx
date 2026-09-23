import { useEffect, useState, type JSX } from 'react'
import { useTranslation } from 'react-i18next'
import { useAuth } from '../../../contexts/AuthContext'
import { api } from '../../../api.js'
import QuotaBlock from '../../../components/QuotaBlock'
import { useToasts } from '../../../hooks/useToasts'
import Toasts from '../../../components/Toasts'
import PencilIcon from '../../../icons/PencilIcon'
import UserIcon from '../../../icons/UserIcon'
import ChangePasswordSection from './ChangePasswordSection'
import type { Quota } from '../../../types/account'

function CheckIcon(): JSX.Element {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width="13" height="13" viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round"
      aria-hidden="true" focusable="false">
      <polyline points="20 6 9 17 4 12" />
    </svg>
  )
}

function XIcon(): JSX.Element {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width="13" height="13" viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round"
      aria-hidden="true" focusable="false">
      <line x1="18" y1="6" x2="6" y2="18" />
      <line x1="6" y1="6" x2="18" y2="18" />
    </svg>
  )
}

export default function AccountPage() {
  const { identity, account, refreshAccount, capabilities } = useAuth()
  const { t } = useTranslation('settings')
  const canEditProfile = capabilities?.profileEditing !== false
  const canChangePassword = capabilities?.passwordChange !== false
  const { toasts, addToast, removeToast, pauseToast, resumeToast } = useToasts()
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
      addToast(t('account.nameUpdated'))
    } catch {
      addToast(t('account.nameUpdateFailed'), 'error')
    } finally {
      setSaving(false)
    }
  }

  if (!identity) return null

  return (
    <div className="settings-page account-page">
      <div className="settings-page-header">
        <h1 className="settings-page-title"><UserIcon size={17} />{t('nav.account')}</h1>
      </div>

      <section className="account-section">
        <h2>{t('account.identity')}</h2>
        {editingName ? (
          <div className="panel-fullname-edit">
            <input
              className="panel-fullname-input"
              value={nameValue}
              onChange={e => setNameValue(e.target.value)}
              onKeyDown={e => { if (e.key === 'Enter') void saveName() }}
              maxLength={255}
              disabled={saving}
              // eslint-disable-next-line jsx-a11y/no-autofocus -- an inline rename field, not a dialog Modal: opening it autofocuses the box the way SearchBar's does
              autoFocus
            />
            <button
              className="panel-fullname-btn panel-fullname-confirm"
              onClick={() => void saveName()}
              disabled={saving}
              aria-label={t('actions.save', { ns: 'common' })}
              title={t('actions.save', { ns: 'common' })}
            >
              {saving ? <span className="spinner spinner-sm" /> : <CheckIcon />}
            </button>
            <button
              className="panel-fullname-btn panel-fullname-cancel"
              onClick={cancelEdit}
              disabled={saving}
              aria-label={t('actions.cancel', { ns: 'common' })}
              title={t('actions.cancel', { ns: 'common' })}
            >
              <XIcon />
            </button>
          </div>
        ) : (
          <div className="panel-fullname-row">
            <span className="panel-fullname">{identity.displayName}</span>
            {canEditProfile && (
              <button
                className="panel-fullname-pencil"
                onClick={startEdit}
                aria-label={t('account.editName')}
                title={t('account.editName')}
              >
                <PencilIcon />
              </button>
            )}
          </div>
        )}
        <div className="panel-mailbox-row">
          <span className="panel-mailbox-label">{t('account.primaryEmail')}</span>
          <span className="panel-mailbox-sep">&nbsp;:&nbsp;</span>
          <span className="panel-mailbox-value">{identity.email}</span>
        </div>
      </section>

      {identity.subDomains.length > 0 && (
        <section className="account-section">
          <h2>{t('account.otherDomains')}</h2>
          <ul className="account-domains">
            {identity.subDomains.map(d => <li key={d.id}>{d.name}</li>)}
          </ul>
        </section>
      )}

      <section className="account-section">
        <h2>{t('account.storage')}</h2>
        <QuotaBlock quota={quota} />
      </section>

      {canChangePassword && (
        <section className="account-section">
          <h2>{t('account.password')}</h2>
          <ChangePasswordSection onDone={() => addToast(t('account.passwordChanged'))} />
        </section>
      )}

      <Toasts toasts={toasts} onRemove={removeToast} onPause={pauseToast} onResume={resumeToast} />
    </div>
  )
}
