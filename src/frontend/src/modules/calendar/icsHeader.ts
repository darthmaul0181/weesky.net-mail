/** RFC 7986's own properties, and the `X-` spellings Google, Apple and Nextcloud all still read. */
const NAME_PROPERTIES = ['X-WR-CALNAME', 'NAME']
const COLOUR_PROPERTIES = ['X-APPLE-CALENDAR-COLOR', 'COLOR']

const HEX = /^#[0-9a-f]{6}$/i

function unescapeText(value: string): string {
  return value.replace(/\\([\\,;nN])/g, (_, char) =>
    (char === 'n' || char === 'N' ? '\n' : char))
}

/** Apple writes an alpha channel the palette has no use for; the rest is a plain hex colour. */
function colourOf(raw: string): string | undefined {
  const hex = raw.trim()
  const trimmed = /^#[0-9a-f]{8}$/i.test(hex) ? hex.slice(0, 7) : hex
  return HEX.test(trimmed) ? trimmed : undefined
}

/** The calendar's own name and colour, read off the head of an `.ics` file so the import dialog
    can pre-fill from what the file says. Nothing past the first component is read — an export is
    tens of megabytes and this runs on the pick — and continuation lines are unfolded first. */
export function calendarHeaderOf(text: string): { name?: string; color?: string } {
  const firstComponent = text.indexOf('BEGIN:V', text.indexOf('BEGIN:VCALENDAR') + 1)
  const head = firstComponent === -1 ? text : text.slice(0, firstComponent)
  const unfolded = head.replace(/\r?\n[ \t]/g, '')

  const header: { name?: string; color?: string } = {}
  for (const line of unfolded.split(/\r?\n/)) {
    const colon = line.indexOf(':')
    if (colon === -1) continue

    const property = line.slice(0, colon).split(';')[0].trim().toUpperCase()
    const value = line.slice(colon + 1)

    if (header.name === undefined && NAME_PROPERTIES.includes(property)) {
      header.name = unescapeText(value).trim()
    } else if (header.color === undefined && COLOUR_PROPERTIES.includes(property)) {
      const color = colourOf(value)
      if (color) header.color = color
    }
  }
  return header
}
