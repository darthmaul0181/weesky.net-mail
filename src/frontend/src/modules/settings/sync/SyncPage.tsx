import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api } from '../../../api.js'
import type { DavCredentials } from '../../../types/dav'
import ToggleRow from '../../../components/ToggleRow'
import LoadingBlock from '../../../components/LoadingBlock'
import Toasts from '../../../components/Toasts.jsx'
import { useToasts } from '../../../hooks/useToasts.js'
import { relativeFromNow } from '../../../lib/intl'
import CopyIcon from '../../../icons/CopyIcon'
import RefreshIcon from '../../../icons/RefreshIcon'

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
 * The one screen of slice 4c-i. Named for what it does rather than for the protocol it speaks:
 * CardDAV is a word the user meets in their client, not in their head, and this tab will host
 * CalDAV — naming it after the first protocol to arrive would force a rename on a route bookmarks
 * have kept.
 */
export default function SyncPage() {
  const { t } = useTranslation('settings')
  const [state, setState] = useState<DavCredentials | null>(null)
  const [failed, setFailed] = useState(false)
  const [busy, setBusy] = useState(false)
  // The value the switch shows while the round trip is in flight, so it moves at the click and
  // returns on its own if the write is refused.
  const [pending, setPending] = useState<boolean | null>(null)
  const [confirming, setConfirming] = useState(false)
  // Held here and nowhere else, so it dies with the page: it exists in clear in exactly one
  // response, and there is no second way to obtain it.
  const [secret, setSecret] = useState<string | null>(null)
  const { toasts, addToast, removeToast } = useToasts()

  useEffect(() => {
    api.getDavCredentials().then(setState).catch(() => setFailed(true))
  }, [])

  async function write(call: () => Promise<DavCredentials>, optimistic: boolean | null = null) {
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

      {failed && <p>{t('sync.loadFailed')}</p>}
      {!failed && !state && <LoadingBlock />}

      {!failed && state && (
        <>
          <ToggleRow
            id="sync-carddav"
            label={t('sync.carddav')}
            hint={t('sync.carddavHint')}
            checked={pending ?? state.cardDavEnabled}
            disabled={busy}
            onChange={on => write(() => api.setDavCardDav(on), on)}
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
                  : state.configured && <span>{t('sync.hidden')}</span>}
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
