import { describe, expect, it } from 'vitest'
import { stepAnchor } from './useCalendarUrlState'
import { windowOf } from './windowOf'

const ISO = { firstDay: 1, minimalDays: 4 } as const

describe('stepAnchor', () => {
  it('pages the list by exactly the days it shows, so no day repeats across pages', () => {
    const next = stepAnchor('list', '2026-10-01', 1)

    expect(next).toBe('2026-11-01')
    expect(stepAnchor('list', '2026-10-01', -1)).toBe('2026-08-31')
    expect(windowOf('list', '2026-10-01', 'UTC', ISO).lastVisible).toBe('2026-10-31')
  })
})

describe('stepAnchor by month', () => {
  it('keeps the day of the month, clamped to the last day of a shorter month', () => {
    expect(stepAnchor('month', '2026-01-31', 1)).toBe('2026-02-28')
    expect(stepAnchor('month', '2026-12-15', 1)).toBe('2027-01-15')
    expect(stepAnchor('month', '2026-03-31', -1)).toBe('2026-02-28')
    expect(stepAnchor('month', '2026-01-10', -1)).toBe('2025-12-10')
  })
})
