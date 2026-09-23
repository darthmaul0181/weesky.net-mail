import { useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import Modal from '../../../components/Modal'
import PencilIcon from '../../../icons/PencilIcon'
import PersonPlusIcon from '../../../icons/PersonPlusIcon'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import type { AdminDomain, AdminUser, AdminUserPayload } from './adminTypes'
import { useCreateAdminUser, useUpdateAdminUser } from './useAdminLists'

function formatRelative(isoString: string, t: TFunction<'admin'>) {
  const diff = Date.now() - new Date(isoString).getTime()
  const mins = Math.floor(diff / 60000)
  if (mins < 1) return t('accounts.justNow')
  if (mins < 60) return t('accounts.minutesAgo', { value: mins })
  const hrs = Math.floor(mins / 60)
  if (hrs < 24) return t('accounts.hoursAgo', { value: hrs })
  const days = Math.floor(hrs / 24)
  return t('accounts.daysAgo', { value: days })
}

interface Props {
  /** The account being edited; absent, the dialog creates one. */
  user?: AdminUser | null
  domains: AdminDomain[]
  onSave: () => void
  onClose: () => void
}

export function AddEditUserModal({ user, domains, onSave, onClose }: Props) {
  const { t } = useTranslation('admin')
  const uid = useId()
  const fieldId = (name: string) => `${uid}-user-${name}`
  const [userName, setUserName] = useState(user?.userName ?? '')
  const [domainId, setDomainId] = useState(user?.domainId ?? domains[0]?.id ?? '')
  const [password, setPassword] = useState('')
  const [fullName, setFullName] = useState(user?.fullName ?? '')
  const [quotaMb, setQuotaMb] = useState(user?.quotaMb ?? 1024)
  const [active, setActive] = useState(user?.active ?? true)
  const [admin, setAdmin] = useState(user?.admin ?? false)
  const [error, setError] = useState<string | null>(null)
  const createUser = useCreateAdminUser()
  const updateUser = useUpdateAdminUser()
  const loading = createUser.isPending || updateUser.isPending
  const isEdit = !!user

  function handleQuotaSlider(v: string) {
    const n = Math.max(1, Math.min(10240, Number(v)))
    setQuotaMb(n)
  }

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (!isEdit && !password) { setError(t('accounts.passwordRequired')); return }
    setError(null)
    try {
      const payload: AdminUserPayload = {
        userName,
        domainId,
        password: password || null,
        fullName,
        quotaMb,
        active,
        admin,
      }
      if (user) {
        await updateUser.mutateAsync({ id: user.id, user: payload })
      } else {
        await createUser.mutateAsync(payload)
      }
      onSave()
    } catch (err) {
      setError(apiErrorMessage(err, t('errorOccurred')))
    }
  }

  return (
    <Modal icon={isEdit ? <PencilIcon /> : <PersonPlusIcon />}
      title={t(isEdit ? 'accounts.editTitle' : 'accounts.addTitle')}
      onClose={onClose} onSubmit={e => void handleSubmit(e)} busy={loading}>
      {isEdit && user.lastLogins.length > 0 && (
        <div className="last-login-info">
          <span className="last-login-label">{t('accounts.lastConnections')}</span>
          <div className="last-login-row">
            {user.lastLogins.map((l, i) => (
              <span key={l.service} className="last-login-entry">
                {i > 0 && <span className="last-login-sep">·</span>}
                <span className="last-login-service">{l.service.toUpperCase()}</span>
                {l.at && <span className="last-login-time">{formatRelative(l.at, t)}</span>}
              </span>
            ))}
          </div>
        </div>
      )}
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
          <input id={fieldId('active')} type="checkbox" checked={active} onChange={e => setActive(e.target.checked)}
            aria-label={t('accounts.active')} />
          <span className="toggle-track" />
        </label>
      </div>
      <div className="field-h">
        <label htmlFor={fieldId('admin')}>{t('accounts.administrator')}</label>
        <label className="toggle-switch">
          <input id={fieldId('admin')} type="checkbox" checked={admin} onChange={e => setAdmin(e.target.checked)}
            aria-label={t('accounts.administrator')} />
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
    </Modal>
  )
}

export default AddEditUserModal
