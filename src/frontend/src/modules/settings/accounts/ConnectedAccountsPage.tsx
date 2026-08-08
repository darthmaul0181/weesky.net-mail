import { useEffect, useRef, useState, type FormEvent } from 'react'
import i18next from 'i18next'
import { Trans, useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router-dom'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal.jsx'
import LoadingBlock from '../../../components/LoadingBlock'
import Toasts from '../../../components/Toasts.jsx'
import { useAuth } from '../../../contexts/AuthContext'
import { useToasts } from '../../../hooks/useToasts.js'
import KeyIcon from '../../../icons/KeyIcon'
import PersonPlusIcon from '../../../icons/PersonPlusIcon.jsx'
import TrashIcon from '../../../icons/TrashIcon.jsx'
import { dateFormat } from '../../../lib/intl'
import ConnectAccountForm from './ConnectAccountForm'
import {
  errorText, leaveTo, oauthCompleteErrorText, providerRefused, useCompleteOAuthConnect,
  useConnectedAccounts, useDeleteConnectedAccount, useStartOAuthConnect,
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
    parts.push(i18next.t('settings:accounts.connectedOn', {
      date: dateFormat({ year: 'numeric', month: 'short', day: 'numeric' }).format(date),
    }))
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
  const { t } = useTranslation('settings')
  const [password, setPassword] = useState('')

  function submit(event: FormEvent) {
    event.preventDefault()
    onSubmit(password)
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title"><KeyIcon /> {t('accounts.reenterPassword')}</span>
          <button className="modal-close" aria-label={t('actions.close', { ns: 'common' })} onClick={onClose}>✕</button>
        </div>
        <form onSubmit={submit}>
          {error && <div className="alert alert-error" role="alert">{error}</div>}
          <p className="settings-note">{t('accounts.reenterHint', { email })}</p>
          <div className="field-h">
            <label htmlFor="reenter-password">{t('accounts.password')}</label>
            <input id="reenter-password" type="password" autoComplete="new-password" autoFocus
              value={password} onChange={e => setPassword(e.target.value)} />
          </div>
          <div className="identity-modal-actions">
            <button type="submit" className="btn btn-primary" style={{ width: 'auto' }}
              disabled={pending || password === ''}>
              {pending ? <span className="spinner" /> : t('actions.save', { ns: 'common' })}
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
  const { t } = useTranslation('settings')
  const { data: accounts, isLoading, isError } = useConnectedAccounts()
  const updatePassword = useUpdateConnectedAccountPassword()
  const disconnect = useDeleteConnectedAccount()
  const startConnect = useStartOAuthConnect()
  const complete = useCompleteOAuthConnect()
  const { toasts, addToast, removeToast } = useToasts()
  const [params, setParams] = useSearchParams()
  const [connecting, setConnecting] = useState(false)
  const [reentering, setReentering] = useState<ConnectedAccount | null>(null)
  const [deleting, setDeleting] = useState<ConnectedAccount | null>(null)
  const [dialogError, setDialogError] = useState<string | null>(null)
  const [reconnecting, setReconnecting] = useState<string | null>(null)
  const resumed = useRef(false)

  // The provider's redirect lands here with the handshake handle. Strip it before completing:
  // a refresh must not replay a consumed state, nor a shared URL re-raise a stale error.
  useEffect(() => {
    if (resumed.current) return
    const state = params.get('oauthState')
    const failed = params.get('oauthError')
    if (!state && !failed) return

    resumed.current = true
    setParams(new URLSearchParams(), { replace: true })
    // Client-side: the provider itself reported the failure, before any call reached the
    // backend — same wording as errors:oauthHandshakeIncomplete, kept as one copy via the key.
    if (failed) { addToast(i18next.t('errors:oauthHandshakeIncomplete'), 'error'); return }

    complete.mutateAsync(state!)
      .then(account => addToast(t('accounts.connected', { email: account.email })))
      .catch(failure => addToast(oauthCompleteErrorText(failure), 'error'))
    // Mount only: the parameter is stripped above, so a params change must not re-run this.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  async function reconnect(account: ConnectedAccount) {
    setReconnecting(account.id)
    try {
      const { authorizationUrl } = await startConnect.mutateAsync({ accountId: account.id })
      leaveTo(authorizationUrl)
    } catch (failure) {
      addToast(errorText(failure, providerRefused()), 'error')
      setReconnecting(null)
    }
  }

  async function savePassword(password: string) {
    if (!reentering) return
    setDialogError(null)
    try {
      await updatePassword.mutateAsync({ id: reentering.id, password })
      addToast(t('accounts.connectedAgain', { email: reentering.email }))
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
      addToast(t('accounts.disconnected', { email }))
    } catch (failure) {
      addToast(errorText(failure, t('accounts.disconnectFailed')), 'error')
    } finally {
      setDeleting(null)
    }
  }

  return (
    <div className="settings-page">
      <div className="settings-page-header">
        <h1 className="settings-page-title"><PersonPlusIcon size={17} />{t('nav.accounts')}</h1>
      </div>
      <p className="settings-note">{t('accounts.intro')}</p>
      {/* The return from the provider is a plain page load: without this the list simply sits
          there, one mailbox short, for the width of the exchange. */}
      {complete.isPending && <p className="settings-note">{t('accounts.finishing')}</p>}

      {isLoading && <LoadingBlock />}
      {/* Only when there is nothing to show: a failed background refetch must not blank a list
          that is already on screen and still perfectly usable. */}
      {!isLoading && !accounts && <p>{t('accounts.loadFailed')}</p>}
      {!isLoading && accounts && (
        <div className="connected-accounts">
          {connecting
            ? (
              <ConnectAccountForm
                onCancel={() => setConnecting(false)}
                onConnected={email => { setConnecting(false); addToast(t('accounts.connected', { email })) }}
              />
            )
            : (
              <div className="admin-list-header">
                <button className="btn btn-primary" style={{ width: 'auto' }}
                  onClick={() => setConnecting(true)}>
                  <PersonPlusIcon /> {t('accounts.connect')}
                </button>
              </div>
            )}

          {isError && <p className="settings-note">{t('refreshFailed')}</p>}
          {accounts.length === 0 && (
            <p className="settings-note">{t('accounts.empty')}</p>
          )}

          <div className="admin-list connected-account-list">
            {accounts.map(account => (
              <div key={account.id} className="admin-list-item">
                <span className="connected-account-text">
                  <span className="admin-list-item-email">{account.displayName || account.email}</span>
                  <span className="admin-list-item-name">{subtitleOf(account)}</span>
                  {!account.credentialsValid && (
                    <span className="connected-account-warn">
                      {t(account.authMode === 'OAuth2'
                        ? 'accounts.needsReconnect'
                        : 'accounts.needsPassword')}
                    </span>
                  )}
                </span>
                <div className="admin-list-item-actions">
                  {/* Offered on every OAuth row, not only an invalid one: `credentialsValid` says
                      the cipher still opens, never that the provider still honours the token, so a
                      revoked consent leaves the row looking healthy while the mailbox refuses.
                      Re-consenting is idempotent and keeps the row's id, identities and roles. */}
                  {account.authMode === 'OAuth2'
                    ? (
                      <button type="button" className="btn btn-ghost" title={t('accounts.signInAgain')}
                        aria-label={t('accounts.reconnectAria', { email: account.email })}
                        disabled={reconnecting === account.id}
                        onClick={() => reconnect(account)}>
                        {reconnecting === account.id ? <span className="spinner" /> : t('accounts.reconnect')}
                      </button>
                    )
                    : !account.credentialsValid && (
                      <button type="button" className="admin-icon-btn" title={t('accounts.reenterPassword')}
                        aria-label={t('accounts.reenterAria', { email: account.email })}
                        onClick={() => { setDialogError(null); setReentering(account) }}>
                        <KeyIcon />
                      </button>
                    )}
                  <button type="button" className="admin-icon-btn is-danger" title={t('accounts.disconnect')}
                    aria-label={t('accounts.disconnectAria', { email: account.email })}
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
            <Trans i18nKey="accounts.disconnectConfirm" ns="settings"
              components={{ name: <strong>{deleting.email}</strong> }} />
            {/* The session falls back on its own, but silently: say so before it happens. */}
            {deleting.id === activeAccountId && <>{' '}{t('accounts.disconnectActive')}</>}
          </>}
          onConfirm={confirmDisconnect}
          onClose={() => setDeleting(null)}
        />
      )}

      <Toasts toasts={toasts} onRemove={removeToast} />
    </div>
  )
}
