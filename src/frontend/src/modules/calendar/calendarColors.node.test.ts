import { describe } from 'vitest'
import { namesInBothLanguages } from '../../locales/nameParity-test'
import { CALENDAR_COLORS, colourNameKey } from './calendarColors'

describe('the colour names', () => {
  /* A name resolved from a map by hex is invisible to `keys.test.ts` and to `tsc` alike, so this
     is the guard standing in for both: a thirteenth colour with no name of its own fails here. */
  namesInBothLanguages(CALENDAR_COLORS, colourNameKey, 'calendar')
})
