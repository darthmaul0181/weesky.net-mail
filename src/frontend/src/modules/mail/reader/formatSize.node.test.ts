import i18next from 'i18next'
import { afterEach, describe, it, expect } from 'vitest'
import { formatSize } from './formatSize'

describe('formatSize', () => {
  afterEach(async () => { await i18next.changeLanguage('en') })

  it.each([
    [0, '0 B'],
    [512, '512 B'],
    [1023, '1023 B'],
    [1024, '1 KB'],
    [2048, '2 KB'],
    [1_500_000, '1.4 MB'],
  ])('formats %i bytes as %s', (bytes, expected) => {
    expect(formatSize(bytes)).toBe(expected)
  })

  it('returns empty for a nonsensical size', () => {
    expect(formatSize(-1)).toBe('')
    expect(formatSize(Number.NaN)).toBe('')
  })

  it('translates its units', async () => {
    expect(formatSize(2048)).toBe('2 KB')
    await i18next.changeLanguage('fr')
    expect(formatSize(2048)).toBe('2 Ko')
  })
})
