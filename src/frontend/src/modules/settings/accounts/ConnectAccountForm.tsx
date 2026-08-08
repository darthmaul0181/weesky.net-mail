import { useCallback, useRef, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import {
  errorText, leaveTo, providerRefused, useConnectableDomains, useConnectAccount,
  useStartOAuthConnect,
} from './useConnectedAccounts'

interface Props {
  onConnected: (email: string) => void
  onCancel: () => void
}

/**
 * Attaches a mailbox. The server list carries names only — hosts and ports are administrator
 * information the endpoint deliberately does not return — and the empty value is the local
 * server, which travels as a null domain.
 */
export default function ConnectAccountForm({ onConnected, onCancel }: Props) {
  const { t } = useTranslation('settings')
  const { data: domains } = useConnectableDomains()
  const connect = useConnectAccount()
  const startConnect = useStartOAuthConnect()
  const [domainId, setDomainId] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  // Not startConnect.isPending: that goes false the instant the URL arrives, re-enabling the
  // button under the finger while the browser is already leaving.
  const [leaving, setLeaving] = useState(false)

  const selected = (domains ?? []).find(d => d.id === domainId)
  const isOAuth = selected?.authMode === 'OAuth2'

  // First mount only, never autoFocus: this field remounts when the server flips back from a
  // provider domain, and refocusing then would steal the select mid keyboard-navigation.
  const focusedOnce = useRef(false)
  const focusOnFirstMount = useCallback((node: HTMLInputElement | null) => {
    if (node && !focusedOnce.current) {
      focusedOnce.current = true
      node.focus()
    }
  }, [])

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (isOAuth) return startOAuth()
    setError(null)
    const address = email.trim()
    try {
      await connect.mutateAsync({ domainId: domainId || null, email: address, password })
      onConnected(address)
    } catch (failure) {
      setError(errorText(failure))
    }
  }

  async function startOAuth() {
    setError(null)
    setLeaving(true)
    try {
      const { authorizationUrl } = await startConnect.mutateAsync({ domainId })
      leaveTo(authorizationUrl)
    } catch (failure) {
      setError(errorText(failure, providerRefused()))
      setLeaving(false)
    }
  }

  return (
    <div className="connect-account-panel">
      <div className="admin-list-header">
        <span className="admin-list-title">{t('accounts.connect')}</span>
        <button type="button" className="modal-close" aria-label={t('actions.close', { ns: 'common' })}
          onClick={onCancel}>✕</button>
      </div>

      <form onSubmit={submit}>
        {error && <div className="alert alert-error" role="alert">{error}</div>}

        {/* .field-h puts the label beside its control: without the htmlFor/id pair the control
            has no accessible name. */}
        <div className="field-h">
          <label htmlFor="connect-server">{t('accounts.server')}</label>
          {/* The refusal named the fields that are about to disappear, so it cannot outlive them. */}
          <select id="connect-server" value={domainId}
            onChange={e => { setError(null); setDomainId(e.target.value) }}>
            <option value="">{t('accounts.localServer')}</option>
            {(domains ?? []).map(domain => (
              <option key={domain.id} value={domain.id}>{domain.name}</option>
            ))}
          </select>
        </div>

        {!isOAuth && (
          <>
            <div className="field-h">
              <label htmlFor="connect-email">{t('accounts.email')}</label>
              <input id="connect-email" type="email" autoComplete="off" ref={focusOnFirstMount}
                value={email} onChange={e => setEmail(e.target.value)} />
            </div>

            <div className="field-h">
              <label htmlFor="connect-password">{t('accounts.password')}</label>
              <input id="connect-password" type="password" autoComplete="new-password"
                value={password} onChange={e => setPassword(e.target.value)} />
            </div>
          </>
        )}

        <p className="settings-note">
          {isOAuth
            ? t('accounts.oauthNote', { provider: selected!.name })
            : t('accounts.verifyNote')}
        </p>

        {isOAuth
          ? (
            <button type="submit" className="btn btn-primary btn-auto" disabled={leaving}>
              {leaving ? <span className="spinner" /> : t('accounts.signInWith', { provider: selected!.name })}
            </button>
          )
          : (
            <button type="submit" className="btn btn-primary btn-auto"
              disabled={connect.isPending || !email.trim() || password === ''}>
              {connect.isPending ? <span className="spinner" /> : t('accounts.connectButton')}
            </button>
          )}
      </form>
    </div>
  )
}
