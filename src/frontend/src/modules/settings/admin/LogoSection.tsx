import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal'
import { useAppLogo } from '../../../hooks/useAppLogo'
import { APP_SETTING_KEYS, useAppSettings, useDeleteAppLogo, useSetAppLogo } from '../../../hooks/useAppSettings'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import { PRODUCT } from '../../../lib/product'
import type { AddToast } from '../../../hooks/useToasts'
import { LOGO_TOO_DETAILED, LOGO_UNREADABLE, prepareLogo, type LogoErrorKey, type PreparedLogo } from './logoImage'

interface Pending extends PreparedLogo { previewBar: string; previewTab: string }

/** The instance logo: chosen, previewed, then sent as three PNGs drawn here, never decoded server side. */
export default function LogoSection({ addToast }: { addToast: AddToast }) {
  const { t } = useTranslation('admin')
  const { data: settings } = useAppSettings()
  const logo = useAppLogo()
  const save = useSetAppLogo()
  const restore = useDeleteAppLogo()
  const input = useRef<HTMLInputElement>(null)
  const latest = useRef(0)
  const [pending, setPending] = useState<Pending | null>(null)
  const [error, setError] = useState<LogoErrorKey | null>(null)
  const [confirming, setConfirming] = useState(false)

  useEffect(() => () => {
    if (pending) { URL.revokeObjectURL(pending.previewBar); URL.revokeObjectURL(pending.previewTab) }
  }, [pending])
  // A preparation still running when the section closes, or overtaken by a newer choice, is dropped.
  useEffect(() => () => { latest.current++ }, [])

  async function choose(file: File | undefined) {
    if (input.current) input.current.value = ''
    if (!file) return
    const ticket = ++latest.current
    setError(null)
    try {
      const prepared = await prepareLogo(file)
      if (ticket !== latest.current) return
      setPending({ ...prepared,
        previewBar: URL.createObjectURL(prepared.images[192]),
        previewTab: URL.createObjectURL(prepared.images[32]) })
    } catch (caught) {
      if (ticket !== latest.current) return
      setPending(null)
      setError(caught instanceof Error && caught.message === LOGO_TOO_DETAILED ? LOGO_TOO_DETAILED : LOGO_UNREADABLE)
    }
  }

  async function send() {
    if (!pending) return
    try {
      await save.mutateAsync(pending.images)
      setPending(null)
      addToast(t('application.logoSaved'))
    } catch (caught) {
      addToast(apiErrorMessage(caught, t('application.logoSaveFailed')), 'error')
    }
  }

  async function backToScotty() {
    try {
      await restore.mutateAsync()
      addToast(t('application.logoRestored'))
    } catch (caught) {
      addToast(apiErrorMessage(caught, t('application.logoRestoreFailed')), 'error')
    }
  }

  const custom = Boolean(settings?.[APP_SETTING_KEYS.logo])
  const busy = save.isPending || restore.isPending

  return (
    <div className="svc-account-section">
      <h2 className="svc-account-section-title">{t('application.logo')}</h2>
      <p className="svc-account-section-intro">{t('application.logoIntro')}</p>

      <div className="logo-preview">
        <div className="logo-preview-bar">
          <img src={pending?.previewBar ?? logo[192]} alt={t('application.logoPreviewBar')} className="topbar-logo" />
          <span className="topbar-name">{PRODUCT.name} <span className="topbar-name-kind">{PRODUCT.kind}</span></span>
        </div>
        <img src={pending?.previewTab ?? logo[32]} alt={t('application.logoPreviewTab')} className="logo-preview-tab"
          width={32} height={32} />
      </div>

      {pending?.lowResolution && <p className="setting-hint">{t('application.logoLowResolution')}</p>}
      {error && <div className="alert alert-error" role="alert">{t(error)}</div>}

      <input ref={input} type="file" hidden aria-label={t('application.logoFile')}
        accept="image/png,image/jpeg,image/webp,image/svg+xml"
        onChange={event => void choose(event.target.files?.[0])} />

      <div className="logo-actions">
        {pending ? (
          <>
            <button type="button" className="btn btn-primary btn-auto" disabled={busy} onClick={() => void send()}>
              {save.isPending ? <span className="spinner" /> : t('actions.save', { ns: 'common' })}
            </button>
            <button type="button" className="btn btn-ghost btn-auto" disabled={busy} onClick={() => setPending(null)}>
              {t('actions.cancel', { ns: 'common' })}
            </button>
          </>
        ) : (
          <>
            <button type="button" className="btn btn-ghost btn-auto" disabled={busy}
              onClick={() => input.current?.click()}>
              {t('application.logoChange')}
            </button>
            {custom && (
              <button type="button" className="btn btn-ghost btn-auto" disabled={busy} onClick={() => setConfirming(true)}>
                {t('application.logoRestore')}
              </button>
            )}
          </>
        )}
      </div>

      {confirming && (
        <DeleteConfirmModal title={t('application.logoRestoreTitle')} message={t('application.logoRestoreBody')}
          confirmLabel={t('application.logoRestoreConfirm')} tone="primary"
          onConfirm={backToScotty} onClose={() => setConfirming(false)} loading={restore.isPending} />
      )}
    </div>
  )
}
