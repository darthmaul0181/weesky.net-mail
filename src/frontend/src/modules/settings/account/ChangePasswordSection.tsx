import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { api } from '../../../api.js'

interface ChangePasswordSectionProps {
  onDone?: () => void
}

export default function ChangePasswordSection({ onDone }: ChangePasswordSectionProps) {
  const { t } = useTranslation('settings')
  const [oldPassword, setOldPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()

    if (!oldPassword) {
      setError(t('account.currentPasswordRequired'))
      return
    }
    if (newPassword.length < 10) {
      setError(t('account.newPasswordTooShort'))
      return
    }
    if (newPassword !== confirm) {
      setError(t('account.passwordsDoNotMatch'))
      return
    }

    setError(null)
    setLoading(true)
    try {
      await api.changePassword(oldPassword, newPassword)
      setOldPassword('')
      setNewPassword('')
      setConfirm('')
      onDone?.()
    } catch {
      setError(t('account.currentPasswordIncorrect'))
    } finally {
      setLoading(false)
    }
  }

  return (
    <form className="change-password-form" onSubmit={handleSubmit}>
      <p className="setting-hint">{t('account.passwordResetsSync')}</p>
      {error && <div className="alert alert-error">{error}</div>}
      <div className="field">
        <label htmlFor="account-old-password">{t('account.currentPassword')}</label>
        <input
          id="account-old-password"
          type="password"
          value={oldPassword}
          onChange={e => setOldPassword(e.target.value)}
        />
      </div>
      <div className="field">
        <label htmlFor="account-new-password">{t('account.newPassword')}</label>
        <input
          id="account-new-password"
          type="password"
          value={newPassword}
          onChange={e => setNewPassword(e.target.value)}
        />
      </div>
      <div className="field">
        <label htmlFor="account-confirm-password">{t('account.confirmNewPassword')}</label>
        <input
          id="account-confirm-password"
          type="password"
          value={confirm}
          onChange={e => setConfirm(e.target.value)}
        />
      </div>
      <button className="btn btn-primary" type="submit" disabled={loading}>
        {loading ? <span className="spinner" /> : t('account.changePassword')}
      </button>
    </form>
  )
}
