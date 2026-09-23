import { expect, it } from 'vitest'
import en from './en'
import fr from './fr'

function leaf(bundle: unknown, path: string): unknown {
  return path.split('.').reduce<unknown>(
    (node, key) => (node as Record<string, unknown> | undefined)?.[key], bundle,
  )
}

/**
 * The two guards a palette named through a map needs. A key resolved by hex is invisible to
 * `keys.test.ts` and to `tsc` alike, so each palette calls this from its own test file and fails
 * there: a colour added with no name of its own, or a name given to two colours — two controls a
 * reader cannot tell apart.
 */
export function namesInBothLanguages<T extends string>(
  colours: readonly T[], keyOf: (colour: T) => string, bundle: Extract<keyof typeof en, keyof typeof fr>,
): void {
  const nameIn = (catalogue: typeof en | typeof fr, colour: T) =>
    leaf(catalogue[bundle], keyOf(colour))

  it('names every colour in both languages', () => {
    for (const colour of colours) {
      expect(nameIn(en, colour), `en ${colour}`).toEqual(expect.stringMatching(/\S/))
      expect(nameIn(fr, colour), `fr ${colour}`).toEqual(expect.stringMatching(/\S/))
    }
  })

  it('gives no two colours the same name', () => {
    for (const catalogue of [en, fr]) {
      const names = colours.map(colour => nameIn(catalogue, colour))
      expect(new Set(names).size).toBe(colours.length)
    }
  })
}
