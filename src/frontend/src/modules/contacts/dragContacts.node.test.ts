import { describe, it, expect } from 'vitest'
import {
  canDropIntoScope, dragIds, parseContactDrag, serializeContactDrag,
} from './dragContacts'

describe('dragIds', () => {
  // The grabbed row carries the whole selection when it belongs to it, itself alone otherwise:
  // dragging an unchecked row must never disturb a selection made for something else.
  it('carries the whole selection when the grabbed row belongs to it', () => {
    expect(dragIds(['a', 'b'], 'a')).toEqual(['a', 'b'])
  })

  it('carries the grabbed row alone when it does not', () => {
    expect(dragIds(['a', 'b'], 'c')).toEqual(['c'])
  })
})

describe('parseContactDrag', () => {
  it('reads back what serialize wrote', () => {
    expect(parseContactDrag(serializeContactDrag({ ids: ['a'] }))).toEqual({ ids: ['a'] })
  })

  it.each([
    ['not json', 'oops'],
    ['no ids', JSON.stringify({})],
    ['an empty batch', JSON.stringify({ ids: [] })],
    ['a non-string id', JSON.stringify({ ids: [7] })],
  ])('answers null for %s', (_label, raw) => {
    expect(parseContactDrag(raw)).toBeNull()
  })
})

describe('canDropIntoScope', () => {
  // "All contacts" is the complete view, not a group: nothing to add to it.
  it('refuses the all scope and accepts favourites', () => {
    expect(canDropIntoScope('all')).toBe(false)
    expect(canDropIntoScope('favorites')).toBe(true)
  })

  // A group is a target by construction: the function does not name it, and this test pins that
  // it has no need to.
  it('accepts a group scope without a clause of its own', () => {
    expect(canDropIntoScope('group:abc')).toBe(true)
  })
})
