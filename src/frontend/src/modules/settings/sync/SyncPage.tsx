import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, ApiError } from '../../../api.js'
import type { DavCredentials } from '../../../types/dav'
import ToggleRow from '../../../components/ToggleRow'
import LoadingBlock from '../../../components/LoadingBlock'
import Toasts from '../../../components/Toasts.jsx'
import { useToasts } from '../../../hooks/useToasts.js'
import { relativeFromNow } from '../../../lib/intl'
import CopyIcon from '../../../icons/CopyIcon'
import RefreshIcon from '../../../icons/RefreshIcon'

/** Which switch is waiting for its round trip, and on what value. */
type Pending = { key: 'carddav' | 'caldav'; value: boolean }

/** The copy button is icon-only, so its aria-label has to name the value: three buttons all
    called "Copy" in one region give a screen reader no way to tell them apart. */
function CopyButton({ label, value, onCopy }: { label: string; value: string; onCopy: (v: string) => void }) {
  const { t } = useTranslation('settings')
  return (
    <button type="button" className="admin-icon-btn" aria-label={t('sync.copyValue', { label })}
      onClick={() => onCopy(value)}><CopyIcon size={15} /></button>
  )
}

/** A value the user comes back for, with the button that puts it on the clipboard. */
function CopyableRow(
  { label, value, onCopy }: { label: string; value: string; onCopy: (value: string) => void },
) {
  return (
    <div className="field-h is-setting">
      <span className="setting-label">{label}</span>
      <span className="sync-value">
        <span>{value}</span>
        <CopyButton label={label} value={value} onCopy={onCopy} />
      </span>
    </div>
  )
}

/**
 * The one screen of slices 4c-i and 5c. Named for what it does rather than for the protocols it
 * speaks: CardDAV and CalDAV are words the user meets in their client, not in their head, and
 * naming the route after the first to arrive would have forced a rename on a bookmark.
 */
export default function SyncPage() {
  const { t } = useTranslation('settings')
  const [state, setState] = useState<DavCredentials | null>(null)
  const [failed, setFailed] = useState<'load' | 'unavailable' | null>(null)
  const [busy, setBusy] = useState(false)
  // The value the switch shows while the round trip is in flight, so it moves at the click and
  // returns on its own if the write is refused. Keyed by switch: a single boolean would paint the
  // value one switch is waiting for onto the other.
  const [pending, setPending] = useState<Pending | null>(null)
  const [confirming, setConfirming] = useState(false)
  // Held here and nowhere else, so it dies with the page: it exists in clear in exactly one
  // response, and there is no second way to obtain it.
  const [secret, setSecret] = useState<string | null>(null)
  const { toasts, addToast, removeToast } = useToasts()

  useEffect(() => {
    // 404 is the deployment saying it publishes no address at all — a permanent condition, which
    // the nav hides but a bookmark still reaches. "Could not load" would misreport it as transient.
    api.getDavCredentials().then(setState).catch((error: unknown) =>
      setFailed(error instanceof ApiError && error.status === 404 ? 'unavailable' : 'load'))
  }, [])

  async function write(call: () => Promise<DavCredentials>, optimistic: Pending | null = null) {
    setBusy(true)
    setPending(optimistic)
    try {
      const next = await call()
      // The clear secret is dropped from the server copy on the way in: `secret` below is then
      // the only place it lives, structurally rather than by convention.
      setState({ ...next, password: undefined })
      setSecret(next.password ?? null)
    } catch {
      addToast(t('sync.saveFailed'), 'error')
    } finally {
      setBusy(false)
      setPending(null)
    }
  }

  // A refused clipboard is not an error to report: the value is on screen and selectable.
  function copy(value: string) {
    navigator.clipboard?.writeText(value)
      .then(() => addToast(t('sync.copied')))
      .catch(() => {})
  }

  return (
    <div className="settings-page">
      <div className="settings-page-header">
        <h1 className="settings-page-title"><RefreshIcon size={17} />{t('nav.sync')}</h1>
      </div>

      {failed && <p>{t(failed === 'unavailable' ? 'sync.unavailable' : 'sync.loadFailed')}</p>}
      {!failed && !state && <LoadingBlock />}

      {!failed && state && (
        <>
          <ToggleRow
            id="sync-carddav"
            label={t('sync.carddav')}
            hint={t('sync.carddavHint')}
            checked={pending?.key === 'carddav' ? pending.value : state.cardDavEnabled}
            disabled={busy}
            onChange={on => write(() => api.setDavCardDav(on), { key: 'carddav', value: on })}
          />

          <ToggleRow
            id="sync-caldav"
            label={t('sync.caldav')}
            hint={t('sync.caldavHint')}
            checked={pending?.key === 'caldav' ? pending.value : state.calDavEnabled}
            disabled={busy}
            onChange={on => write(
              () => api.setDavCalDav(on, Intl.DateTimeFormat().resolvedOptions().timeZone),
              { key: 'caldav', value: on })}
          />

          <div className="account-section">
            <h2>{t('sync.connection')}</h2>
            <CopyableRow label={t('sync.serverUrl')} value={state.serverUrl} onCopy={copy} />
            <CopyableRow label={t('sync.username')} value={state.username} onCopy={copy} />

            <div className="field-h is-setting">
              <span className="setting-label">{t('sync.password')}</span>
              <span className="sync-value">
                {secret
                  ? (
                    <>
                      <code className="sync-secret">{secret}</code>
                      <CopyButton label={t('sync.password')} value={secret} onCopy={copy} />
                    </>
                  )
                  : <span>{t(state.configured ? 'sync.hidden' : 'sync.notConfigured')}</span>}
                {state.configured && (
                  <button type="button" className="btn" disabled={busy}
                    onClick={() => setConfirming(true)}>{t('sync.regenerate')}</button>
                )}
              </span>
            </div>
            {secret && <p className="sync-secret-note">{t('sync.shownOnce')}</p>}

            <div className="field-h is-setting">
              <span className="setting-label">{t('sync.lastUsed')}</span>
              <span className="sync-value">
                <span>{state.lastUsedAt ? relativeFromNow(state.lastUsedAt) : t('sync.neverUsed')}</span>
              </span>
            </div>
          </div>
        </>
      )}

      {confirming && (
        <div className="modal-overlay" onClick={() => setConfirming(false)}>
          <div className="modal" onClick={e => e.stopPropagation()}>
            <div className="modal-header">
              <span className="modal-title">{t('sync.regenerateTitle')}</span>
              <button className="modal-close" onClick={() => setConfirming(false)}>✕</button>
            </div>
            <p>{t('sync.regenerateWarning')}</p>
            <div className="modal-actions">
              <button type="button" className="btn btn-primary" aria-label={t('sync.regenerateTitle')}
                onClick={() => { setConfirming(false); write(() => api.regenerateDavSecret()) }}>
                {t('sync.regenerate')}
              </button>
            </div>
          </div>
        </div>
      )}

      <Toasts toasts={toasts} onRemove={removeToast} />
    </div>
  )
}
