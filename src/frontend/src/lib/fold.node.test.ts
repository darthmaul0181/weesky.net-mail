import { describe, it, expect } from 'vitest'
import { fold } from './fold'

describe('fold', () => {
  it('strips diacritics and lowercases', () => {
    expect(fold('Chloé VERMEULEN')).toBe('chloe vermeulen')
  })

  it('leaves plain text alone', () => {
    expect(fold('bruno')).toBe('bruno')
  })

  // \p{Diacritic} also covers ASCII '^' and '`', so stripping that class would delete a caret
  // from plain text — \p{M} (combining marks) is the narrower, correct class.
  it('does not strip ASCII characters that merely look like diacritics', () => {
    expect(fold('a^b`c')).toBe('a^b`c')
  })

  it('lowercases a non-Latin letter', () => {
    expect(fold('Москва')).toBe('москва')
  })
})
