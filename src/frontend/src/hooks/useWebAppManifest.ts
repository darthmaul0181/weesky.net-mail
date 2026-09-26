import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { buildManifest } from '../lib/webAppManifest'
import { useAppSettings } from './useAppSettings'

/** Posts the install manifest as an in-memory blob: start_url must share the manifest's origin,
 * which rules out the API, and a static frontend cannot compose it. Posted only once the settings
 * say "enabled": posting then withdrawing made the install icon flash. */
export function useWebAppManifest(): void {
  const { data } = useAppSettings()
  // A new `t` on each language change: the shortcuts are posted again under their new names.
  const { t } = useTranslation(['mail', 'common'])

  useEffect(() => {
    const manifest = buildManifest(data, window.location.origin, t)
    if (!manifest) return

    const url = URL.createObjectURL(
      new Blob([JSON.stringify(manifest)], { type: 'application/manifest+json' }))
    const link = document.createElement('link')
    link.rel = 'manifest'
    link.href = url
    document.head.appendChild(link)

    return () => {
      link.remove()
      URL.revokeObjectURL(url)
    }
  }, [data, t])
}
