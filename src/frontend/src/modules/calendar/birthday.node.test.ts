import { describe, expect, it } from 'vitest'
import { ageOf, contactUrlOf, isBirthday } from './birthday'
import type { Occurrence } from './calendarTypes'

const base = { eventId: 'e', calendarId: 'c', uid: 'u', instanceId: '20260621', isOverride: false,
  isAllDay: true, isFloating: false, startDate: '2026-06-21', endDateExclusive: '2026-06-22',
  transparency: 'TRANSPARENT', hasAlarm: true } satisfies Occurrence

describe('birthday', () => {
  it('is a birthday only when the server stamped a contact', () => {
    expect(isBirthday(base)).toBe(false)
    expect(isBirthday({ ...base, contactId: 'k' })).toBe(true)
  })
  it('counts the age at the occurrence shown', () => {
    expect(ageOf({ ...base, contactId: 'k', birthYear: 1986 })).toBe(40)
    expect(ageOf({ ...base, contactId: 'k', startDate: '2027-06-21', birthYear: 1986 })).toBe(41)
  })
  it('has no age without a year', () => {
    expect(ageOf({ ...base, contactId: 'k' })).toBeNull()
  })
  it('opens the contact the way the contacts module selects one', () => {
    expect(contactUrlOf('k')).toBe('/contacts?id=k')
  })
})
