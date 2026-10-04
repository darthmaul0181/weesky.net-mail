import { describe, it, expect } from 'vitest'
import i18next from 'i18next'
import { buildManifest } from './webAppManifest'
import { logoUrls } from './appLogo'

const ORIGIN = 'https://account.mail.weesky.net'
const t = i18next.getFixedT('en', ['mail', 'common'])

const enabled = {
  'app.installable': 'true',
  'app.name': 'Scotty mail',
  'app.shortName': 'Scotty',
}

describe('buildManifest', () => {
  it('answers null while the settings have not arrived', () => {
    expect(buildManifest(undefined, ORIGIN, t, logoUrls(''))).toBeNull()
  })

  it('answers null when the app is disabled', () => {
    expect(buildManifest({ ...enabled, 'app.installable': 'false' }, ORIGIN, t, logoUrls(''))).toBeNull()
  })

  // A manifest without a name is refused by the browser: better to post nothing at all.
  it('answers null when a name is missing or blank', () => {
    expect(buildManifest({ ...enabled, 'app.name': '' }, ORIGIN, t, logoUrls(''))).toBeNull()
    expect(buildManifest({ ...enabled, 'app.shortName': '   ' }, ORIGIN, t, logoUrls(''))).toBeNull()
  })

  it('carries the names the admin set', () => {
    const manifest = buildManifest(enabled, ORIGIN, t, logoUrls(''))!

    expect(manifest.name).toBe('Scotty mail')
    expect(manifest.short_name).toBe('Scotty')
  })

  // A blob: has an opaque path: a relative URL is not reliably resolvable against it.
  it('spells every URL absolutely', () => {
    const manifest = buildManifest(enabled, ORIGIN, t, logoUrls(''))!

    const urls = [
      manifest.id, manifest.start_url, manifest.scope,
      ...manifest.icons.map(i => i.src),
      ...manifest.shortcuts.map(s => s.url),
      ...manifest.protocol_handlers.map(p => p.url),
    ]
    expect(urls.every(url => url.startsWith(`${ORIGIN}/`))).toBe(true)
  })

  it('offers the two icon sizes the install criteria require', () => {
    const manifest = buildManifest(enabled, ORIGIN, t, logoUrls(''))!

    expect(manifest.icons.map(i => i.sizes)).toEqual(['192x192', '512x512'])
  })

  it('opens standalone at the root, which already redirects to the mailbox', () => {
    const manifest = buildManifest(enabled, ORIGIN, t, logoUrls(''))!

    expect(manifest.display).toBe('standalone')
    expect(manifest.start_url).toBe(`${ORIGIN}/`)
  })

  // The night palette in light mode, the one an account with no preference gets. A manifest
  // carries a single colour and cannot follow the palettes, so these two are pinned here.
  it('paints the splash from the night palette in light mode', () => {
    const manifest = buildManifest(enabled, ORIGIN, t, logoUrls(''))!

    expect(manifest.theme_color).toBe('#182238')
    expect(manifest.background_color).toBe('#f6f3ef')
  })

  it('registers the two shortcuts and the mailto handler', () => {
    const manifest = buildManifest(enabled, ORIGIN, t, logoUrls(''))!

    expect(manifest.shortcuts.map(s => s.name)).toEqual(['New message', 'Contacts'])
    expect(manifest.protocol_handlers).toEqual(
      [{ protocol: 'mailto', url: `${ORIGIN}/mail/compose?mailto=%s` }])
  })

  it('names the shortcuts in the UI language', () => {
    const manifest = buildManifest(enabled, ORIGIN, i18next.getFixedT('fr', ['mail', 'common']), logoUrls(''))!

    expect(manifest.shortcuts.map(s => s.name)).toEqual(['Nouveau message', 'Contacts'])
  })

  it('names the custom logo as the installed icon, absolute', () => {
    const manifest = buildManifest(enabled, 'https://mail.example.net', t, {
      32: 'https://api.example.net/api/AppSettings/logo/32?v=v1',
      192: 'https://api.example.net/api/AppSettings/logo/192?v=v1',
      512: 'https://api.example.net/api/AppSettings/logo/512?v=v1',
    })
    expect(manifest!.icons).toEqual([
      { src: 'https://api.example.net/api/AppSettings/logo/192?v=v1', sizes: '192x192', type: 'image/png' },
      { src: 'https://api.example.net/api/AppSettings/logo/512?v=v1', sizes: '512x512', type: 'image/png' },
    ])
  })

  it('makes a relative icon address absolute on the page origin', () => {
    const manifest = buildManifest(enabled, 'https://mail.example.net', t, logoUrls(''))
    expect(manifest!.icons[1]!.src).toBe('https://mail.example.net/icon-512.png')
  })
})
