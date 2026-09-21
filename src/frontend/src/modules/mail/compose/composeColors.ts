/** Hex to the short name the catalogue knows it by. A name resolved from a map is invisible to
    `keys.test.ts`, so `composeColors.test.ts` is the guard that stands in for it. */
const NAMES = {
  '#000000': 'black', '#444444': 'darkGrey', '#666666': 'grey',
  '#999999': 'lightGrey', '#cccccc': 'silver', '#ffffff': 'white',
  '#d0021b': 'red', '#e2674a': 'coral', '#f5a623': 'amber',
  '#f8e71c': 'yellow', '#7ed321': 'green', '#417505': 'olive',
  '#4a90d9': 'blue', '#182238': 'navy', '#9013fe': 'purple',
  '#bd10e0': 'magenta', '#8b572a': 'brown', '#50e3c2': 'turquoise',
} as const

type SwatchColour = keyof typeof NAMES
type SwatchName = typeof NAMES[SwatchColour]

/** Six per row, the count `.compose-swatches` draws: the rows have to match what the eye sees.
    Typed as the name map's own keys, so a nineteenth colour cannot be added without naming it. */
export const SWATCH_ROWS: readonly (readonly SwatchColour[])[] = [
  ['#000000', '#444444', '#666666', '#999999', '#cccccc', '#ffffff'],
  ['#d0021b', '#e2674a', '#f5a623', '#f8e71c', '#7ed321', '#417505'],
  ['#4a90d9', '#182238', '#9013fe', '#bd10e0', '#8b572a', '#50e3c2'],
]

/** What a swatch is called, since a reader hearing `#d0021b` learns nothing. It takes one of the
    eighteen rather than any hex, so a key nothing resolves must not be constructible. */
export function swatchNameKey(hex: SwatchColour): `toolbar.colourName.${SwatchName}` {
  return `toolbar.colourName.${NAMES[hex]}`
}
