import { describe, expect, it } from 'vitest'
import { capturable, splitFullName } from './captureModel'
import type { Contact } from './contactTypes'

function contact(id: string, addresses: string[]): Contact {
  return { id, firstName: null, lastName: null, nickname: null, isFavorite: false, addresses }
}

describe('splitFullName', () => {
  it('splits at the last space', () => {
    expect(splitFullName('Alice Dupont', 'a@x.be'))
      .toEqual({ firstName: 'Alice', lastName: 'Dupont' })
    expect(splitFullName('Jean Pierre Dupont', 'a@x.be'))
      .toEqual({ firstName: 'Jean Pierre', lastName: 'Dupont' })
  })

  it('reads a comma as Last, First', () => {
    expect(splitFullName('Dupont, Alice', 'a@x.be'))
      .toEqual({ firstName: 'Alice', lastName: 'Dupont' })
  })

  it('files a single word as the first name', () => {
    expect(splitFullName('Alice', 'a@x.be')).toEqual({ firstName: 'Alice', lastName: null })
  })

  it('yields nothing for a blank name', () => {
    expect(splitFullName('   ', 'a@x.be')).toEqual({ firstName: null, lastName: null })
  })

  // Many clients put the address in the display name; storing it as a first name would show the
  // address twice on the tile.
  it('yields nothing when the name is the address', () => {
    expect(splitFullName('Alice@X.be', 'alice@x.be')).toEqual({ firstName: null, lastName: null })
  })

  // Over 100 the backend refuses the whole contact, so the fix has to be a truncation, not a loss.
  it('truncates each half to 100 characters', () => {
    const long = 'a'.repeat(150)
    const split = splitFullName(`${long} ${long}`, 'a@x.be')

    expect(split.firstName).toHaveLength(100)
    expect(split.lastName).toHaveLength(100)
  })

  // A lone surrogate is refused by strict utf8mb4, which loses the contact the truncation saves.
  it('drops an astral character the truncation would cut in half', () => {
    const split = splitFullName(`${'a'.repeat(99)}𝄞x Dupont`, 'a@x.be')

    expect(split.firstName).toBe('a'.repeat(99))
    expect([...(split.firstName ?? '')].every(c => c.charCodeAt(0) < 0xd800)).toBe(true)
  })
})

describe('capturable', () => {
  it('captures an address the book does not hold', () => {
    const found = capturable([contact('1', ['bob@x.be'])], ['alice@x.be'], {}, new Set())

    expect(found).toEqual([{ firstName: null, lastName: null, address: 'alice@x.be' }])
  })

  it('names the candidate from the hint', () => {
    const found = capturable([], ['alice@x.be'], { 'alice@x.be': 'Alice Dupont' }, new Set())

    expect(found).toEqual([{ firstName: 'Alice', lastName: 'Dupont', address: 'alice@x.be' }])
  })

  it('skips an address already in the book, whatever its spelling', () => {
    expect(capturable([contact('1', ['alice@x.be'])], ['  Alice@X.BE '], {}, new Set())).toEqual([])
  })

  it('skips my own addresses, whatever their spelling', () => {
    expect(capturable([], ['me@x.be'], {}, new Set(['Me@X.be']))).toEqual([])
  })

  it('captures one candidate when a send names the same address twice', () => {
    const found = capturable([], ['alice@x.be', 'ALICE@x.be'], {}, new Set())

    expect(found).toHaveLength(1)
  })

  // The frontier between canonicalAddress and contactSearch's fold: fold strips diacritics so it
  // could answer a search, and would wrongly report this address as already known.
  it('treats two addresses differing by a diacritic as two candidates', () => {
    const found = capturable([contact('1', ['jose@x.be'])], ['josé@x.be'], {}, new Set())

    expect(found).toEqual([{ firstName: null, lastName: null, address: 'josé@x.be' }])
  })

  it('drops blank entries', () => {
    expect(capturable([], ['', '   '], {}, new Set())).toEqual([])
  })

  it('keeps the order the recipients came in', () => {
    const found = capturable([], ['b@x.be', 'a@x.be'], {}, new Set())

    expect(found.map(c => c.address)).toEqual(['b@x.be', 'a@x.be'])
  })
})
