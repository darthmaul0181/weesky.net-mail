import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import PencilIcon from '../../../icons/PencilIcon.jsx'
import GlobeIcon from '../../../icons/GlobeIcon.jsx'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import {
  useCreateExternalDomain, useUpdateExternalDomain,
  type ExternalDomain, type ExternalDomainPayload,
} from './useExternalDomains'

// STARTTLS and SSL/TLS are protocol names; only "None" is prose.
const SECURITY_OPTIONS = [
  { value: 'None', labelKey: 'external.securityNone' },
  { value: 'StartTls', label: 'STARTTLS' },
  { value: 'SslOnConnect', label: 'SSL/TLS' },
] as const satisfies Array<{ value: string; label?: string; labelKey?: string }>

type SecurityOption = (typeof SECURITY_OPTIONS)[number]

function securityLabel(option: SecurityOption, t: TFunction<'admin'>): string {
  return 'labelKey' in option ? t(option.labelKey) : option.label
}

// Mirrors Uri.CheckHostName loosely: a dotted DNS name or an IPv4/IPv6 literal. It only needs to
// catch the common typos before they round-trip — the backend's own check is the real gate.
const HOSTNAME_RE = /^(?!-)[A-Za-z0-9-]{1,63}(?<!-)(\.(?!-)[A-Za-z0-9-]{1,63}(?<!-))*$/
const IPV4_RE = /^(25[0-5]|2[0-4]\d|1?\d?\d)(\.(25[0-5]|2[0-4]\d|1?\d?\d)){3}$/
const IPV6_RE = /^[0-9a-fA-F:]+:[0-9a-fA-F:]*$/

function isValidHost(host: string): boolean {
  if (!host || host.length > 255) return false
  return HOSTNAME_RE.test(host) || IPV4_RE.test(host) || IPV6_RE.test(host)
}

function isValidPort(value: string): boolean {
  if (!/^\d+$/.test(value.trim())) return false
  const port = Number(value)
  return port >= 1 && port <= 65535
}

// Mirrors OAuthProviderConfig.IsHttps: an endpoint reached in the clear would put the client
// secret on the wire, so there is no opt-in the way there is for IMAP.
function isHttpsUrl(value: string): boolean {
  if (!value || value.length > 512) return false
  try {
    return new URL(value).protocol === 'https:'
  } catch {
    return false
  }
}

interface Props {
  domain?: ExternalDomain | null
  onSave: () => void
  onClose: () => void
}

/**
 * Add/edit an admin-curated external mail provider — the admin dialog shape, one `.field-h` row
 * per field. Client-side validation mirrors `AdminController.Validate` exactly (name length, host
 * syntax, port range, sieve both-or-neither) so the common refusals never round-trip.
 */
export default function ExternalDomainDialog({ domain, onSave, onClose }: Props) {
  const { t } = useTranslation('admin')
  const isEdit = !!domain
  const createDomain = useCreateExternalDomain()
  const updateDomain = useUpdateExternalDomain()

  const [name, setName] = useState(domain?.name ?? '')
  const [imapHost, setImapHost] = useState(domain?.imapHost ?? '')
  const [imapPort, setImapPort] = useState(String(domain?.imapPort ?? 993))
  const [imapSecurity, setImapSecurity] = useState(domain?.imapSecurity ?? 'SslOnConnect')
  const [smtpHost, setSmtpHost] = useState(domain?.smtpHost ?? '')
  const [smtpPort, setSmtpPort] = useState(String(domain?.smtpPort ?? 587))
  const [smtpSecurity, setSmtpSecurity] = useState(domain?.smtpSecurity ?? 'StartTls')
  const [sieveHost, setSieveHost] = useState(domain?.sieveHost ?? '')
  const [sievePort, setSievePort] = useState(domain?.sievePort != null ? String(domain.sievePort) : '')
  const [authMode, setAuthMode] = useState<'Password' | 'OAuth2'>(domain?.authMode ?? 'Password')
  const [oauthAuthorizationUrl, setOauthAuthorizationUrl] = useState(domain?.oauthAuthorizationUrl ?? '')
  const [oauthTokenUrl, setOauthTokenUrl] = useState(domain?.oauthTokenUrl ?? '')
  const [oauthScopes, setOauthScopes] = useState(domain?.oauthScopes ?? '')
  const [oauthClientId, setOauthClientId] = useState(domain?.oauthClientId ?? '')
  // Always seeded empty: the stored secret is write-only and never comes back from the API.
  const [oauthClientSecret, setOauthClientSecret] = useState('')
  const [error, setError] = useState<string | null>(null)

  const nameValid = name.trim() !== '' && name.length <= 100
  const imapHostValid = isValidHost(imapHost)
  const imapPortValid = isValidPort(imapPort)
  const smtpHostValid = isValidHost(smtpHost)
  const smtpPortValid = isValidPort(smtpPort)

  const sieveHostFilled = sieveHost.trim() !== ''
  const sievePortFilled = sievePort.trim() !== ''
  const sieveMismatch = sieveHostFilled !== sievePortFilled
  const sieveHostValid = !sieveHostFilled || isValidHost(sieveHost)
  const sievePortValid = !sievePortFilled || isValidPort(sievePort)

  const isOAuth = authMode === 'OAuth2'
  const secretStored = !!domain?.oauthClientSecretSet
  const authUrlValid = isHttpsUrl(oauthAuthorizationUrl.trim())
  const tokenUrlValid = isHttpsUrl(oauthTokenUrl.trim())
  const scopesValid = oauthScopes.trim() !== '' && oauthScopes.length <= 1024
  const clientIdValid = oauthClientId.trim() !== '' && oauthClientId.length <= 255
  const secretValid = (oauthClientSecret !== '' && oauthClientSecret.length <= 512) || secretStored
  const oauthValid = !isOAuth
    || (authUrlValid && tokenUrlValid && scopesValid && clientIdValid && secretValid)

  const canSubmit = nameValid && imapHostValid && imapPortValid && smtpHostValid && smtpPortValid
    && !sieveMismatch && sieveHostValid && sievePortValid && oauthValid

  const pending = createDomain.isPending || updateDomain.isPending

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!canSubmit) return
    setError(null)
    const payload: ExternalDomainPayload = {
      name: name.trim(),
      imapHost: imapHost.trim(),
      imapPort: Number(imapPort),
      imapSecurity,
      smtpHost: smtpHost.trim(),
      smtpPort: Number(smtpPort),
      smtpSecurity,
      sieveHost: sieveHostFilled ? sieveHost.trim() : null,
      sievePort: sievePortFilled ? Number(sievePort) : null,
      authMode,
      oauthAuthorizationUrl: isOAuth ? oauthAuthorizationUrl.trim() : null,
      oauthTokenUrl: isOAuth ? oauthTokenUrl.trim() : null,
      oauthScopes: isOAuth ? oauthScopes.trim() : null,
      oauthClientId: isOAuth ? oauthClientId.trim() : null,
      // Empty on an edit means "keep the stored secret" — the dialog has nothing to send back.
      oauthClientSecret: isOAuth && oauthClientSecret !== '' ? oauthClientSecret : null,
    }
    try {
      if (isEdit) {
        await updateDomain.mutateAsync({ id: domain.id, domain: payload })
      } else {
        await createDomain.mutateAsync(payload)
      }
      onSave()
    } catch (err) {
      setError(apiErrorMessage(err, t('errorOccurred')))
    }
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">
            {isEdit ? <PencilIcon /> : <GlobeIcon />} {t(isEdit ? 'external.editTitle' : 'external.addTitle')}
          </span>
          <button type="button" className="modal-close" aria-label={t('actions.close', { ns: 'common' })}
            onClick={onClose}>✕</button>
        </div>
        <form onSubmit={handleSubmit}>
          {error && <div className="alert alert-error" role="alert">{error}</div>}

          <div className="field-h">
            <label htmlFor="ext-domain-name">{t('external.displayName')}</label>
            <input id="ext-domain-name" type="text" value={name} maxLength={100}
              className={name && !nameValid ? 'is-error' : undefined}
              onChange={e => setName(e.target.value)} autoFocus />
          </div>

          <div className="field-h">
            <label htmlFor="ext-domain-imap-host">{t('external.imapHost')}</label>
            <input id="ext-domain-imap-host" type="text" value={imapHost}
              className={imapHost && !imapHostValid ? 'is-error' : undefined}
              onChange={e => setImapHost(e.target.value)} />
          </div>
          <div className="field-h">
            <label htmlFor="ext-domain-imap-port">{t('external.imapPort')}</label>
            <input id="ext-domain-imap-port" type="number" min={1} max={65535} value={imapPort}
              className={imapPort && !imapPortValid ? 'is-error' : undefined}
              onChange={e => setImapPort(e.target.value)} />
          </div>
          <div className="field-h">
            <label htmlFor="ext-domain-imap-security">{t('external.imapSecurity')}</label>
            <select id="ext-domain-imap-security" value={imapSecurity}
              onChange={e => setImapSecurity(e.target.value)}>
              {SECURITY_OPTIONS.map(o =>
                <option key={o.value} value={o.value}>{securityLabel(o, t)}</option>)}
            </select>
          </div>

          <div className="field-h">
            <label htmlFor="ext-domain-smtp-host">{t('external.smtpHost')}</label>
            <input id="ext-domain-smtp-host" type="text" value={smtpHost}
              className={smtpHost && !smtpHostValid ? 'is-error' : undefined}
              onChange={e => setSmtpHost(e.target.value)} />
          </div>
          <div className="field-h">
            <label htmlFor="ext-domain-smtp-port">{t('external.smtpPort')}</label>
            <input id="ext-domain-smtp-port" type="number" min={1} max={65535} value={smtpPort}
              className={smtpPort && !smtpPortValid ? 'is-error' : undefined}
              onChange={e => setSmtpPort(e.target.value)} />
          </div>
          <div className="field-h">
            <label htmlFor="ext-domain-smtp-security">{t('external.smtpSecurity')}</label>
            <select id="ext-domain-smtp-security" value={smtpSecurity}
              onChange={e => setSmtpSecurity(e.target.value)}>
              {SECURITY_OPTIONS.map(o =>
                <option key={o.value} value={o.value}>{securityLabel(o, t)}</option>)}
            </select>
          </div>

          <div className="field-h">
            <label htmlFor="ext-domain-auth-mode">{t('external.authentication')}</label>
            <select id="ext-domain-auth-mode" value={authMode}
              onChange={e => setAuthMode(e.target.value as 'Password' | 'OAuth2')}>
              <option value="Password">{t('external.authPassword')}</option>
              <option value="OAuth2">OAuth 2.0</option>
            </select>
          </div>

          {isOAuth && (
            <>
              <div className="field-h">
                <label htmlFor="ext-domain-oauth-auth-url">{t('external.authorizationUrl')}</label>
                <input id="ext-domain-oauth-auth-url" type="text" value={oauthAuthorizationUrl}
                  className={oauthAuthorizationUrl && !authUrlValid ? 'is-error' : undefined}
                  onChange={e => setOauthAuthorizationUrl(e.target.value)} />
              </div>
              <div className="field-h">
                <label htmlFor="ext-domain-oauth-token-url">{t('external.tokenUrl')}</label>
                <input id="ext-domain-oauth-token-url" type="text" value={oauthTokenUrl}
                  className={oauthTokenUrl && !tokenUrlValid ? 'is-error' : undefined}
                  onChange={e => setOauthTokenUrl(e.target.value)} />
              </div>
              <div className="field-h">
                <label htmlFor="ext-domain-oauth-scopes">{t('external.scopes')}</label>
                <input id="ext-domain-oauth-scopes" type="text" value={oauthScopes}
                  className={oauthScopes && !scopesValid ? 'is-error' : undefined}
                  onChange={e => setOauthScopes(e.target.value)} />
              </div>
              <div className="field-h">
                <label htmlFor="ext-domain-oauth-client-id">{t('external.clientId')}</label>
                <input id="ext-domain-oauth-client-id" type="text" value={oauthClientId}
                  className={oauthClientId && !clientIdValid ? 'is-error' : undefined}
                  onChange={e => setOauthClientId(e.target.value)} />
              </div>
              <div className="field-h">
                <label htmlFor="ext-domain-oauth-secret">{t('external.clientSecret')}</label>
                <input id="ext-domain-oauth-secret" type="password" autoComplete="new-password"
                  value={oauthClientSecret} placeholder={secretStored ? t('external.secretUnchanged') : undefined}
                  onChange={e => setOauthClientSecret(e.target.value)} />
              </div>
              <p className="settings-note">
                {t(secretStored ? 'external.secretStoredNote' : 'external.secretNewNote')}
              </p>
            </>
          )}

          <p className="admin-list-title" style={{ marginTop: '16px', marginBottom: '8px' }}>
            {t('external.sieveHeading')}
          </p>
          <div className="field-h">
            <label htmlFor="ext-domain-sieve-host">{t('external.sieveHost')}</label>
            <input id="ext-domain-sieve-host" type="text" value={sieveHost}
              className={sieveHost && !sieveHostValid ? 'is-error' : undefined}
              onChange={e => setSieveHost(e.target.value)} />
          </div>
          <div className="field-h">
            <label htmlFor="ext-domain-sieve-port">{t('external.sievePort')}</label>
            <input id="ext-domain-sieve-port" type="number" min={1} max={65535} value={sievePort}
              className={sievePort && !sievePortValid ? 'is-error' : undefined}
              onChange={e => setSievePort(e.target.value)} />
          </div>
          {sieveMismatch && (
            <div className="alert alert-error" role="alert">
              {t('external.sieveMismatch')}
            </div>
          )}
          <p className="settings-note">{t('external.sieveNote')}</p>

          <button className="btn btn-primary" type="submit" disabled={pending || !canSubmit}
            style={{ marginTop: '8px' }}>
            {pending
              ? <span className="spinner" />
              : (isEdit ? t('actions.saveChanges', { ns: 'common' }) : t('external.create'))}
          </button>
        </form>
      </div>
    </div>
  )
}
