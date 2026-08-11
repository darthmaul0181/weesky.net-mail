import { describe, it, expect } from 'vitest'
import { formatReaderDate, formatReaderDateShort } from './formatReaderDate'

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

describe('formatReaderDateShort', () => {
  it('returns empty for an unparseable date', () => {
    expect(formatReaderDateShort('not a date')).toBe('')
  })

  it('spells no weekday and no month name, so the line cannot wrap on one', () => {
    const formatted = formatReaderDateShort('2026-07-09T17:52:04Z')

    expect(formatted).not.toMatch(/July|Thursday/)
    expect(formatted).toMatch(/\d/)
    expect(formatted).not.toMatch(/:\d\d:\d\d/)
  })

  // The shape rather than the day: field order, padding and the meridiem are what the locale
  // decides, and asserting the day itself would pin the test machine's timezone instead.
  // \s, not a literal space — ICU 72 puts a narrow no-break space before AM/PM.
  it('follows the given locale', () => {
    expect(formatReaderDateShort('2026-03-04T09:00:00Z', 'fr'))
      .toMatch(/^\d\d\/\d\d\/2026\s\d\d:\d\d$/)
    expect(formatReaderDateShort('2026-03-04T09:00:00Z', 'en'))
      .toMatch(/^\d{1,2}\/\d{1,2}\/26,\s\d{1,2}:\d\d\s[AP]M$/)
  })
})
