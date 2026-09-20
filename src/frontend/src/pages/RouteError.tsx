import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useRouteError } from 'react-router-dom'

const RELOADED_AT = 'chunkReloadAt'

/** A stale chunk is a deploy that shipped while this tab sat open: the hashed filename the old
    bundle asks for is gone. Chrome, Firefox and Safari each word the failure differently. */
export function isChunkLoadError(error: unknown): boolean {
  const message = error instanceof Error ? error.message : String(error ?? '')
  return /dynamically imported module|Importing a module script failed/i.test(message)
}

/** Root `errorElement`. A stale chunk reloads once (sessionStorage stops a broken deploy looping).
    Offline, a chunk fails the same way, but a reload would land on the browser's "no internet"
    page, so it only says it is offline. */
export default function RouteError() {
  const error = useRouteError()
  const { t } = useTranslation('common')
  const stale = isChunkLoadError(error)
  const offline = stale && navigator.onLine === false

  useEffect(() => {
    if (!stale || offline) return
    try {
      if (Date.now() - Number(sessionStorage.getItem(RELOADED_AT)) < 10_000) return
      sessionStorage.setItem(RELOADED_AT, String(Date.now()))
    } catch { return }
    window.location.reload()
  }, [stale, offline])

  const message = offline ? t('appError.offline') : stale ? t('appError.updated') : t('appError.message')

  return (
    <div className="app-error">
      <div className="modal" role="alert">
        <p style={{ margin: '0 0 20px' }}>{message}</p>
        <button type="button" className="btn btn-primary" onClick={() => window.location.reload()}>
          {t('appError.reload')}
        </button>
      </div>
    </div>
  )
}
