import { describe, it, expect } from 'vitest'
import { spamRatio } from './spamRatio'

const spam = (score: number, threshold: number) => ({ score, threshold, raw: 'X-Spamd-Result: …' })

describe('spamRatio', () => {
  it('is the score over the threshold', () => {
    expect(spamRatio(spam(7, 16))).toBeCloseTo(0.4375)
  })

  it('caps at 1 past the threshold', () => {
    expect(spamRatio(spam(20, 5))).toBe(1)
  })

  // Ham can score negative in both rspamd and SpamAssassin; the gauge floor is empty, not inverted.
  it('floors a negative score at 0', () => {
    expect(spamRatio(spam(-1.5, 15))).toBe(0)
  })

  it('refuses a threshold of zero or less rather than dividing by it', () => {
    expect(spamRatio(spam(3, 0))).toBeNull()
    expect(spamRatio(spam(3, -5))).toBeNull()
  })

  it('answers null for an absent score', () => {
    expect(spamRatio(null)).toBeNull()
    expect(spamRatio(undefined)).toBeNull()
  })
})
