import { describe, it, expect } from 'vitest'
import { buildIndex, letterOf, prefixOf } from './aliasFamilies'

const compare = new Intl.Collator('fr', { sensitivity: 'base' }).compare
const index = (...names: string[]) => buildIndex(names.map(name => ({ name, domain: 'weesky.be' })), compare)
const shape = (...names: string[]) => index(...names).map(({ letter, lines }) =>
  [letter, ...lines.map(line => `${line.prefix}[${line.entries.map(e => e.shown).join(',')}]`)])

describe('prefixOf', () => {
  it.each([
    ['darth_amazon', 'darth_'], ['michael.dalpont-okta', 'michael.'], ['www-data', 'www-'],
    ['house', ''], ['_hidden', ''], ['trailing_', ''],
  ])('%s → %j', (name, prefix) => expect(prefixOf(name)).toBe(prefix))
})

describe('letterOf', () => {
  it.each([['abuse', 'A'], ['élodie', 'E'], ['go5000', 'G'], ['5000go', '#'], ['+tag', '#']])(
    '%s → %s', (name, letter) => expect(letterOf(name)).toBe(letter))
})

describe('buildIndex', () => {
  it('writes a prefix shared by three aliases once, on a line after the loose names', () => {
    expect(shape('darth_ebay', 'diamant', 'darth_amazon', 'dorothée', 'darth_ups')).toEqual([
      ['D', '[diamant,dorothée]', 'darth_[amazon,ebay,ups]'],
    ])
  })

  it('leaves a prefix held by two aliases on the loose line, in full', () => {
    expect(shape('gilles_okta', 'go5000', 'gilles_carrefour')).toEqual([
      ['G', '[gilles_carrefour,gilles_okta,go5000]'],
    ])
  })

  it('splits on the dot and the hyphen too, at the first separator', () => {
    expect(shape('m.a', 'm-b', 'm.c', 'm.d-e')).toEqual([['M', '[m-b]', 'm.[a,c,d-e]']])
  })

  it('shows every name in lower case, and the address it acts on in lower case too', () => {
    const [d] = index('Dorian')
    expect(d?.lines[0]?.entries[0]).toMatchObject({ shown: 'dorian', key: 'dorian@weesky.be' })
  })

  it('orders letters alphabetically with the unordered ones last, and a family-only letter has no loose line', () => {
    expect(shape('1password', 'x_a', 'x_b', 'x_c', 'abuse')).toEqual([
      ['A', '[abuse]'], ['X', 'x_[a,b,c]'], ['#', '[1password]'],
    ])
  })
})
