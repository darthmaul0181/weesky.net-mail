import { describe, it, expect } from 'vitest'
import { formatReaderDate } from './formatReaderDate'

describe('formatReaderDate', () => {
  it('returns empty for an unparseable date', () => {
    expect(formatReaderDate('not a date')).toBe('')
  })

  // The default locale is now the active i18next one ('en' under test-setup), not the host's —
  // so, unlike before, pinning the English wording here is no longer a fixture of the test machine.
  it('spells out the day, month and year', () => {
    const formatted = formatReaderDate('2026-07-09T17:52:04Z')

    expect(formatted).toMatch(/2026/)
    expect(formatted).toMatch(/9/)
    // A long month name, not a two-digit number as the raw toLocaleString gave.
    expect(formatted).toMatch(/July/)
  })

  it('keeps the time but drops the seconds', () => {
    const formatted = formatReaderDate('2026-07-09T17:52:04Z')
    const time = new Date('2026-07-09T17:52:04Z')
      .toLocaleTimeString('en', { hour: '2-digit', minute: '2-digit' })

    expect(formatted).toContain(time)
    expect(formatted).not.toMatch(/:\d\d:\d\d/)
  })

  it('follows the given locale', () => {
    expect(formatReaderDate('2026-03-04T09:00:00Z', 'fr')).toMatch(/mercredi/)
    expect(formatReaderDate('2026-03-04T09:00:00Z', 'en')).toMatch(/Wednesday/)
  })
})
