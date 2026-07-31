import { useState, type FormEvent } from 'react'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal.jsx'
import LoadingBlock from '../../../components/LoadingBlock'
import Toasts from '../../../components/Toasts.jsx'
import { useAuth } from '../../../contexts/AuthContext'
import { useToasts } from '../../../hooks/useToasts.js'
import KeyIcon from '../../../icons/KeyIcon'
import PersonPlusIcon from '../../../icons/PersonPlusIcon.jsx'
import TrashIcon from '../../../icons/TrashIcon.jsx'
import ConnectAccountForm from './ConnectAccountForm'
import {
  errorText, useConnectedAccounts, useDeleteConnectedAccount,
  useUpdateConnectedAccountPassword, type ConnectedAccount,
} from './useConnectedAccounts'

/** An account on our own server has no external domain row to name, so the tile names the
 *  mailbox's own domain — the same kind of thing an external tile shows, spelled the same way. */
function domainOf(email: string): string {
  const at = email.lastIndexOf('@')
  return at === -1 ? email : email.slice(at + 1)
}

function subtitleOf(account: ConnectedAccount): string {
  const parts = [account.email, account.domainName ?? domainOf(account.email)]
  const date = new Date(account.creationDate)
  if (!Number.isNaN(date.getTime())) {
    parts.push(`connected on ${date.toLocaleDateString(undefined, {
      year: 'numeric', month: 'short', day: 'numeric',
    })}`)
  }
  return parts.join(' · ')
}

/** One field, the admin dialog shape: the ✕ is the only way out. */
function ReenterPasswordDialog({ email, pending, error, onSubmit, onClose }: {
  email: string
  pending: boolean
  error: string | null
  onSubmit: (password: string) => void
  onClose: () => void
}) {
  const [password, setPassword] = useState('')

  function submit(event: FormEvent) {
    event.preventDefault()
    onSubmit(password)
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title"><KeyIcon /> Re-enter password</span>
          <button className="modal-close" aria-label="Close" onClick={onClose}>✕</button>
        </div>
        <form onSubmit={submit}>
          {error && <div className="alert alert-error" role="alert">{error}</div>}
          <p className="settings-note">
            Enter the current password of {email} so Weesky can open this mailbox again.
          </p>
          <div className="field-h">
            <label htmlFor="reenter-password">Password</label>
            <input id="reenter-password" type="password" autoComplete="new-password" autoFocus
              value={password} onChange={e => setPassword(e.target.value)} />
          </div>
          <div className="identity-modal-actions">
            <button type="submit" className="btn btn-primary" style={{ width: 'auto' }}
              disabled={pending || password === ''}>
              {pending ? <span className="spinner" /> : 'Save'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}

/**
 * The mailboxes attached to this session. Every mutation invalidates the list under the key
 * AuthContext reads, so the account switcher follows without being told.
 */
export default function ConnectedAccountsPage() {
  const { activeAccountId } = useAuth()
  const { data: accounts, isLoading, isError } = useConnectedAccounts()
  const updatePassword = useUpdateConnectedAccountPassword()
  const disconnect = useDeleteConnectedAccount()
  const { toasts, addToast, removeToast } = useToasts()
  const [connecting, setConnecting] = useState(false)
  const [reentering, setReentering] = useState<ConnectedAccount | null>(null)
  const [deleting, setDeleting] = useState<ConnectedAccount | null>(null)
  const [dialogError, setDialogError] = useState<string | null>(null)

  async function savePassword(password: string) {
    if (!reentering) return
    setDialogError(null)
    try {
      await updatePassword.mutateAsync({ id: reentering.id, password })
      addToast(`${reentering.email} is connected again`)
      setReentering(null)
    } catch (failure) {
      setDialogError(errorText(failure))
    }
  }

  async function confirmDisconnect() {
    if (!deleting) return
    const { id, email } = deleting
    try {
      await disconnect.mutateAsync(id)
      addToast(`${email} was disconnected`)
    } catch (failure) {
      addToast(errorText(failure, 'Could not disconnect this account'), 'error')
    } finally {
      setDeleting(null)
    }
  }

  return (
    <div className="settings-page">
      <div className="settings-page-header">
        <h1 className="settings-page-title"><PersonPlusIcon size={17} />Connected accounts</h1>
      </div>
      <p className="settings-note">
        Read and send mail from other mailboxes without signing out.
      </p>

      {isLoading && <LoadingBlock />}
      {/* Only when there is nothing to show: a failed background refetch must not blank a list
          that is already on screen and still perfectly usable. */}
      {!isLoading && !accounts && <p>Could not load your connected accounts.</p>}
      {!isLoading && accounts && (
        <div className="connected-accounts">
          {connecting
            ? (
              <ConnectAccountForm
                onCancel={() => setConnecting(false)}
                onConnected={email => { setConnecting(false); addToast(`${email} is connected`) }}
              />
            )
            : (
              <div className="admin-list-header">
                <button className="btn btn-primary" style={{ width: 'auto' }}
                  onClick={() => setConnecting(true)}>
                  <PersonPlusIcon /> Connect an account
                </button>
              </div>
            )}

          {isError && <p className="settings-note">Could not refresh this list — it may be out of date.</p>}
          {accounts.length === 0 && (
            <p className="settings-note">No other mailbox is connected yet.</p>
          )}

          <div className="admin-list connected-account-list">
            {accounts.map(account => (
              <div key={account.id} className="admin-list-item">
                <span className="connected-account-text">
                  <span className="admin-list-item-email">{account.displayName || account.email}</span>
                  <span className="admin-list-item-name">{subtitleOf(account)}</span>
                  {!account.credentialsValid && (
                    <span className="connected-account-warn">
                      Your main password changed — enter this account&apos;s password again.
                    </span>
                  )}
                </span>
                <div className="admin-list-item-actions">
                  {!account.credentialsValid && (
                    <button type="button" className="admin-icon-btn" title="Re-enter password"
                      aria-label={`Re-enter the password for ${account.email}`}
                      onClick={() => { setDialogError(null); setReentering(account) }}>
                      <KeyIcon />
                    </button>
                  )}
                  <button type="button" className="admin-icon-btn is-danger" title="Disconnect"
                    aria-label={`Disconnect ${account.email}`}
                    onClick={() => setDeleting(account)}>
                    <TrashIcon />
                  </button>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {reentering && (
        <ReenterPasswordDialog
          email={reentering.email}
          pending={updatePassword.isPending}
          error={dialogError}
          onSubmit={savePassword}
          onClose={() => setReentering(null)}
        />
      )}
      {deleting && (
        <DeleteConfirmModal
          loading={disconnect.isPending}
          message={<>
            Disconnect <strong>{deleting.email}</strong>? Nothing in that mailbox is deleted — Weesky
            simply stops opening it.
            {/* The session falls back on its own, but silently: say so before it happens. */}
            {deleting.id === activeAccountId
              && ' You are reading this mailbox right now, so you will be taken back to your own.'}
          </>}
          onConfirm={confirmDisconnect}
          onClose={() => setDeleting(null)}
        />
      )}

      <Toasts toasts={toasts} onRemove={removeToast} />
    </div>
  )
}
