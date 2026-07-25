import { describe, it, expect } from 'vitest'
import { formatSize } from './formatSize'

describe('formatSize', () => {
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
})
