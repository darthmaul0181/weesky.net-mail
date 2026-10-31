import { describe, expect, it } from 'vitest'
import { partStatOf } from './partStat'

describe('partStatOf', () => {
  it('reads the four answers in any case', () => {
    expect(partStatOf('ACCEPTED')).toBe('accepted')
    expect(partStatOf('tentative')).toBe('tentative')
    expect(partStatOf('Declined')).toBe('declined')
    expect(partStatOf('needs-action')).toBe('needs-action')
  })

  it('answers null for a missing or unknown value', () => {
    expect(partStatOf(undefined)).toBeNull()
    expect(partStatOf('DELEGATED')).toBeNull()
    expect(partStatOf('')).toBeNull()
  })
})
