import { describe, expect, it } from 'vitest'
import {
  dateLocaleOf, dayNames, formatRangeTitle, formatTime, hourCycleOf, monthGrid, startOfWeek,
  weekdayNameOf, weekInfoOf, weekNumberOf, weekRulesOf,
} from './calendarLocale'

const ISO = { firstDay: 1, minimalDays: 4 } as const
const US = { firstDay: 7, minimalDays: 1 } as const

/** An engine carrying neither `getWeekInfo()` nor `weekInfo` — Node 20 has only the second, this
    machine's Node 24 has both, and the fallback table has to be reachable on either. */
const noWeekInfo = () => undefined

describe('weekInfoOf', () => {
  // The datum is spelled two ways: a method from V8 13 (Node 22+), the older accessor on Safari
  // and Node 20. Whichever this platform carries, the seam has to answer the same thing.
  it('reads whichever spelling the platform carries', () => {
    expect(weekInfoOf(new Intl.Locale('fr-BE'))?.firstDay).toBe(1)
    expect(weekInfoOf(new Intl.Locale('en-US'))?.firstDay).toBe(7)
  })
})

describe('weekRulesOf', () => {
  it('reads the region the browser is set to', () => {
    expect(weekRulesOf('fr-BE')).toEqual(ISO)
    expect(weekRulesOf('en-US')).toEqual(US)
  })

  // Through the seam, never by spying on `Intl.Locale`'s prototype: a spy would exercise the
  // table on the engine that carries the method and silently skip it on the one that does not.
  it('falls back to the region table when the engine answers neither spelling', () => {
    expect(weekRulesOf('fr-BE', noWeekInfo)).toEqual(ISO)
    expect(weekRulesOf('en-US', noWeekInfo)).toEqual(US)
    expect(weekRulesOf('en-CA', noWeekInfo)).toEqual(US)
    expect(weekRulesOf('nl', noWeekInfo)).toEqual(ISO)
  })

  it('answers something usable for a region tag it cannot parse', () => {
    expect(weekRulesOf('not a locale')).toEqual(ISO)
  })
})

describe('weekdayNameOf', () => {
  it("counts the weekdays from the module's own Monday", () => {
    expect(weekdayNameOf(0, 'long', 'en-GB')).toBe('Monday')
    expect(weekdayNameOf(6, 'long', 'en-GB')).toBe('Sunday')
  })
})

describe('dateLocaleOf', () => {
  // The language names the months, the region orders the fields: neither alone is the answer.
  it('grafts the browser region onto the interface language', () => {
    expect(dateLocaleOf('en', 'fr-BE')).toBe('en-BE')
    expect(dateLocaleOf('fr', 'en-US')).toBe('fr-US')
  })

  it('leaves English day-first when the browser names no region', () => {
    expect(dateLocaleOf('en', 'en')).toBe('en-GB')
    expect(dateLocaleOf('fr', 'fr')).toBe('fr')
  })

  it('answers something usable for a language tag it cannot parse', () => {
    expect(dateLocaleOf('en', 'not a locale')).toBe('en-GB')
  })
})

describe('hourCycleOf', () => {
  it('follows the region, not the language', () => {
    expect(hourCycleOf('en-US')).toBe('h12')
    expect(hourCycleOf('en-BE')).toBe('h23')
    expect(hourCycleOf('fr-BE')).toBe('h23')
  })
})

describe('startOfWeek', () => {
  it('walks back to the rules first day', () => {
    expect(startOfWeek('2026-09-16', ISO)).toBe('2026-09-14')
    expect(startOfWeek('2026-09-14', ISO)).toBe('2026-09-14')
    expect(startOfWeek('2026-09-16', US)).toBe('2026-09-13')
  })
})

describe('weekNumberOf', () => {
  it('counts ISO weeks under {1,4}', () => {
    expect(weekNumberOf('2026-09-16', ISO)).toBe(38)
    expect(weekNumberOf('2027-01-01', ISO)).toBe(53)
  })

  it('counts a Sunday-first, one-day year under {7,1}', () => {
    expect(weekNumberOf('2026-01-03', US)).toBe(1)
  })
})

describe('monthGrid', () => {
  it('always draws six rows of seven, starting on the rules first day', () => {
    const grid = monthGrid(2026, 9, ISO)

    expect(grid).toHaveLength(6)
    expect(grid.every(row => row.length === 7)).toBe(true)
    expect(grid[0][0]).toBe('2026-08-31')
    expect(grid[5][6]).toBe('2026-10-11')
  })

  it('starts the same month on a Sunday under {7,1}', () => {
    expect(monthGrid(2026, 9, US)[0][0]).toBe('2026-08-30')
  })
})

describe('formatRangeTitle', () => {
  // Never the dash itself: Intl chooses it, and pinning it makes the test the formatter's mirror.
  it('names a week as one range', () => {
    const title = formatRangeTitle('2026-09-14', '2026-09-20', 'week', 'en', 'fr-BE')

    expect(title).toContain('14')
    expect(title).toContain('20 September 2026')
  })

  it('names a single day in full', () => {
    expect(formatRangeTitle('2026-09-14', '2026-09-14', 'day', 'en', 'fr-BE'))
      .toBe('Monday, 14 September 2026')
  })

  it('names a month by its own name, not by the grid it spans', () => {
    expect(formatRangeTitle('2026-08-31', '2026-10-11', 'month', 'en', 'fr-BE'))
      .toBe('September 2026')
  })

  it('follows the interface language', () => {
    expect(formatRangeTitle('2026-08-31', '2026-10-11', 'month', 'fr', 'en-US'))
      .toBe('septembre 2026')
  })
})

describe('formatTime', () => {
  it('reads the instant in the zone it is given', () => {
    const instant = new Date('2026-09-14T07:00:00Z')

    expect(formatTime(instant, 'en', 'h23', 'Europe/Brussels', 'fr-BE')).toBe('09:00')
    expect(formatTime(instant, 'en', 'h23', 'UTC', 'fr-BE')).toBe('07:00')
  })

  it('spells a twelve-hour clock when the region asks for one', () => {
    const instant = new Date('2026-09-14T07:00:00Z')

    expect(formatTime(instant, 'en', 'h12', 'Europe/Brussels', 'en-US'))
      .toMatch(/^9:00\s?[ap]m$/i)
  })
})

describe('dayNames', () => {
  it('answers seven names in the week order the rules set', () => {
    expect(dayNames('en-BE', ISO, 'short')).toEqual(
      ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'])
    expect(dayNames('en-BE', US, 'short')[0]).toBe('Sun')
    expect(dayNames('en-BE', ISO, 'narrow')).toHaveLength(7)
  })
})
