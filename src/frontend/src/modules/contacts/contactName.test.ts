import { describe, expect, it } from 'vitest'
import { contactNameOf, displayNameOf, primaryAddressOf } from './contactName'
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

  /* The card's own FN outranks the components, which is the whole point of storing it: the server
     only keeps one that diverges from them, so what arrives here is a name somebody typed. */
  it('prefers the display name over the components', () => {
    expect(displayNameOf(contact({
      firstName: 'Raphaël', lastName: 'Le Châtelier', displayName: 'Dr. Raphaël Le Châtelier Jr.',
    }))).toBe('Dr. Raphaël Le Châtelier Jr.')
  })

  it('falls back to first and last name when no display name was given', () => {
    expect(displayNameOf(contact({ firstName: 'Raphaël', lastName: 'Le Châtelier' })))
      .toBe('Raphaël Le Châtelier')
  })

  it('returns an empty string when the contact carries nothing', () => {
    expect(displayNameOf(contact())).toBe('')
  })
})

describe('contactNameOf', () => {
  it('names a contact the same way displayNameOf does', () => {
    expect(contactNameOf(contact({ firstName: 'Bruno', lastName: 'Mertens' }))).toBe('Bruno Mertens')
    expect(contactNameOf(contact({ nickname: 'bru' }))).toBe('bru')
    expect(contactNameOf(contact({ firstName: 'Bruno', displayName: 'Dr. B.' }))).toBe('Dr. B.')
  })

  /* The address guard covers the display name too: a legacy row can hold an FN that is the
     card's own address, and a recipient chip must not call that a name. */
  it('answers null for a display name that is one of the contact addresses', () => {
    expect(contactNameOf(contact({
      displayName: 'Bruno@Example.com', addresses: ['bruno@example.com'],
    }))).toBeNull()
  })

  // The whole reason it exists: a caller showing one address must not be handed another one as a
  // fallback. A recipient chip naming an address the message will not go to would be a lie.
  it('answers null rather than falling back to an address', () => {
    expect(contactNameOf(contact({ addresses: ['bruno@x.be', 'other@x.be'] }))).toBeNull()
    expect(contactNameOf(contact())).toBeNull()
  })

  // An address in a name column is what an Outlook/Rainloop export writes for a nameless card, and
  // it is not a name: unchecked, such a contact out-names the one holding the real name for the
  // same address, and the chip shows an address it claims is a name.
  it('answers null when the name it found is one of the contact’s own addresses', () => {
    expect(contactNameOf(contact({
      nickname: 'bruno@x.be', addresses: ['bruno@x.be'],
    }))).toBeNull()
    expect(contactNameOf(contact({
      firstName: 'Bruno@X.BE ', addresses: ['bruno@x.be'],
    }))).toBeNull()
    expect(contactNameOf(contact({
      nickname: 'other@x.be', addresses: ['bruno@x.be', 'other@x.be'],
    }))).toBeNull()
  })

  it('keeps a name that merely looks like an address the contact does not hold', () => {
    expect(contactNameOf(contact({ nickname: 'info@shop', addresses: ['bruno@x.be'] })))
      .toBe('info@shop')
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
