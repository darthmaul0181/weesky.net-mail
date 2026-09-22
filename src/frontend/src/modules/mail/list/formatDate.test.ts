import { describe, it, expect } from 'vitest'
import { formatListDate } from './formatDate'

/* The day is written out and every message is a local wall clock, the form the midnight case at the
   foot of this file already uses: a UTC instant read as a local day is only the same day as a UTC
   message between UTC-9 and UTC+8, so a host outside that band ran a different case. */
const at = (year: number, month: number, day: number, hour: number, minute: number) =>
  new Date(year, month - 1, day, hour, minute).toISOString()
const now = '2026-07-18'

describe('formatListDate', () => {
  // Assertions stay locale-independent: the rendered month name follows the browser locale,
  // so what is pinned here is the intent — precision shrinks as the message ages.

  it('shows a time for today', () => {
    expect(formatListDate(at(2026, 7, 18, 9, 30), now)).toMatch(/\d{1,2}[:h]\d{2}/)
  })

  it('omits the year within the current year', () => {
    const formatted = formatListDate(at(2026, 3, 4, 9, 30), now)

    expect(formatted).not.toMatch(/2026/)
    expect(formatted).toMatch(/4/)
    expect(formatted).not.toMatch(/\d{1,2}[:h]\d{2}/)   // not a time either
  })

  it('shows the year for an older message', () => {
    expect(formatListDate(at(2024, 3, 4, 9, 30), now)).toMatch(/2024/)
  })

  it('returns an empty string for an unparseable date', () => {
    expect(formatListDate('not-a-date', now)).toBe('')
  })

  it('returns an empty string for an empty value', () => {
    expect(formatListDate('', now)).toBe('')
  })

  /* The day is a parameter and not a clock read in here, because a memoised row only redraws for a
     prop that changed: the same message reads as a time today and as a day tomorrow. */
  it('reads the same message as a time today and as a day the morning after', () => {
    const evening = at(2026, 7, 18, 18, 32)

    expect(formatListDate(evening, '2026-07-18')).toMatch(/\d{1,2}[:h]\d{2}/)
    expect(formatListDate(evening, '2026-07-19')).not.toMatch(/\d{1,2}[:h]\d{2}/)
    expect(formatListDate(evening, '2026-07-19')).toMatch(/18/)
  })

  // The dates used to pass `undefined`, so they followed the *browser* — a French interface on an
  // English browser printed English months.
  it('follows the given locale rather than the ambient one', () => {
    const june = '2026-06-15'
    expect(formatListDate(at(2026, 3, 4, 9, 0), june, 'fr')).toMatch(/mars/)
    expect(formatListDate(at(2026, 3, 4, 9, 0), june, 'en')).toMatch(/Mar/)
  })
})
