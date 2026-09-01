import { describe, it, expect } from 'vitest'
import {
  canDropIntoScope, dragIds, parseContactDrag, serializeContactDrag,
} from './dragContacts'

describe('dragIds', () => {
  // La ligne saisie emporte la sélection quand elle en fait partie, elle seule sinon : glisser une
  // ligne non cochée ne doit jamais déranger une sélection faite pour autre chose.
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
  // « Tous les contacts » est la vue complète, pas un groupe : rien à y ajouter.
  it('refuses the all scope and accepts favourites', () => {
    expect(canDropIntoScope('all')).toBe(false)
    expect(canDropIntoScope('favorites')).toBe(true)
  })

  // Un groupe est une cible par construction : la fonction ne le nomme pas, et ce test épingle
  // qu'elle n'a pas besoin de le nommer.
  it('accepts a group scope without a clause of its own', () => {
    expect(canDropIntoScope('group:abc')).toBe(true)
  })
})
