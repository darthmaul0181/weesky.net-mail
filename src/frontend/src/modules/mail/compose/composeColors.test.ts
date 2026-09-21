import { describe, expect, it } from 'vitest'
import en from '../../../locales/en'
import fr from '../../../locales/fr'
import { SWATCH_ROWS, swatchNameKey } from './composeColors'

function leaf(bundle: unknown, path: string): unknown {
  return path.split('.').reduce<unknown>(
    (node, key) => (node as Record<string, unknown> | undefined)?.[key], bundle,
  )
}

const COLOURS = SWATCH_ROWS.flat()
const nameIn = (bundle: unknown, colour: (typeof COLOURS)[number]) =>
  leaf(bundle, swatchNameKey(colour))

describe('the swatch names', () => {
  /* A name resolved from a map by hex is invisible to `keys.test.ts` and to `tsc` alike, so this
     is the guard standing in for both: a nineteenth colour with no name of its own fails here. */
  it('names every swatch in both languages', () => {
    for (const colour of COLOURS) {
      expect(nameIn(en.compose, colour), `en ${colour}`).toEqual(expect.stringMatching(/\S/))
      expect(nameIn(fr.compose, colour), `fr ${colour}`).toEqual(expect.stringMatching(/\S/))
    }
  })

  // Two swatches answering to one name are two controls a reader cannot tell apart.
  it('gives no two colours the same name', () => {
    for (const bundle of [en.compose, fr.compose]) {
      const names = COLOURS.map(colour => nameIn(bundle, colour))
      expect(new Set(names).size).toBe(COLOURS.length)
    }
  })

  // Six per row is what the stylesheet draws, and the rows have to match what the eye sees.
  it('lays the eighteen out six per row', () => {
    expect(SWATCH_ROWS).toHaveLength(3)
    for (const row of SWATCH_ROWS) expect(row).toHaveLength(6)
  })
})
