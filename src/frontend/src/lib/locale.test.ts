import { describe, expect, it, beforeEach } from 'vitest'
import {
  LANGUAGE_MIRROR_KEY,
  readLanguageMirror,
  resolveLocale,
  writeLanguageMirror,
} from './locale'

describe('resolveLocale', () => {
  it('takes the stored preference when it names a locale', () => {
    expect(resolveLocale('fr', 'en', ['en-GB'])).toBe('fr')
    expect(resolveLocale('en', 'fr', ['fr-BE'])).toBe('en')
  })

  it('falls through "auto" to the next link rather than treating it as a locale', () => {
    expect(resolveLocale('auto', 'fr', ['en-GB'])).toBe('fr')
  })

  // A value this build does not know can only come from a newer build that shipped a third
  // locale. Falling through is what keeps such an account on a language it can read.
  it('falls through an unrecognised stored value the same way', () => {
    expect(resolveLocale('de', 'fr', ['en-GB'])).toBe('fr')
  })

  it('uses the mirror when nothing is stored', () => {
    expect(resolveLocale(undefined, 'fr', ['en-GB'])).toBe('fr')
  })

  it('falls through an "auto" or unknown mirror to the browser', () => {
    expect(resolveLocale(undefined, 'auto', ['fr-BE'])).toBe('fr')
    expect(resolveLocale(undefined, 'de', ['fr-BE'])).toBe('fr')
  })

  it('matches a browser language on its primary subtag, not on the whole tag', () => {
    expect(resolveLocale(undefined, undefined, ['fr-BE'])).toBe('fr')
    expect(resolveLocale(undefined, undefined, ['en-US'])).toBe('en')
  })

  // A three-letter primary subtag must not be truncated into a two-letter one it never named:
  // `frr` is Northern Frisian, not French, and a first-two-characters match would confuse them.
  it('does not mistake a three-letter primary subtag for a two-letter one it starts with', () => {
    expect(resolveLocale(undefined, undefined, ['frr'])).toBe('en')
    expect(resolveLocale(undefined, undefined, ['fr-BE'])).toBe('fr')
  })

  it('takes the first supported browser language, skipping the ones it cannot serve', () => {
    expect(resolveLocale(undefined, undefined, ['de-DE', 'nl-BE', 'fr-BE', 'en'])).toBe('fr')
  })

  it('answers en when nothing at all matches', () => {
    expect(resolveLocale(undefined, undefined, ['de-DE'])).toBe('en')
    expect(resolveLocale(undefined, undefined, [])).toBe('en')
  })

  it('is case-insensitive about a browser tag', () => {
    expect(resolveLocale(undefined, undefined, ['FR-BE'])).toBe('fr')
  })
})

describe('the mirror', () => {
  beforeEach(() => localStorage.clear())

  it('round-trips through localStorage under the shared key', () => {
    writeLanguageMirror('fr')
    expect(localStorage.getItem(LANGUAGE_MIRROR_KEY)).toBe('fr')
    expect(readLanguageMirror()).toBe('fr')
  })

  it('answers undefined rather than null when nothing is stored', () => {
    expect(readLanguageMirror()).toBeUndefined()
  })
})
