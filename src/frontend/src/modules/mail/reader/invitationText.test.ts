import { describe, it, expect } from 'vitest'
import type { TFunction } from 'i18next'
import { cardStateOf, noActionKey, whenOf } from './invitationText'
import type { MailInvitation } from '../api/mailTypes'

const base: MailInvitation = {
  method: 'Request', uid: 'u', sequence: 0, isAllDay: false, repeats: false, attendees: [],
  addressedTo: 'alice@weesky.be', filePartStat: 'NEEDS-ACTION', inCalendar: 'Absent',
  occurrenceOnly: false, part: '2', unreadable: false,
  summary: 'Dîner chez Marc', start: '2026-10-10T17:30:00Z', end: '2026-10-10T20:30:00Z',
}
const t = ((key: string) => key) as unknown as TFunction<'mail'>

describe('cardStateOf', () => {
  it('names the seven states of the mock-up', () => {
    expect(cardStateOf(base)).toBe('invite')
    expect(cardStateOf({ ...base, inCalendar: 'Current', savedPartStat: 'ACCEPTED' })).toBe('answered')
    expect(cardStateOf({ ...base, addressedTo: undefined, filePartStat: undefined })).toBe('forwarded')
    expect(cardStateOf({ ...base, inCalendar: 'Outdated', savedPartStat: 'ACCEPTED' })).toBe('updated')
    expect(cardStateOf({ ...base, method: 'Cancel', inCalendar: 'Cancelled' })).toBe('cancelled')
    expect(cardStateOf({ ...base, method: 'Cancel', inCalendar: 'Absent' })).toBe('noAction')
    expect(cardStateOf({ ...base, inCalendar: 'Newer' })).toBe('noAction')
    expect(cardStateOf({ ...base, occurrenceOnly: true })).toBe('noAction')
    expect(cardStateOf({ ...base, unreadable: true, reason: 'x' })).toBe('unreadable')
  })

  it('a forwarded invitation already in the calendar is answered-less current', () => {
    expect(cardStateOf({ ...base, addressedTo: undefined, inCalendar: 'Current' })).toBe('answered')
  })
})

describe('noActionKey', () => {
  it('picks the sentence', () => {
    expect(noActionKey({ ...base, occurrenceOnly: true })).toBe('occurrenceOnly')
    expect(noActionKey({ ...base, inCalendar: 'Newer' })).toBe('newer')
    expect(noActionKey({ ...base, method: 'Cancel', inCalendar: 'Absent' })).toBe('cancelAbsent')
  })
})

describe('whenOf', () => {
  it('writes the date in the browser zone, in words', () => {
    expect(whenOf(base, 'Europe/Brussels', 'fr', 'fr-BE', 'h23', t))
      .toBe('samedi 10 octobre 2026, 19:30 – 22:30')
    expect(whenOf(base, 'America/New_York', 'en', 'en-GB', 'h23', t))
      .toBe('Saturday, 10 October 2026, 13:30 – 16:30')
  })

  it('a whole day is its date and the all-day word', () => {
    const day = { ...base, isAllDay: true, start: undefined, end: undefined, startDate: '2026-11-01', endDateExclusive: '2026-11-02' }
    expect(whenOf(day, 'Europe/Brussels', 'fr', 'fr-BE', 'h23', t))
      .toBe('dimanche 1 novembre 2026 · reader.invitation.allDay')
  })

  it('several whole days are a range', () => {
    const days = { ...base, isAllDay: true, start: undefined, end: undefined, startDate: '2026-11-01', endDateExclusive: '2026-11-03' }
    const range = whenOf(days, 'Europe/Brussels', 'en', 'en-GB', 'h23', t)

    // Both ends named, and the year carried — the claim. Not the whole string: the dash, which
    // end keeps the shared year, and the thin space U+2009 the formatter sets inside the elided
    // half are all Intl's, and pinning them would make this test its mirror rather than a guard.
    expect(range).toMatch(/Sunday\s1\sNovember/u)
    expect(range).toMatch(/Monday\s2\sNovember\s2026/u)
  })

  it('a day crossing midnight names both days', () => {
    const late = { ...base, start: '2026-10-10T21:30:00Z', end: '2026-10-11T00:30:00Z' }
    expect(whenOf(late, 'Europe/Brussels', 'fr', 'fr-BE', 'h23', t))
      .toBe('samedi 10 octobre 2026, 23:30 – dimanche 11 octobre 2026, 02:30')
  })

  it('a start with no end is the day and one clock', () => {
    const open = { ...base, end: undefined }
    expect(whenOf(open, 'Europe/Brussels', 'fr', 'fr-BE', 'h23', t)).toBe('samedi 10 octobre 2026, 19:30')
  })

  it('says nothing at all when the block carried no date', () => {
    expect(whenOf({ ...base, start: undefined, end: undefined }, 'Europe/Brussels', 'fr', 'fr-BE', 'h23', t)).toBe('')
  })
})
