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
