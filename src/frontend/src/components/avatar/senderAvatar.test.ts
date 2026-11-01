import { afterEach, describe, expect, it, vi } from 'vitest'
import { avatarHue, avatarInitials } from './senderAvatar'

describe('avatarInitials', () => {
  it('takes the first letters of the first two words', () => {
    expect(avatarInitials('Léa Martin', 'x@y.z')).toBe('LM')
    expect(avatarInitials('Jean-Pierre Le Gall', 'x@y.z')).toBe('JL')
  })

  it('handles a single word', () => {
    expect(avatarInitials('Karim', 'x@y.z')).toBe('K')
  })

  it('falls back to the address when there is no name', () => {
    expect(avatarInitials(undefined, 'noreply@lumen.com')).toBe('N')
    expect(avatarInitials('bob@x.com', 'bob@x.com')).toBe('B')
  })

  it('answers ? with neither', () => {
    expect(avatarInitials('', '')).toBe('?')
    expect(avatarInitials(undefined, undefined)).toBe('?')
  })

  it('never splits a surrogate pair or a wide character', () => {
    expect(avatarInitials('😀 Smile', 'x@y.z')).toBe('😀S')
    expect(avatarInitials('李 小龍', 'x@y.z')).toBe('李小')
  })
})

describe('without Intl.Segmenter', () => {
  afterEach(() => { vi.unstubAllGlobals(); vi.resetModules() })

  it('loads, and takes whole code points instead', async () => {
    vi.stubGlobal('Intl', { ...Intl, Segmenter: undefined })
    vi.resetModules()
    const { avatarInitials: initials } = await import('./senderAvatar')

    expect(initials('élodie 😀ner', undefined)).toBe('É😀')
  })
})

describe('avatarHue', () => {
  it('is stable across case and spaces, and within [0, 360)', () => {
    const hue = avatarHue('Bob@X.com ')
    expect(hue).toBe(avatarHue('bob@x.com'))
    expect(Number.isInteger(hue)).toBe(true)
    expect(hue).toBeGreaterThanOrEqual(0)
    expect(hue).toBeLessThan(360)
  })

  it('differs between two addresses', () => {
    expect(avatarHue('bob@x.com')).not.toBe(avatarHue('alice@y.org'))
  })
})
