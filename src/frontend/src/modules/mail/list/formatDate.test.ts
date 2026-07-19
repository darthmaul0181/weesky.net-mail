import { describe, it, expect } from 'vitest'
import { formatListDate } from './formatDate'

const now = new Date('2026-07-18T15:00:00Z')

describe('formatListDate', () => {
  // Assertions stay locale-independent: the rendered month name follows the browser locale,
  // so what is pinned here is the intent — precision shrinks as the message ages.

  it('shows a time for today', () => {
    expect(formatListDate('2026-07-18T09:30:00Z', now)).toMatch(/\d{1,2}[:h]\d{2}/)
  })

  it('omits the year within the current year', () => {
    const formatted = formatListDate('2026-03-04T09:30:00Z', now)

    expect(formatted).not.toMatch(/2026/)
    expect(formatted).toMatch(/4/)
    expect(formatted).not.toMatch(/\d{1,2}[:h]\d{2}/)   // not a time either
  })

  it('shows the year for an older message', () => {
    expect(formatListDate('2024-03-04T09:30:00Z', now)).toMatch(/2024/)
  })

  it('returns an empty string for an unparseable date', () => {
    expect(formatListDate('not-a-date', now)).toBe('')
  })

  it('returns an empty string for an empty value', () => {
    expect(formatListDate('', now)).toBe('')
  })
})
