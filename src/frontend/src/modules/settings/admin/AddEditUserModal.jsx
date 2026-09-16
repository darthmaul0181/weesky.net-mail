import { useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api } from '../../../api.js'
import ModalOverlay from '../../../components/ModalOverlay'
import PencilIcon from '../../../icons/PencilIcon.jsx'
import PersonPlusIcon from '../../../icons/PersonPlusIcon.jsx'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'

function formatRelative(isoString, t) {
  const diff = Date.now() - new Date(isoString).getTime()
  const mins = Math.floor(diff / 60000)
  if (mins < 1) return t('accounts.justNow')
  if (mins < 60) return t('accounts.minutesAgo', { value: mins })
  const hrs = Math.floor(mins / 60)
  if (hrs < 24) return t('accounts.hoursAgo', { value: hrs })
  const days = Math.floor(hrs / 24)
  return t('accounts.daysAgo', { value: days })
}

export function AddEditUserModal({ user, domains, onSave, onClose }) {
  const { t } = useTranslation('admin')
  const uid = useId()
  const fieldId = name => `${uid}-user-${name}`
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
    if (!isEdit && !password) { setError(t('accounts.passwordRequired')); return }
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
      setError(apiErrorMessage(err, t('errorOccurred')))
    } finally {
      setLoading(false)
    }
  }

  return (
    <ModalOverlay onClose={onClose}>
      <div className="modal" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">{isEdit ? <PencilIcon /> : <PersonPlusIcon />}{t(isEdit ? 'accounts.editTitle' : 'accounts.addTitle')}</span>
          <button className="modal-close" aria-label={t('actions.close', { ns: 'common' })} onClick={onClose}>✕</button>
        </div>
        {isEdit && user.lastLogins?.length > 0 && (
          <div className="last-login-info">
            <span className="last-login-label">{t('accounts.lastConnections')}</span>
            <div className="last-login-row">
              {user.lastLogins.map((l, i) => (
                <span key={l.service} className="last-login-entry">
                  {i > 0 && <span className="last-login-sep">·</span>}
                  <span className="last-login-service">{l.service.toUpperCase()}</span>
                  <span className="last-login-time">{formatRelative(l.at, t)}</span>
                </span>
              ))}
            </div>
          </div>
        )}
        <form onSubmit={handleSubmit}>
          {error && <div className="alert alert-error" role="alert">{error}</div>}
          <div className="field-h">
            <label htmlFor={fieldId('username')}>{t('accounts.userName')}</label>
            <input id={fieldId('username')} type="text" value={userName} onChange={e => setUserName(e.target.value)}
              disabled={isEdit} required />
          </div>
          <div className="field-h">
            <label htmlFor={fieldId('domain')}>{t('accounts.domain')}</label>
            <select id={fieldId('domain')} value={domainId} onChange={e => setDomainId(e.target.value)} disabled={isEdit}>
              {domains.map(d => <option key={d.id} value={d.id}>{d.name}</option>)}
            </select>
          </div>
          <div className="field-h">
            <label htmlFor={fieldId('password')}>{t('accounts.password')}</label>
            <input id={fieldId('password')} type="password" value={password} onChange={e => setPassword(e.target.value)}
              placeholder={isEdit ? t('accounts.leaveBlank') : ''} />
          </div>
          <div className="field-h">
            <label htmlFor={fieldId('fullname')}>{t('accounts.fullName')}</label>
            <input id={fieldId('fullname')} type="text" value={fullName} onChange={e => setFullName(e.target.value)} />
          </div>
          <div className="field-h">
            <label htmlFor={fieldId('quota')}>{t('accounts.quota')}</label>
            <div className="quota-field">
              <input type="range" min={1} max={10240} value={quotaMb} aria-label={t('accounts.quota')}
                onChange={e => handleQuotaSlider(e.target.value)} />
              <input id={fieldId('quota')} type="number" min={1} max={10240} value={quotaMb}
                onChange={e => handleQuotaSlider(e.target.value)} />
              <span className="quota-field-unit">{t('accounts.quotaUnit')}</span>
            </div>
          </div>
          <div className="field-h">
            <label htmlFor={fieldId('active')}>{t('accounts.active')}</label>
            <label className="toggle-switch">
              <input id={fieldId('active')} type="checkbox" checked={active} onChange={e => setActive(e.target.checked)} />
              <span className="toggle-track" />
            </label>
          </div>
          <div className="field-h">
            <label htmlFor={fieldId('admin')}>{t('accounts.administrator')}</label>
            <label className="toggle-switch">
              <input id={fieldId('admin')} type="checkbox" checked={admin} onChange={e => setAdmin(e.target.checked)} />
              <span className="toggle-track" />
            </label>
          </div>
          <button className="btn btn-primary" type="submit"
            disabled={loading || !userName.trim() || (!isEdit && !password.trim())}
            style={{ marginTop: '8px' }}>
            {loading
              ? <span className="spinner" />
              : (isEdit ? t('actions.saveChanges', { ns: 'common' }) : t('accounts.create'))}
          </button>
        </form>
      </div>
    </ModalOverlay>
  )
}

export default AddEditUserModal
