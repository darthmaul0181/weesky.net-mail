import { describe, it, expect } from 'vitest'
import {
  toRows, sortIdentities, applyDefault, applyLabel, applyRemoval, applyAddition,
} from './identityRows'
import type { SendingIdentity } from '../../mail/api/mailTypes'

function identity(over: Partial<SendingIdentity>): SendingIdentity {
  return {
    address: 'a@x.be', displayName: 'A', isDefault: false,
    isPrimary: false, stale: false, labelIsCustom: true, ...over,
  }
}
const primary = identity({ address: 'mick@x.be', displayName: 'Mick', isPrimary: true, isDefault: true, labelIsCustom: false })
const alias = identity({ address: 'michel@x.be', displayName: 'Michel' })

describe('toRows', () => {
  it('excludes a primary whose label is not overridden', () => {
    expect(toRows([primary, alias])).toEqual([
      { address: 'michel@x.be', displayName: 'Michel', isDefault: false },
    ])
  })

  it('never sends the primary, even if a labelIsCustom flag lingers on it', () => {
    const overridden = { ...primary, displayName: 'Le Boss', labelIsCustom: true }
    expect(toRows([overridden, alias])).toEqual([
      { address: 'michel@x.be', displayName: 'Michel', isDefault: false },
    ])
  })
})

describe('applyDefault', () => {
  it('marks the chosen alias as the only default', () => {
    expect(toRows(applyDefault([primary, alias], 'michel@x.be'))).toEqual([
      { address: 'michel@x.be', displayName: 'Michel', isDefault: true },
    ])
  })

  // No marked row means "the primary is the default" — choosing it just demarcates the others.
  it('choosing the primary produces no marked row', () => {
    const aliasDefault = { ...alias, isDefault: true }
    expect(toRows(applyDefault([{ ...primary, isDefault: false }, aliasDefault], 'mick@x.be'))).toEqual([
      { address: 'michel@x.be', displayName: 'Michel', isDefault: false },
    ])
  })

  it('keeps the primary on the resolved list, which is what the page goes on showing', () => {
    expect(applyDefault([primary, alias], 'michel@x.be').map(i => i.address))
      .toEqual(['mick@x.be', 'michel@x.be'])
  })

  // The one apply* where the resolved list and the payload legitimately disagree: the star has
  // to move to the primary on screen even though the wire says "default" by marking nobody.
  it('resolves the primary as the default it has just become', () => {
    const aliasDefault = { ...alias, isDefault: true }
    const resolved = applyDefault([{ ...primary, isDefault: false }, aliasDefault], 'mick@x.be')
    expect(resolved.find(i => i.isPrimary)?.isDefault).toBe(true)
    expect(resolved.find(i => !i.isPrimary)?.isDefault).toBe(false)
    expect(toRows(resolved).some(r => r.isDefault)).toBe(false)
  })
})

describe('sortIdentities', () => {
  // Mirrors OrderByDescending(IsDefault).ThenBy(DisplayName, OrdinalIgnoreCase).
  it('puts the default first, then orders by label whatever the case', () => {
    const rows = [
      identity({ address: 'z@x.be', displayName: 'zoe' }),
      identity({ address: 'a@x.be', displayName: 'Ancien' }),
      identity({ address: 'd@x.be', displayName: 'Marc', isDefault: true }),
      identity({ address: 'b@x.be', displayName: 'bob' }),
    ]
    expect(sortIdentities(rows).map(i => i.displayName)).toEqual(['Marc', 'Ancien', 'bob', 'zoe'])
  })

  // The mirror of OrdinalIgnoreCase_OrdersTheWayTheFrontendSortDoes: same pairs, same order,
  // asserted there against the real comparer. A naive toLowerCase puts '_perso' first, and a
  // naive toUpperCase makes 'ß'/'ss' and 'ﬁx'/'fix' compare equal.
  it.each([
    ['_perso', 'Anne'],
    ['ß', 'ss'],
    ['ﬁx', 'fix'],
    ['İ', 'i'],
  ])('folds %s after %s, the way OrdinalIgnoreCase does', (later, earlier) => {
    const rows = [identity({ address: 'l@x.be', displayName: later }),
      identity({ address: 'e@x.be', displayName: earlier })]
    expect(sortIdentities(rows).map(i => i.displayName)).toEqual([earlier, later])
  })

  it('treats labels differing only in case as equal', () => {
    const rows = [identity({ address: 'a@x.be', displayName: 'anne' }),
      identity({ address: 'b@x.be', displayName: 'Anne' })]
    expect(sortIdentities(rows).map(i => i.address)).toEqual(['a@x.be', 'b@x.be'])
  })

  it('leaves the list it was given alone', () => {
    const rows = [alias, primary]
    sortIdentities(rows)
    expect(rows.map(i => i.address)).toEqual(['michel@x.be', 'mick@x.be'])
  })
})

describe('applyLabel', () => {
  it('renames an alias', () => {
    expect(toRows(applyLabel([primary, alias], 'michel@x.be', ' Michel D. '))).toEqual([
      { address: 'michel@x.be', displayName: 'Michel D.', isDefault: false },
    ])
  })

  it('clearing an alias label keeps the old one', () => {
    expect(toRows(applyLabel([primary, alias], 'michel@x.be', ''))).toEqual([
      { address: 'michel@x.be', displayName: 'Michel', isDefault: false },
    ])
  })
})

describe('applyRemoval / applyAddition', () => {
  it('removes an identity', () => {
    expect(toRows(applyRemoval([primary, alias], 'michel@x.be'))).toEqual([])
  })

  // Same disagreement as applyDefault: nobody marked is how the wire says "the primary", but
  // the star still has to land somewhere on screen.
  it('hands the star back to the primary when the marked alias is the one removed', () => {
    const marked = { ...alias, isDefault: true }
    const resolved = applyRemoval([{ ...primary, isDefault: false }, marked], 'michel@x.be')
    expect(resolved.find(i => i.isPrimary)?.isDefault).toBe(true)
    expect(toRows(resolved).some(r => r.isDefault)).toBe(false)
  })

  it('leaves the default where it is when some other row is removed', () => {
    const marked = { ...alias, isDefault: true }
    const other = identity({ address: 'support@x.be', displayName: 'Support' })
    const resolved = applyRemoval([{ ...primary, isDefault: false }, marked, other], 'support@x.be')
    expect(resolved.filter(i => i.isDefault).map(i => i.address)).toEqual(['michel@x.be'])
  })

  it('appends a new identity, never as default', () => {
    expect(toRows(applyAddition([primary, alias], 'support@x.be', ' Support '))).toEqual([
      { address: 'michel@x.be', displayName: 'Michel', isDefault: false },
      { address: 'support@x.be', displayName: 'Support', isDefault: false },
    ])
  })

  it('resolves the added identity as a live, non-primary one', () => {
    const added = applyAddition([primary], 'support@x.be', 'Support')
    expect(added[added.length - 1])
      .toMatchObject({ isPrimary: false, stale: false, labelIsCustom: true })
  })
})
