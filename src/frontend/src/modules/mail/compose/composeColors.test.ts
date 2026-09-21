import { describe, expect, it } from 'vitest'
import { namesInBothLanguages } from '../../../locales/nameParity-test'
import { SWATCH_ROWS, swatchNameKey } from './composeColors'

describe('the swatch names', () => {
  /* A name resolved from a map by hex is invisible to `keys.test.ts` and to `tsc` alike, so this
     is the guard standing in for both: a nineteenth colour with no name of its own fails here. */
  namesInBothLanguages(SWATCH_ROWS.flat(), swatchNameKey, 'compose')

  // Six per row is what the stylesheet draws, and the rows have to match what the eye sees.
  it('lays the eighteen out six per row', () => {
    expect(SWATCH_ROWS).toHaveLength(3)
    for (const row of SWATCH_ROWS) expect(row).toHaveLength(6)
  })
})
