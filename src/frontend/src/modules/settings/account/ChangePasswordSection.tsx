import { useRef, useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Trans, useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { api, ApiError } from '../../../api.js'
import Modal from '../../../components/Modal'
import KeyIcon from '../../../icons/KeyIcon'

const DAV_CREDENTIALS_KEY = ['davCredentials']

interface ChangePasswordDialogProps {
  warnsAboutSync: boolean
  onClose: () => void
  onDone: () => void
}

function ChangePasswordDialog({ warnsAboutSync, onClose, onDone }: ChangePasswordDialogProps) {
  const { t } = useTranslation('settings')
  const oldPasswordRef = useRef<HTMLInputElement>(null)
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
      onDone()
    } catch {
      setError(t('account.currentPasswordIncorrect'))
      setLoading(false)
    }
  }

  return (
    <Modal title={t('account.changePassword')} icon={<KeyIcon />} onClose={onClose} busy={loading}
      initialFocusRef={oldPasswordRef} onSubmit={e => void handleSubmit(e)}>
      {warnsAboutSync && (
        <p className="alert alert-warn">
          <Trans i18nKey="account.passwordResetsSync" ns="settings"
            components={{ strong: <strong />, syncLink: <Link to="/settings/sync" /> }} />
        </p>
      )}
      {error && <div className="alert alert-error" role="alert">{error}</div>}
      <div className="field">
        <label htmlFor="account-old-password">{t('account.currentPassword')}</label>
        <input
          ref={oldPasswordRef}
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
    </Modal>
  )
}

interface ChangePasswordSectionProps {
  onDone?: () => void
}

export default function ChangePasswordSection({ onDone }: ChangePasswordSectionProps) {
  const { t } = useTranslation('settings')
  const queryClient = useQueryClient()
  const [open, setOpen] = useState(false)
  const { data, error } = useQuery({
    queryKey: DAV_CREDENTIALS_KEY,
    queryFn: () => api.getDavCredentials(),
  })
  // Unknown reads as syncing: an unneeded warning costs a line, a missing one locks the account.
  // Only a 404 is a real no, the deployment publishing no sync address at all.
  const noSyncAddress = error instanceof ApiError && error.status === 404
  const warnsAboutSync = data ? data.configured : !noSyncAddress

  function handleDone() {
    setOpen(false)
    void queryClient.invalidateQueries({ queryKey: DAV_CREDENTIALS_KEY })
    onDone?.()
  }

  return (
    <div className="field-h is-setting">
      <span className="setting-label">
        <span>{t('account.passwordLabel')}</span>
        <span className="setting-hint">
          {warnsAboutSync ? t('account.passwordHintSync') : t('account.passwordHint')}
        </span>
      </span>
      <button type="button" className="btn" aria-label={t('account.changePassword')}
        onClick={() => setOpen(true)}>{t('account.changePasswordOpen')}</button>
      {open && (
        <ChangePasswordDialog warnsAboutSync={warnsAboutSync} onClose={() => setOpen(false)}
          onDone={handleDone} />
      )}
    </div>
  )
}
