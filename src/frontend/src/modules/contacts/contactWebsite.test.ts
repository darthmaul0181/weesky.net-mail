import { describe, expect, it } from 'vitest'
import { websiteHref } from './contactWebsite'

describe('websiteHref', () => {
  // (X1) A bare 'example.com' must not reach an href unmodified: rendered as href="example.com"
  // in this app, the browser resolves it relative to the page's own origin — not to the site
  // named — opening /contacts/example.com instead. The https:// rewrite is what avoids that.
  it('adds https:// to a scheme-less value', () => {
    expect(websiteHref('example.com')).toBe('https://example.com/')
  })

  it('keeps an explicit http(s) URL as-is', () => {
    expect(websiteHref('http://a.b/c')).toBe('http://a.b/c')
  })

  // A port must not be misread as a scheme: `example.com:8080` has no `://`, so a naive
  // `/:/`-based scheme test would leave it untouched instead of prefixing https://.
  it('adds https:// to a scheme-less value carrying a port', () => {
    expect(websiteHref('example.com:8080')).toBe('https://example.com:8080/')
  })

  it('refuses a javascript: URL', () => {
    expect(websiteHref('javascript:alert(1)')).toBeNull()
  })

  it('refuses a data: URL', () => {
    expect(websiteHref('data:text/html,x')).toBeNull()
  })

  it('refuses a mailto: URL', () => {
    expect(websiteHref('mailto:a@b')).toBeNull()
  })

  it('refuses a blank value', () => {
    expect(websiteHref('')).toBeNull()
    expect(websiteHref('   ')).toBeNull()
  })
})
