import type { TFunction } from 'i18next'
import { describe, expect, it } from 'vitest'
import { answersOf, dotClassOf, guestAnswerOf } from './attendeeStatus'

const t = ((key: string) => key) as unknown as TFunction<'calendar'>

describe('guestAnswerOf', () => {
  it('words a guest’s answer, pending by default', () => {
    expect(guestAnswerOf('ACCEPTED', t)).toBe('guestAnswer.accepted')
    expect(guestAnswerOf('tentative', t)).toBe('guestAnswer.tentative')
    expect(guestAnswerOf('DECLINED', t)).toBe('guestAnswer.declined')
    expect(guestAnswerOf('NEEDS-ACTION', t)).toBe('guestAnswer.pending')
    expect(guestAnswerOf(undefined, t)).toBe('guestAnswer.pending')
    expect(dotClassOf('DELEGATED')).toBe('is-pending')
    expect(dotClassOf('accepted')).toBe('is-accepted')
  })
})

describe('answersOf', () => {
  // The list follows the chips; the answer is the stored master guest's, by address.
  it('answers each guest of the form, in its order, from the stored master guest', () => {
    const stored = [
      { email: 'alice@weesky.be', isOrganizer: true, partStat: 'ACCEPTED' },
      { email: 'Marc@Example.org', name: 'Marc', isOrganizer: false, partStat: 'ACCEPTED' },
      { email: 'julie@example.net', isOrganizer: false, partStat: 'DECLINED', recurrenceId: '20261019T100000' },
    ]
    const guests = [
      { email: 'julie@example.net', name: 'Julie' }, { email: 'marc@example.org' },
      { email: 'marc@example.org' }, { email: 'alice@weesky.be' },
    ]

    expect(answersOf(guests, stored)).toEqual([
      { email: 'julie@example.net', name: 'Julie', isOrganizer: false },
      { email: 'marc@example.org', name: 'Marc', isOrganizer: false, partStat: 'ACCEPTED' },
      { email: 'marc@example.org', name: 'Marc', isOrganizer: false, partStat: 'ACCEPTED' },
      { email: 'alice@weesky.be', isOrganizer: false },
    ])
  })
})
