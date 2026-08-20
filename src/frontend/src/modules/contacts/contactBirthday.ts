import { dateFormat } from '../../lib/intl'

/** `19930621`, `1993-06-21`, and the date-time a phone exports — only the date half is read. */
const Dated = /^(\d{4})-?(\d{2})-?(\d{2})/

/** vCard's year-less birthday: `--0315` and its extended spelling. */
const YearLess = /^--(\d{2})-?(\d{2})$/

/**
 * A card's BDAY as the interface's language spells it, or the raw value when it says something
 * this cannot read — a birthday that vanishes reads as data lost, where an odd-looking one reads
 * as a card to correct. The year is never invented: a card that gives none is formatted without.
 */
export function formatBirthday(raw: string | null | undefined, locale?: string): string | null {
  const value = raw?.trim()
  if (!value) return null

  const yearLess = YearLess.exec(value)
  if (yearLess) {
    // Any leap-safe year: it is never formatted, it only carries the month and day to Intl.
    return format(2000, +yearLess[1], +yearLess[2], { day: 'numeric', month: 'long' }, locale) ?? value
  }

  const dated = Dated.exec(value)
  if (dated) {
    return format(+dated[1], +dated[2], +dated[3],
      { day: 'numeric', month: 'long', year: 'numeric' }, locale) ?? value
  }

  return value
}

/** Null when the numbers name no real day — 31 February parses and then rolls into March. */
function format(
  year: number, month: number, day: number,
  options: Intl.DateTimeFormatOptions, locale?: string,
): string | null {
  const date = new Date(Date.UTC(year, month - 1, day))
  if (date.getUTCMonth() !== month - 1 || date.getUTCDate() !== day) return null
  // UTC throughout: built at midnight UTC, a date formatted in a westward zone would read as
  // the day before.
  return dateFormat({ ...options, timeZone: 'UTC' }, locale).format(date)
}
