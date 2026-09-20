import { describe, expect, it } from 'vitest'
import en from '../../locales/en'
import fr from '../../locales/fr'
import { CALENDAR_COLORS, colourNameKey } from './calendarColors'

function leaf(bundle: unknown, path: string): unknown {
  return path.split('.').reduce<unknown>(
    (node, key) => (node as Record<string, unknown> | undefined)?.[key], bundle,
  )
}

const nameIn = (bundle: unknown, color: string) => leaf(bundle, colourNameKey(color))

describe('the colour names', () => {
  /* A name resolved from a map by hex is invisible to `keys.test.ts` and to `tsc` alike, so this
     is the guard standing in for both: a thirteenth colour with no name of its own fails here. */
  it('names every colour of the palette in both languages', () => {
    for (const color of CALENDAR_COLORS) {
      expect(nameIn(en.calendar, color), `en ${color}`).toEqual(expect.stringMatching(/\S/))
      expect(nameIn(fr.calendar, color), `fr ${color}`).toEqual(expect.stringMatching(/\S/))
    }
  })

  // Two swatches answering to one name are two controls a reader cannot tell apart.
  it('gives no two colours the same name', () => {
    for (const bundle of [en.calendar, fr.calendar]) {
      const names = CALENDAR_COLORS.map(color => nameIn(bundle, color))
      expect(new Set(names).size).toBe(CALENDAR_COLORS.length)
    }
  })

  it('reads a hex code however it was typed', () => {
    expect(colourNameKey(' #3B82C4 ')).toBe(colourNameKey('#3b82c4'))
  })
})
