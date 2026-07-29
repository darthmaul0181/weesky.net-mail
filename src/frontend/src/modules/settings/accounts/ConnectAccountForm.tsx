import { useState, type FormEvent } from 'react'
import { errorText, useConnectableDomains, useConnectAccount } from './useConnectedAccounts'

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
  const { data: domains } = useConnectableDomains()
  const connect = useConnectAccount()
  const [domainId, setDomainId] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    const address = email.trim()
    try {
      await connect.mutateAsync({ domainId: domainId || null, email: address, password })
      onConnected(address)
    } catch (failure) {
      setError(errorText(failure))
    }
  }

  return (
    <div className="connect-account-panel">
      <div className="admin-list-header">
        <span className="admin-list-title">Connect an account</span>
        <button type="button" className="modal-close" aria-label="Close" onClick={onCancel}>✕</button>
      </div>

      <form onSubmit={submit}>
        {error && <div className="alert alert-error" role="alert">{error}</div>}

        {/* .field-h puts the label beside its control: without the htmlFor/id pair the control
            has no accessible name. */}
        <div className="field-h">
          <label htmlFor="connect-server">Server</label>
          <select id="connect-server" value={domainId} onChange={e => setDomainId(e.target.value)}>
            <option value="">Weesky (local)</option>
            {(domains ?? []).map(domain => (
              <option key={domain.id} value={domain.id}>{domain.name}</option>
            ))}
          </select>
        </div>

        <div className="field-h">
          <label htmlFor="connect-email">Email</label>
          <input id="connect-email" type="email" autoComplete="off" autoFocus
            value={email} onChange={e => setEmail(e.target.value)} />
        </div>

        <div className="field-h">
          <label htmlFor="connect-password">Password</label>
          <input id="connect-password" type="password" autoComplete="new-password"
            value={password} onChange={e => setPassword(e.target.value)} />
        </div>

        <p className="settings-note">The connection is verified before the account is saved.</p>

        <button type="submit" className="btn btn-primary" style={{ width: 'auto' }}
          disabled={connect.isPending || !email.trim() || password === ''}>
          {connect.isPending ? <span className="spinner" /> : 'Connect'}
        </button>
      </form>
    </div>
  )
}
