import { describe, expect, it } from 'vitest'
import {
  addDays, plainDateOf, todayIn, utcOfLocalMidnight, utcOfLocalTime,
} from './plainDate'

describe('addDays', () => {
  it('walks the calendar, not the clock', () => {
    expect(addDays('2026-09-14', 7)).toBe('2026-09-21')
    expect(addDays('2026-09-14', -1)).toBe('2026-09-13')
  })

  it('crosses a month, a year and a leap day', () => {
    expect(addDays('2026-08-31', 1)).toBe('2026-09-01')
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01')
    expect(addDays('2028-02-28', 1)).toBe('2028-02-29')
  })

  // The day Brussels loses an hour: a naive local-time arithmetic lands on the 25th twice.
  it('is unmoved by a daylight-saving transition', () => {
    expect(addDays('2026-10-25', 1)).toBe('2026-10-26')
    expect(addDays('2026-03-29', 1)).toBe('2026-03-30')
  })
})

describe('plainDateOf', () => {
  it('reads the day the zone is on, never the machine', () => {
    const instant = new Date('2026-09-14T23:30:00Z')
    expect(plainDateOf(instant, 'Europe/Brussels')).toBe('2026-09-15')
    expect(plainDateOf(instant, 'America/New_York')).toBe('2026-09-14')
    expect(plainDateOf(instant, 'UTC')).toBe('2026-09-14')
  })
})

describe('todayIn', () => {
  it('answers a plain date in the zone it is given', () => {
    expect(todayIn('Europe/Brussels')).toMatch(/^\d{4}-\d{2}-\d{2}$/)
  })
})

describe('utcOfLocalMidnight', () => {
  it('turns a local midnight into its instant', () => {
    expect(utcOfLocalMidnight('2026-09-13', 'Europe/Brussels').toISOString())
      .toBe('2026-09-12T22:00:00.000Z')
    expect(utcOfLocalMidnight('2026-01-13', 'Europe/Brussels').toISOString())
      .toBe('2026-01-12T23:00:00.000Z')
    expect(utcOfLocalMidnight('2026-09-13', 'UTC').toISOString()).toBe('2026-09-13T00:00:00.000Z')
  })

  // The offset is read at the instant asked for, not at an arbitrary one: the day the clocks
  // go back, midnight is still +02:00 and the next midnight is already +01:00.
  it('reads the offset the day itself is on', () => {
    expect(utcOfLocalMidnight('2026-10-25', 'Europe/Brussels').toISOString())
      .toBe('2026-10-24T22:00:00.000Z')
    expect(utcOfLocalMidnight('2026-10-26', 'Europe/Brussels').toISOString())
      .toBe('2026-10-25T23:00:00.000Z')
  })

  it('answers a zone west of Greenwich too', () => {
    expect(utcOfLocalMidnight('2026-09-14', 'America/New_York').toISOString())
      .toBe('2026-09-14T04:00:00.000Z')
  })
})

describe('utcOfLocalTime', () => {
  it('turns a wall clock into its instant', () => {
    expect(utcOfLocalTime('2026-09-16', 540, 'Europe/Brussels').toISOString())
      .toBe('2026-09-16T07:00:00.000Z')
  })

  // The day the clocks go back is 25 hours long, so midnight plus three hours is 02:00 and not
  // 03:00: the minutes have to be resolved with the offset rather than added to it afterwards.
  it('names the hour asked for on the day a transition lengthens', () => {
    expect(utcOfLocalTime('2026-10-25', 180, 'Europe/Brussels').toISOString())
      .toBe('2026-10-25T02:00:00.000Z')
    expect(utcOfLocalMidnight('2026-10-25', 'Europe/Brussels').getTime() + 180 * 60_000)
      .toBe(new Date('2026-10-25T01:00:00.000Z').getTime())
  })
})
