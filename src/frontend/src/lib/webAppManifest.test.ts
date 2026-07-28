import { describe, it, expect } from 'vitest'
import { buildManifest } from './webAppManifest'

const ORIGIN = 'https://account.mail.weesky.net'

const enabled = {
  'app.installable': 'true',
  'app.name': 'Snoopy mail',
  'app.shortName': 'Snoopy',
}

describe('buildManifest', () => {
  it('answers null while the settings have not arrived', () => {
    expect(buildManifest(undefined, ORIGIN)).toBeNull()
  })

  it('answers null when the app is disabled', () => {
    expect(buildManifest({ ...enabled, 'app.installable': 'false' }, ORIGIN)).toBeNull()
  })

  // A manifest without a name is refused by the browser: better to post nothing at all.
  it('answers null when a name is missing or blank', () => {
    expect(buildManifest({ ...enabled, 'app.name': '' }, ORIGIN)).toBeNull()
    expect(buildManifest({ ...enabled, 'app.shortName': '   ' }, ORIGIN)).toBeNull()
  })

  it('carries the names the admin set', () => {
    const manifest = buildManifest(enabled, ORIGIN)!

    expect(manifest.name).toBe('Snoopy mail')
    expect(manifest.short_name).toBe('Snoopy')
  })

  // A blob: has an opaque path: a relative URL is not reliably resolvable against it.
  it('spells every URL absolutely', () => {
    const manifest = buildManifest(enabled, ORIGIN)!

    const urls = [
      manifest.id, manifest.start_url, manifest.scope,
      ...manifest.icons.map(i => i.src),
      ...manifest.shortcuts.map(s => s.url),
      ...manifest.protocol_handlers.map(p => p.url),
    ]
    expect(urls.every(url => url.startsWith(`${ORIGIN}/`))).toBe(true)
  })

  it('offers the two icon sizes the install criteria require', () => {
    const manifest = buildManifest(enabled, ORIGIN)!

    expect(manifest.icons.map(i => i.sizes)).toEqual(['192x192', '512x512'])
  })

  it('opens standalone at the root, which already redirects to the mailbox', () => {
    const manifest = buildManifest(enabled, ORIGIN)!

    expect(manifest.display).toBe('standalone')
    expect(manifest.start_url).toBe(`${ORIGIN}/`)
  })

  // The night palette in light mode, the one an account with no preference gets. A manifest
  // carries a single colour and cannot follow the palettes, so these two are pinned here.
  it('paints the splash from the night palette in light mode', () => {
    const manifest = buildManifest(enabled, ORIGIN)!

    expect(manifest.theme_color).toBe('#182238')
    expect(manifest.background_color).toBe('#f6f3ef')
  })

  it('registers the two shortcuts and the mailto handler', () => {
    const manifest = buildManifest(enabled, ORIGIN)!

    expect(manifest.shortcuts.map(s => s.name)).toEqual(['New message', 'Contacts'])
    expect(manifest.protocol_handlers).toEqual(
      [{ protocol: 'mailto', url: `${ORIGIN}/mail/compose?mailto=%s` }])
  })
})
