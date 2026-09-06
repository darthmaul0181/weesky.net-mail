import { describe, expect, it } from 'vitest'
import type { EventDetail, EventWrite, Occurrence } from './calendarTypes'
import {
  allowedScopes, formOf, isRecurring, movedBody, movedOccurrence, newEventForm, updateBodyOf,
  validate, writeOf,
} from './eventForm'

const TZ = 'Europe/Brussels'

function fields(overrides: Partial<EventWrite> = {}): EventWrite {
  return {
    calendarId: 'c1', summary: 'Stand-up', isAllDay: false,
    start: '2026-09-14T08:00:00', end: '2026-09-14T09:00:00', timeZone: TZ,
    reminderMinutesBefore: [15], availability: 'Busy', visibility: 'Default',
    ...overrides,
  }
}

function detailOf(overrides: Partial<EventDetail> = {}): EventDetail {
  return {
    id: 'e1', calendarId: 'c1', uid: 'u1', icsHash: 'hash-1', fields: fields(),
    attendees: [], repeatIsExact: true, foreignAlarms: [],
    ...overrides,
  }
}

function occurrenceOf(overrides: Partial<Occurrence> = {}): Occurrence {
  return {
    eventId: 'e1', calendarId: 'c1', uid: 'u1', instanceId: '20260914T080000', isOverride: false,
    isAllDay: false, isFloating: false, timeZone: TZ,
    startUtc: '2026-09-14T06:00:00Z', endUtc: '2026-09-14T07:00:00Z',
    transparency: 'OPAQUE', hasAlarm: false,
    ...overrides,
  }
}

const allDayFields = fields({
  isAllDay: true, start: undefined, end: undefined, timeZone: undefined,
  startDate: '2026-09-14', endDateInclusive: '2026-09-16',
})

describe('newEventForm', () => {
  it('reads the slot the user dragged, in the zone the screen is on', () => {
    const form = newEventForm(
      new Date('2026-09-14T07:00:00Z'), new Date('2026-09-14T08:30:00Z'), false, 'c1', TZ)

    expect(form).toMatchObject({
      calendarId: 'c1', title: '', isAllDay: false, timeZone: TZ,
      startDate: '2026-09-14', startTime: '09:00', endDate: '2026-09-14', endTime: '10:30',
      availability: 'Busy', visibility: 'Default', keepRepeat: false,
    })
    expect(form.repeat).toEqual({ kind: 'never' })
  })

  // A day off does not block a free/busy: the clients that write all-day events write them free.
  it('is born free when it is a whole day', () => {
    const form = newEventForm(
      new Date('2026-09-14T07:00:00Z'), new Date('2026-09-15T07:00:00Z'), true, 'c1', TZ)

    expect(form.isAllDay).toBe(true)
    expect(form.availability).toBe('Free')
    expect(form.startDate).toBe('2026-09-14')
    expect(form.endDate).toBe('2026-09-15')
  })

  // Décision 13: a dated event opens on the fifteen minutes every client defaults to; a whole day
  // has no hour for a distance to count from, so it is born silent.
  it('gives a dated event the default reminder and a whole day none', () => {
    const at = (iso: string) => new Date(iso)
    const dated = newEventForm(
      at('2026-09-14T07:00:00Z'), at('2026-09-14T08:00:00Z'), false, 'c1', TZ)
    const whole = newEventForm(
      at('2026-09-14T07:00:00Z'), at('2026-09-15T07:00:00Z'), true, 'c1', TZ)

    expect(dated.reminders).toEqual([15])
    expect(whole.reminders).toEqual([])
  })
})

describe('formOf', () => {
  it('gives a floating event the browser zone, since it carries none of its own', () => {
    const form = formOf(
      detailOf({ fields: fields({ timeZone: undefined }) }), null, 'Europe/Brussels')

    expect(form.timeZone).toBe('Europe/Brussels')
    expect(form.startDate).toBe('2026-09-14')
    expect(form.startTime).toBe('08:00')
  })

  it('keeps the zone a dated event was written in', () => {
    const form = formOf(
      detailOf({ fields: fields({ timeZone: 'America/New_York' }) }), null, 'Europe/Brussels')

    expect(form.timeZone).toBe('America/New_York')
  })

  // The occurrence's instant is read in the event's own zone, never in the browser's: an event
  // written in New York and opened from Brussels must still say the hour its author chose.
  it('reads an occurrence in the event zone, not the browser one', () => {
    const detail = detailOf({ fields: fields({ timeZone: 'America/New_York' }) })
    const form = formOf(detail, occurrenceOf({
      startUtc: '2026-09-14T13:00:00Z', endUtc: '2026-09-14T14:00:00Z',
    }), 'Europe/Brussels')

    expect(form.startDate).toBe('2026-09-14')
    expect(form.startTime).toBe('09:00')
    expect(form.endTime).toBe('10:00')
  })

  it('carries the whole-day dates and the foreign alarms through', () => {
    const form = formOf(
      detailOf({ fields: allDayFields, foreignAlarms: ['EMAIL, 1 day before'] }), null, TZ)

    expect(form.isAllDay).toBe(true)
    expect(form.startDate).toBe('2026-09-14')
    expect(form.endDate).toBe('2026-09-16')
    expect(form.foreignAlarms).toEqual(['EMAIL, 1 day before'])
  })

  // The master of a weekly series sits weeks away from the instance being opened: seeding from
  // its dates is how a `This` save rewrites the fourth Monday onto the first.
  it('seeds a whole-day occurrence from its own days, never the master’s', () => {
    const detail = detailOf({
      fields: fields({
        ...allDayFields, repeat: { frequency: 'WEEKLY', interval: 1, byDay: ['MO'], end: 'Never' },
      }),
    })
    const instance = occurrenceOf({
      isAllDay: true, instanceId: '20260928', startDate: '2026-09-28',
      endDateExclusive: '2026-10-01', startUtc: undefined, endUtc: undefined,
    })
    const form = formOf(detail, instance, TZ)

    expect(form.startDate).toBe('2026-09-28')
    expect(form.endDate).toBe('2026-09-30')
    expect(updateBodyOf(form, detail, instance, 'This'))
      .toMatchObject({ startDate: '2026-09-28', endDateInclusive: '2026-09-30' })
  })

  it('falls back to the master when the occurrence carries no days of its own', () => {
    const form = formOf(detailOf({ fields: allDayFields }), occurrenceOf({ isAllDay: true }), TZ)

    expect(form.startDate).toBe('2026-09-14')
    expect(form.endDate).toBe('2026-09-16')
  })

  it('names a plain rule by its own word and keeps a rich one whole', () => {
    const weekly = formOf(detailOf({
      fields: fields({ repeat: { frequency: 'WEEKLY', interval: 1, byDay: [], end: 'Never' } }),
    }), null, TZ)
    const rule = {
      frequency: 'MONTHLY', interval: 2, byDay: [], bySetPos: -1, bySetPosDay: 'FR',
      end: 'Never' as const,
    }
    const custom = formOf(detailOf({ fields: fields({ repeat: rule }) }), null, TZ)

    expect(weekly.repeat).toEqual({ kind: 'weekly' })
    expect(custom.repeat).toEqual({ kind: 'custom', rule })
  })

  // The screen never showed the stored rule, so the picker must not be allowed to rewrite it.
  it('arms keepRepeat when the stored rule is more than the editor can show', () => {
    expect(formOf(detailOf({ repeatIsExact: false }), null, TZ).keepRepeat).toBe(true)
    expect(formOf(detailOf({ repeatIsExact: true }), null, TZ).keepRepeat).toBe(false)
  })
})

describe('writeOf', () => {
  it('writes a dated event with its wall clock and its zone', () => {
    const write = writeOf(formOf(detailOf(), null, TZ))

    expect(write).toMatchObject({
      calendarId: 'c1', summary: 'Stand-up', isAllDay: false,
      start: '2026-09-14T08:00:00', end: '2026-09-14T09:00:00', timeZone: TZ,
      reminderMinutesBefore: [15],
    })
    expect(write.startDate).toBeUndefined()
  })

  it('writes a whole day with dates alone, the last one inclusive', () => {
    const write = writeOf(formOf(detailOf({ fields: allDayFields }), null, TZ))

    expect(write).toMatchObject({
      isAllDay: true, startDate: '2026-09-14', endDateInclusive: '2026-09-16',
    })
    expect(write.start).toBeUndefined()
    expect(write.end).toBeUndefined()
    expect(write.timeZone).toBeUndefined()
  })

  // The API refuses the pair, and a creation has no stored rule to keep in the first place.
  it('never sends keepRepeat on a creation', () => {
    const form = formOf(detailOf({ repeatIsExact: false }), null, TZ)

    expect(writeOf(form).keepRepeat).toBeUndefined()
  })

  it('spells a plain repeat choice as a rule', () => {
    const form = { ...formOf(detailOf(), null, TZ), repeat: { kind: 'daily' as const } }

    expect(writeOf(form).repeat)
      .toEqual({ frequency: 'DAILY', interval: 1, byDay: [], end: 'Never' })
  })
})

describe('updateBodyOf', () => {
  it('names the instance it edits and the version it read', () => {
    const detail = detailOf()
    const body = updateBodyOf(formOf(detail, occurrenceOf(), TZ), detail, occurrenceOf(), 'This')

    expect(body.scope).toBe('This')
    expect(body.instanceId).toBe('20260914T080000')
    expect(body.ifHash).toBe('hash-1')
  })

  it('names no instance when the edit reaches the whole series', () => {
    const detail = detailOf()
    const body = updateBodyOf(formOf(detail, occurrenceOf(), TZ), detail, occurrenceOf(), 'All')

    expect(body.instanceId).toBeUndefined()
  })

  // The two are refused together: keeping the stored rule means not restating it.
  it('sends keepRepeat instead of a rule the editor never showed', () => {
    const detail = detailOf({
      repeatIsExact: false,
      fields: fields({ repeat: { frequency: 'WEEKLY', interval: 3, byDay: ['MO'], end: 'Never' } }),
    })
    const body = updateBodyOf(formOf(detail, null, TZ), detail, null, 'All')

    expect(body.keepRepeat).toBe(true)
    expect(body.repeat).toBeUndefined()
  })
})

describe('allowedScopes', () => {
  const repeating = detailOf({
    fields: fields({ repeat: { frequency: 'WEEKLY', interval: 1, byDay: [], end: 'Never' } }),
  })

  it('offers the three scopes of an occurrence of a series', () => {
    expect(allowedScopes(formOf(repeating, occurrenceOf(), TZ), repeating, occurrenceOf()))
      .toEqual(['This', 'ThisAndFollowing', 'All'])
  })

  // A narrow scope writes an exception into the series; moving the series to another calendar
  // moves the whole file, and the two cannot be asked for at once.
  it('offers only the whole series once the calendar has changed', () => {
    const form = { ...formOf(repeating, occurrenceOf(), TZ), calendarId: 'c2' }

    expect(allowedScopes(form, repeating, occurrenceOf())).toEqual(['All'])
  })

  it('offers only the whole series when there is no series at all', () => {
    const detail = detailOf()

    expect(allowedScopes(formOf(detail, null, TZ), detail, null)).toEqual(['All'])
  })
})

describe('isRecurring', () => {
  // Décision 8: the RECURRENCE-ID of the occurrence opened, and nothing else. A repeat the picker
  // has just added has no other occurrence to reach, so there is nothing to ask about.
  it('reads the occurrence, never the repeat the form carries', () => {
    expect(isRecurring(occurrenceOf())).toBe(true)
    expect(isRecurring(occurrenceOf({ instanceId: '' }))).toBe(false)
    expect(isRecurring(null)).toBe(false)
  })

  it('leaves a repeat added to a lone event on the whole series', () => {
    const detail = detailOf()
    const form = { ...formOf(detail, null, TZ), repeat: { kind: 'monthly' as const } }

    expect(allowedScopes(form, detail, null)).toEqual(['All'])
  })
})

describe('movedBody', () => {
  it('shifts the instance by the minutes dragged, in the event zone', () => {
    const detail = detailOf()
    const body = movedBody(detail, occurrenceOf(), 90, null, 'This')

    expect(body.start).toBe('2026-09-14T09:30:00')
    expect(body.end).toBe('2026-09-14T10:30:00')
    expect(body.instanceId).toBe('20260914T080000')
    expect(body.ifHash).toBe('hash-1')
  })

  it('takes the new duration when a resize gives one', () => {
    const body = movedBody(detailOf(), occurrenceOf(), 0, 30, 'This')

    expect(body.start).toBe('2026-09-14T08:00:00')
    expect(body.end).toBe('2026-09-14T08:30:00')
  })

  it('moves a whole day by whole days', () => {
    const detail = detailOf({
      fields: fields({
        isAllDay: true, start: undefined, end: undefined, timeZone: undefined,
        startDate: '2026-09-14', endDateInclusive: '2026-09-15',
      }),
    })
    const body = movedBody(detail, occurrenceOf({
      isAllDay: true, startDate: '2026-09-14', endDateExclusive: '2026-09-16',
    }), 1440, null, 'All')

    expect(body.startDate).toBe('2026-09-15')
    expect(body.endDateInclusive).toBe('2026-09-16')
  })

  // The master sits weeks away from the instance being dragged: shifting its dates would write
  // the change onto the wrong day while the block on the screen sat on the right one.
  it('moves the dragged instance of a whole-day series, never the master', () => {
    const detail = detailOf({
      fields: fields({
        ...allDayFields, repeat: { frequency: 'WEEKLY', interval: 1, byDay: ['MO'], end: 'Never' },
      }),
    })
    const third = occurrenceOf({
      isAllDay: true, startUtc: undefined, endUtc: undefined, instanceId: '2026-09-28',
      startDate: '2026-09-28', endDateExclusive: '2026-10-01',
    })
    const body = movedBody(detail, third, 1440, null, 'This')

    expect(body.startDate).toBe('2026-09-29')
    expect(body.endDateInclusive).toBe('2026-10-01')
    expect(body.instanceId).toBe('2026-09-28')
  })

  it('keeps a rule the editor never showed rather than restating it', () => {
    const detail = detailOf({
      repeatIsExact: false,
      fields: fields({ repeat: { frequency: 'WEEKLY', interval: 3, byDay: ['MO'], end: 'Never' } }),
    })
    const body = movedBody(detail, occurrenceOf(), 60, null, 'All')

    expect(body.keepRepeat).toBe(true)
    expect(body.repeat).toBeUndefined()
  })

  // The body is a spread of what the API sent back: the pair the API refuses must be impossible
  // to build here whatever that answer holds.
  it('never lets keepRepeat out beside a rule, even when the API sent both', () => {
    const detail = detailOf({
      repeatIsExact: true,
      fields: fields({
        keepRepeat: true,
        repeat: { frequency: 'WEEKLY', interval: 1, byDay: [], end: 'Never' },
      }),
    })
    const body = movedBody(detail, occurrenceOf(), 60, null, 'All')

    expect(body.keepRepeat).toBeUndefined()
    expect(body.repeat).toBeDefined()
  })

  // A drag is a distance in grid pixels, so an hour dragged is an hour of clock face. Applied to
  // the instant instead, the block would come back onto 02:30 the morning the clocks go back.
  it('shifts the wall clock across a daylight-saving change, not the instant', () => {
    const detail = detailOf({ fields: fields({ timeZone: TZ }) })
    const body = movedBody(detail, occurrenceOf({
      startUtc: '2026-10-24T23:30:00Z', endUtc: '2026-10-25T00:30:00Z',
    }), 60, null, 'This')

    expect(body.start).toBe('2026-10-25T02:30:00')
    expect(body.end).toBe('2026-10-25T03:30:00')
  })
})

describe('validate', () => {
  it('accepts an ordinary event', () => {
    expect(validate(formOf(detailOf(), null, TZ))).toBeNull()
  })

  it('refuses an end before its start', () => {
    const form = { ...formOf(detailOf(), null, TZ), endTime: '07:00' }

    expect(validate(form)).toBe('editor.endBeforeStart')
  })

  // The brief refuses an end *before* its start, and the key says exactly that. An event of no
  // duration is a different thing, and `editor.endBeforeStart` would be lying about it.
  it('accepts an event that ends when it starts', () => {
    const form = { ...formOf(detailOf(), null, TZ), endTime: '08:00' }

    expect(validate(form)).toBeNull()
  })

  it('refuses a whole day ending before it starts', () => {
    const form = formOf(detailOf({
      fields: fields({
        isAllDay: true, start: undefined, end: undefined, timeZone: undefined,
        startDate: '2026-09-16', endDateInclusive: '2026-09-14',
      }),
    }), null, TZ)

    expect(validate(form)).toBe('editor.endBeforeStart')
  })

  it('accepts a whole day that starts and ends the same day', () => {
    const form = formOf(detailOf({
      fields: fields({
        isAllDay: true, start: undefined, end: undefined, timeZone: undefined,
        startDate: '2026-09-14', endDateInclusive: '2026-09-14',
      }),
    }), null, TZ)

    expect(validate(form)).toBeNull()
  })
})

describe('movedOccurrence', () => {
  // The block is drawn from the occurrence, so the cache has to hold the same hours the body
  // sends: the shifted wall clock is re-posed in the event's own zone, never the machine's.
  it('re-poses a dated occurrence in the event zone', () => {
    const moved = movedOccurrence(detailOf(), occurrenceOf(), 90, null)

    expect(moved.startUtc).toBe('2026-09-14T07:30:00.000Z')
    expect(moved.endUtc).toBe('2026-09-14T08:30:00.000Z')
  })

  it('takes the new duration a resize gives, leaving the start where it was', () => {
    const moved = movedOccurrence(detailOf(), occurrenceOf(), 0, 30)

    expect(moved.startUtc).toBe('2026-09-14T06:00:00.000Z')
    expect(moved.endUtc).toBe('2026-09-14T06:30:00.000Z')
  })

  // A floating instance belongs to no zone at all: its clock is read as written and written back.
  it('leaves a floating occurrence floating', () => {
    const one = occurrenceOf({
      isFloating: true, startUtc: undefined, endUtc: undefined, timeZone: undefined,
      localStart: '2026-09-14T08:00:00', localEnd: '2026-09-14T09:00:00',
    })
    const moved = movedOccurrence(detailOf(), one, 60, null)

    expect(moved.localStart).toBe('2026-09-14T09:00:00')
    expect(moved.localEnd).toBe('2026-09-14T10:00:00')
    expect(moved.startUtc).toBeUndefined()
  })

  it('moves a whole day by whole days, keeping the morning after as its end', () => {
    const detail = detailOf({ fields: allDayFields })
    const moved = movedOccurrence(detail, occurrenceOf({
      isAllDay: true, startUtc: undefined, endUtc: undefined,
      startDate: '2026-09-14', endDateExclusive: '2026-09-16',
    }), 1440, null)

    expect(moved.startDate).toBe('2026-09-15')
    expect(moved.endDateExclusive).toBe('2026-09-17')
  })

  // 25 October 2026 is the morning the clocks go back in Brussels: a block dragged an hour on
  // the grid is an hour of clock face, which is two hours of instant across that transition.
  it('counts the drag on the clock face, not on the instant', () => {
    const detail = detailOf({ fields: fields({ start: '2026-10-25T01:00:00', end: '2026-10-25T01:30:00' }) })
    const one = occurrenceOf({
      startUtc: '2026-10-24T23:00:00Z', endUtc: '2026-10-24T23:30:00Z',
      instanceId: '20261025T010000',
    })
    const moved = movedOccurrence(detail, one, 120, null)

    expect(moved.startUtc).toBe('2026-10-25T02:00:00.000Z')
  })
})
