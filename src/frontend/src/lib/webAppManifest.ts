import { APP_SETTING_KEYS, installableOf, type AppSettings } from '../hooks/useAppSettings'

interface Icon { src: string; sizes: string; type: string }
interface Shortcut { name: string; url: string }
interface ProtocolHandler { protocol: string; url: string }

export interface WebAppManifest {
  id: string
  name: string
  short_name: string
  start_url: string
  scope: string
  display: 'standalone'
  theme_color: string
  background_color: string
  icons: Icon[]
  shortcuts: Shortcut[]
  protocol_handlers: ProtocolHandler[]
}

// The night palette in light mode, the one an account with no preference gets. A manifest
// carries only one colour: it cannot follow eight palettes across two modes.
const THEME_COLOR = '#182238'
const BACKGROUND_COLOR = '#f6f3ef'

/** The manifest, or null when nothing should be posted. Every URL is absolute (a blob: has an
 * opaque path), built from the current origin so it holds on every deployment. */
export function buildManifest(
  settings: AppSettings | undefined, origin: string, t: (key: 'mail:layout.newMessage' | 'common:rail.contacts') => string,
): WebAppManifest | null {
  if (!settings || !installableOf(settings)) return null

  const name = (settings[APP_SETTING_KEYS.name] ?? '').trim()
  const shortName = (settings[APP_SETTING_KEYS.shortName] ?? '').trim()
  if (!name || !shortName) return null

  return {
    id: `${origin}/`,
    name,
    short_name: shortName,
    start_url: `${origin}/`,
    scope: `${origin}/`,
    display: 'standalone',
    theme_color: THEME_COLOR,
    background_color: BACKGROUND_COLOR,
    icons: [
      { src: `${origin}/icon-192.png`, sizes: '192x192', type: 'image/png' },
      { src: `${origin}/icon-512.png`, sizes: '512x512', type: 'image/png' },
    ],
    shortcuts: [
      { name: t('mail:layout.newMessage'), url: `${origin}/mail/compose` },
      { name: t('common:rail.contacts'), url: `${origin}/contacts` },
    ],
    protocol_handlers: [{ protocol: 'mailto', url: `${origin}/mail/compose?mailto=%s` }],
  }
}
