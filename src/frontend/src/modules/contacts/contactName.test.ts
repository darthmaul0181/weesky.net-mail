import { describe, expect, it } from 'vitest'
import { displayNameOf, primaryAddressOf } from './contactName'
import type { Contact } from './contactTypes'

function contact(fields: Partial<Contact> = {}): Contact {
  return {
    id: 'c1', firstName: null, lastName: null, nickname: null,
    isFavorite: false, addresses: [], ...fields,
  }
}

describe('displayNameOf', () => {
  it('joins first and last name', () => {
    expect(displayNameOf(contact({ firstName: 'Bruno', lastName: 'Mertens' }))).toBe('Bruno Mertens')
  })

  it('accepts a first name alone', () => {
    expect(displayNameOf(contact({ firstName: 'Bruno' }))).toBe('Bruno')
  })

  it('accepts a last name alone', () => {
    expect(displayNameOf(contact({ lastName: 'Mertens' }))).toBe('Mertens')
  })

  // The three fallbacks in order. A tile with no label at all is what this prevents, and every
  // screen has to fall back the same way or one contact reads under two different names.
  // The contact carries an address as well, which is what pins the *order* of the two fallbacks:
  // with no address on it, folding to the address first would read exactly the same.
  it('falls back to the nickname before the primary address', () => {
    expect(displayNameOf(contact({ nickname: 'bru', addresses: ['bruno@example.com'] }))).toBe('bru')
  })

  it('falls back to the primary address when there is neither', () => {
    expect(displayNameOf(contact({ addresses: ['bruno@example.com', 'other@example.com'] })))
      .toBe('bruno@example.com')
  })

  it('prefers a name over a nickname', () => {
    expect(displayNameOf(contact({ firstName: 'Bruno', nickname: 'bru' }))).toBe('Bruno')
  })

  it('returns an empty string when the contact carries nothing', () => {
    expect(displayNameOf(contact())).toBe('')
  })
})

describe('primaryAddressOf', () => {
  it('is the first address', () => {
    expect(primaryAddressOf(contact({ addresses: ['a@x.be', 'b@x.be'] }))).toBe('a@x.be')
  })

  it('is null without any address', () => {
    expect(primaryAddressOf(contact())).toBeNull()
  })
})
