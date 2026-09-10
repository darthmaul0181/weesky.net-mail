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
