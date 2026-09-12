import { describe, expect, it } from 'vitest'
import { occurrenceKey, renderingOf } from './occurrenceStyle'

describe('renderingOf', () => {
  it('reads a tentative status', () => {
    expect(renderingOf({ status: 'TENTATIVE', transparency: 'OPAQUE' })).toBe('tentative')
  })

  it('reads transparency as free', () => {
    expect(renderingOf({ status: 'CONFIRMED', transparency: 'TRANSPARENT' })).toBe('free')
  })

  it('reads a cancelled status', () => {
    expect(renderingOf({ status: 'CANCELLED', transparency: 'OPAQUE' })).toBe('cancelled')
  })

  it('reads anything else as busy', () => {
    expect(renderingOf({ status: 'CONFIRMED', transparency: 'OPAQUE' })).toBe('busy')
    expect(renderingOf({ transparency: 'OPAQUE' })).toBe('busy')
  })

  it('lets a cancellation beat a tentative status', () => {
    expect(renderingOf({ status: 'CANCELLED', transparency: 'TRANSPARENT' })).toBe('cancelled')
  })

  // The user's own answer to an invitation: « provisoire » is drawn as tentative whatever the
  // organizer's STATUS says, and an acceptance changes nothing.
  it('reads the user’s own tentative answer', () => {
    expect(renderingOf({ status: 'CONFIRMED', transparency: 'OPAQUE', myPartStat: 'TENTATIVE' })).toBe('tentative')
    expect(renderingOf({ transparency: 'OPAQUE', myPartStat: 'tentative' })).toBe('tentative')
    expect(renderingOf({ status: 'CONFIRMED', transparency: 'OPAQUE', myPartStat: 'ACCEPTED' })).toBe('busy')
    expect(renderingOf({ status: 'CANCELLED', transparency: 'OPAQUE', myPartStat: 'TENTATIVE' })).toBe('cancelled')
  })

  // Once answered, the answer is the availability: a STATUS:TENTATIVE the file still carries —
  // the organizer's, or one an earlier save wrote — does not outlive an acceptance.
  it('lets an acceptance beat a tentative status', () => {
    expect(renderingOf({ status: 'TENTATIVE', transparency: 'OPAQUE', myPartStat: 'ACCEPTED' })).toBe('busy')
    expect(renderingOf({ status: 'TENTATIVE', transparency: 'TRANSPARENT', myPartStat: 'accepted' })).toBe('free')
    expect(renderingOf({ status: 'TENTATIVE', transparency: 'OPAQUE' })).toBe('tentative')
  })
})

describe('occurrenceKey', () => {
  it('joins the event and the instance', () => {
    expect(occurrenceKey({ eventId: 'e1', instanceId: '20260916T090000' }))
      .toBe('e1#20260916T090000')
  })

  it('keeps the separator for an event that does not repeat', () => {
    expect(occurrenceKey({ eventId: 'e1', instanceId: '' })).toBe('e1#')
  })
})
