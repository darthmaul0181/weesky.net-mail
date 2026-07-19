import { describe, it, expect } from 'vitest'
import { formatReaderDate } from './formatReaderDate'

describe('formatReaderDate', () => {
  it('returns empty for an unparseable date', () => {
    expect(formatReaderDate('not a date')).toBe('')
  })

  // Asserting intent, not a locale's exact wording: the runtime's locale decides the words
  // and the separators, and pinning those would make the test a fixture of the test machine.
  it('spells out the day, month and year', () => {
    const formatted = formatReaderDate('2026-07-09T17:52:04Z')

    expect(formatted).toMatch(/2026/)
    expect(formatted).toMatch(/9/)
    // A long month name, not a two-digit number as the raw toLocaleString gave.
    expect(formatted).not.toMatch(/\b07\b/)
  })

  it('keeps the time but drops the seconds', () => {
    const formatted = formatReaderDate('2026-07-09T17:52:04Z')
    const time = new Date('2026-07-09T17:52:04Z')
      .toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })

    expect(formatted).toContain(time)
    expect(formatted).not.toMatch(/:\d\d:\d\d/)
  })
})
