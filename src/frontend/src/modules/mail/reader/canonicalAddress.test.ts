import { describe, expect, it } from 'vitest'
import { canonicalAddress } from './canonicalAddress'

describe('canonicalAddress', () => {
  it('lower-cases and trims, mirroring the backend', () => {
    expect(canonicalAddress('  News@Example.COM ')).toBe('news@example.com')
  })

  it('answers an empty string for a missing address', () => {
    expect(canonicalAddress(null)).toBe('')
    expect(canonicalAddress(undefined)).toBe('')
  })
})
