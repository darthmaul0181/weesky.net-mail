import { useState, type FormEvent } from 'react'
import { api } from '../../../api.js'

interface ChangePasswordSectionProps {
  onDone?: () => void
}

export default function ChangePasswordSection({ onDone }: ChangePasswordSectionProps) {
  const [oldPassword, setOldPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()

    if (!oldPassword) {
      setError('Current password is required.')
      return
    }
    if (newPassword.length < 10) {
      setError('New password must be at least 10 characters.')
      return
    }
    if (newPassword !== confirm) {
      setError('Passwords do not match.')
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
      setError('Current password is incorrect.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <form className="change-password-form" onSubmit={handleSubmit}>
      {error && <div className="alert alert-error">{error}</div>}
      <div className="field">
        <label htmlFor="account-old-password">Current password</label>
        <input
          id="account-old-password"
          type="password"
          value={oldPassword}
          onChange={e => setOldPassword(e.target.value)}
        />
      </div>
      <div className="field">
        <label htmlFor="account-new-password">New password</label>
        <input
          id="account-new-password"
          type="password"
          value={newPassword}
          onChange={e => setNewPassword(e.target.value)}
        />
      </div>
      <div className="field">
        <label htmlFor="account-confirm-password">Confirm new password</label>
        <input
          id="account-confirm-password"
          type="password"
          value={confirm}
          onChange={e => setConfirm(e.target.value)}
        />
      </div>
      <button className="btn btn-primary" type="submit" disabled={loading}>
        {loading ? <span className="spinner" /> : 'Change password'}
      </button>
    </form>
  )
}
