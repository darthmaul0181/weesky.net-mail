import { describe, expect, it } from 'vitest'
import type { Occurrence } from './calendarTypes'
import { dayOf, itemsByDay, placeAll, placeOccurrence } from './multiDay'

const TZ = 'Europe/Brussels'
const WEEK = ['2026-09-14', '2026-09-15', '2026-09-16', '2026-09-17', '2026-09-18',
  '2026-09-19', '2026-09-20']

function occurrence(overrides: Partial<Occurrence>): Occurrence {
  return {
    eventId: 'e1', calendarId: 'c1', uid: 'u1', instanceId: '', isOverride: false,
    isAllDay: false, isFloating: false, transparency: 'OPAQUE', hasAlarm: false,
    ...overrides,
  }
}

describe('placeOccurrence', () => {
  it('cuts an evening that runs past midnight into one slice per day', () => {
    const placed = placeOccurrence(occurrence({
      timeZone: TZ, startUtc: '2026-09-14T20:00:00Z', endUtc: '2026-09-15T00:00:00Z',
    }), TZ, WEEK)

    expect(placed).toEqual({
      kind: 'slices',
      slices: [
        { day: '2026-09-14', startMinute: 1320, endMinute: 1440 },
        { day: '2026-09-15', startMinute: 0, endMinute: 120 },
      ],
    })
  })

  it('leaves no empty slice on a day the event only touches at midnight', () => {
    const placed = placeOccurrence(occurrence({
      timeZone: TZ, startUtc: '2026-09-14T20:00:00Z', endUtc: '2026-09-14T22:00:00Z',
    }), TZ, WEEK)

    expect(placed).toEqual({
      kind: 'slices',
      slices: [{ day: '2026-09-14', startMinute: 1320, endMinute: 1440 }],
    })
  })

  // A full day of grid column for one event reads as a wall of colour; the band says one event.
  it('bands a dated event that runs a day or more', () => {
    const placed = placeOccurrence(occurrence({
      timeZone: TZ, startUtc: '2026-09-14T07:00:00Z', endUtc: '2026-09-16T09:00:00Z',
    }), TZ, WEEK)

    expect(placed).toEqual({ kind: 'band', from: '2026-09-14', to: '2026-09-16' })
  })

  it('slices an event a minute short of the day it would take', () => {
    const placed = placeOccurrence(occurrence({
      timeZone: TZ, startUtc: '2026-09-14T21:00:00Z', endUtc: '2026-09-15T20:59:00Z',
    }), TZ, WEEK)

    expect(placed).toEqual({
      kind: 'slices',
      slices: [
        { day: '2026-09-14', startMinute: 1380, endMinute: 1440 },
        { day: '2026-09-15', startMinute: 0, endMinute: 1379 },
      ],
    })
  })

  it('bands the same event once it reaches the whole day', () => {
    const placed = placeOccurrence(occurrence({
      timeZone: TZ, startUtc: '2026-09-14T21:00:00Z', endUtc: '2026-09-15T21:00:00Z',
    }), TZ, WEEK)

    expect(placed).toEqual({ kind: 'band', from: '2026-09-14', to: '2026-09-15' })
  })

  it('bands an all-day event, with no time to show', () => {
    const placed = placeOccurrence(occurrence({
      isAllDay: true, startDate: '2026-09-15', endDateExclusive: '2026-09-18',
    }), TZ, WEEK)

    expect(placed).toEqual({ kind: 'band', from: '2026-09-15', to: '2026-09-17' })
  })

  it('bands a single all-day event too', () => {
    const placed = placeOccurrence(occurrence({
      isAllDay: true, startDate: '2026-09-15', endDateExclusive: '2026-09-16',
    }), TZ, WEEK)

    expect(placed).toEqual({ kind: 'band', from: '2026-09-15', to: '2026-09-15' })
  })

  // A floating instant belongs to no zone: its wall clock is read as written, never converted.
  it('reads a floating occurrence off its own wall clock', () => {
    const placed = placeOccurrence(occurrence({
      isFloating: true, localStart: '2026-09-16T09:00:00', localEnd: '2026-09-16T10:30:00',
    }), 'America/New_York', WEEK)

    expect(placed).toEqual({
      kind: 'slices',
      slices: [{ day: '2026-09-16', startMinute: 540, endMinute: 630 }],
    })
  })

  it('cuts what falls outside the visible days rather than drawing it', () => {
    const placed = placeOccurrence(occurrence({
      timeZone: TZ, startUtc: '2026-09-13T20:00:00Z', endUtc: '2026-09-14T00:00:00Z',
    }), TZ, WEEK)

    expect(placed).toEqual({
      kind: 'slices',
      slices: [{ day: '2026-09-14', startMinute: 0, endMinute: 120 }],
    })
  })

  it('clamps a band to the visible days', () => {
    const placed = placeOccurrence(occurrence({
      isAllDay: true, startDate: '2026-09-12', endDateExclusive: '2026-09-30',
    }), TZ, WEEK)

    expect(placed).toEqual({ kind: 'band', from: '2026-09-14', to: '2026-09-20' })
  })

  it('renders nothing at all when nothing falls in the visible days', () => {
    const placed = placeOccurrence(occurrence({
      isAllDay: true, startDate: '2026-10-01', endDateExclusive: '2026-10-02',
    }), TZ, WEEK)

    expect(placed).toEqual({ kind: 'slices', slices: [] })
  })
})
const ALL_DAY = occurrence({ isAllDay: true, startDate: '2026-09-16', endDateExclusive: '2026-09-18' })
const DATED = occurrence({ startUtc: '2026-09-16T07:00:00Z', endUtc: '2026-09-16T08:00:00Z' })
const PAIR = ['2026-09-16', '2026-09-17']

describe('dayOf', () => {
  it('files an all-day occurrence under the day it starts', () => {
    expect(dayOf(ALL_DAY, TZ)).toBe('2026-09-16')
  })

  it('files a dated occurrence under the day its wall clock starts', () => {
    expect(dayOf(occurrence({
      startUtc: '2026-09-16T21:00:00Z', endUtc: '2026-09-16T22:00:00Z',
    }), TZ)).toBe('2026-09-16')
  })
})

describe('placeAll', () => {
  it('splits a screenful into the band and the columns', () => {
    const placed = placeAll([ALL_DAY, DATED], TZ, PAIR)

    expect(placed.bands).toHaveLength(1)
    expect(placed.slices.get('2026-09-16')).toHaveLength(1)
    expect(placed.slices.get('2026-09-17')).toBeUndefined()
  })
})

describe('itemsByDay', () => {
  it('repeats a band on every day it covers and puts it ahead of the hours', () => {
    const items = itemsByDay(placeAll([DATED, ALL_DAY], TZ, PAIR), PAIR)

    expect(items.get('2026-09-16')?.map(one => one.band)).toEqual([true, false])
    expect(items.get('2026-09-17')?.map(one => one.band)).toEqual([true])
  })

  // The two slices are the hour grid's own rendering: a month cell and a list name the evening
  // once, on the day it starts, the way Google does.
  it('files an evening crossing midnight under the day it starts on', () => {
    const items = itemsByDay(placeAll([occurrence({
      startUtc: '2026-09-16T20:00:00Z', endUtc: '2026-09-17T00:00:00Z',
    })], TZ, PAIR), PAIR)

    expect(items.get('2026-09-16')).toHaveLength(1)
    expect(items.get('2026-09-17')).toBeUndefined()
  })
})
