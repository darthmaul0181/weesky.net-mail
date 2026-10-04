import { afterEach, describe, expect, it, vi } from 'vitest'
import { currentLogo, logoUrls, rememberLogoVersion, rememberedLogoVersion, setCurrentLogo } from './appLogo'

afterEach(() => { localStorage.clear(); vi.restoreAllMocks() })

describe('logoUrls', () => {
  it('answers the bundled Scotty images without a version', () => {
    const urls = logoUrls('')
    expect(urls[32]).toMatch(/favicon-32/)
    expect(urls[192]).toMatch(/logo-192/)
    expect(urls[512]).toBe('/icon-512.png')
  })

  it('puts the version in every API address, so a new logo is a new URL', () => {
    const urls = logoUrls('20261004T101500123')
    for (const size of [32, 192, 512] as const)
      expect(urls[size]).toMatch(new RegExp(`/api/AppSettings/logo/${size}[?]v=20261004T101500123$`))
  })
})

describe('remembered version', () => {
  it('reads back what was remembered, and nothing at first', () => {
    expect(rememberedLogoVersion()).toBe('')
    rememberLogoVersion('v1')
    expect(rememberedLogoVersion()).toBe('v1')
  })

  it('survives a storage that throws', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('denied') })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('denied') })
    expect(() => rememberLogoVersion('v1')).not.toThrow()
    expect(rememberedLogoVersion()).toBe('')
  })
})

describe('current logo', () => {
  it('starts on Scotty and follows what it is given', () => {
    expect(currentLogo()).toEqual(logoUrls(''))
    setCurrentLogo(logoUrls('v2'))
    expect(currentLogo()[192]).toMatch(/v=v2$/)
  })
})
