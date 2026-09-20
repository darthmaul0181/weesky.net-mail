/** The twelve the palette offers, drawn from the mockup. They are data rather than theme: a
    calendar keeps its colour across every palette, so none of them is a token. */
export const CALENDAR_COLORS: readonly string[] = [
  '#3b82c4', '#7c5cbf', '#2e9e6b', '#e2674a', '#d9a400', '#c2410c',
  '#0e9aa7', '#be185d', '#4b5563', '#7c3aed', '#15803d', '#b45309',
]

const HEX = /^#[0-9a-f]{6}$/i

/** What the API stores and what `X-APPLE-CALENDAR-COLOR` is normalised to: six digits, no alpha.
    Anything else is refused at the keyboard rather than after a round trip. */
export function isHexColor(value: string): boolean {
  return HEX.test(value.trim())
}

/** Hex to the short name the catalogue knows it by. A name resolved from a map is invisible to
    `keys.test.ts`, so `calendarColors.test.ts` is the guard that stands in for it. */
const NAMES = {
  '#3b82c4': 'blue', '#7c5cbf': 'lavender', '#2e9e6b': 'emerald', '#e2674a': 'coral',
  '#d9a400': 'gold', '#c2410c': 'orange', '#0e9aa7': 'teal', '#be185d': 'raspberry',
  '#4b5563': 'slate', '#7c3aed': 'violet', '#15803d': 'forest', '#b45309': 'bronze',
} as const

type ColourName = typeof NAMES[keyof typeof NAMES]

/** What a swatch is called, since a reader hearing `#3b82c4` learns nothing. The narrow return
    type is what `t()` takes; a hex the map does not hold answers a key nothing resolves, which is
    the failure the guard test reports. */
export function colourNameKey(hex: string): `dialogs.colourName.${ColourName}` {
  return `dialogs.colourName.${NAMES[hex.trim().toLowerCase() as keyof typeof NAMES]}`
}
