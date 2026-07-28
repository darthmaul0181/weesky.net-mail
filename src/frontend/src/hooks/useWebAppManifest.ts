import { useEffect } from 'react'
import { buildManifest } from '../lib/webAppManifest'
import { useAppSettings } from './useAppSettings'

/**
 * Posts the manifest the browser reads to offer installation.
 *
 * It is built in memory rather than served as a file: the specification wants start_url on the
 * same origin as the manifest, which rules out the API, and the frontend is a heap of static
 * files with no route able to compose it. A blob: inherits the document's origin, so the check
 * passes.
 *
 * Nothing is posted until the settings have answered "enabled". The alternative — a static
 * manifest posted straight away and then removed — made the install icon appear and then vanish,
 * which reads as a rendering fault.
 */
export function useWebAppManifest(): void {
  const { data } = useAppSettings()

  useEffect(() => {
    const manifest = buildManifest(data, window.location.origin)
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
  }, [data])
}
