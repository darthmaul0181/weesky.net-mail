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

/** What the field accepts: day first, then month, then an optional year — the shape its own
    placeholder has always advertised. Any of the three separators a keyboard offers. Day first in
    both languages on purpose: the stored card is one card, and reading `03/04/1990` as April in
    English and March in French would make the same date mean two days depending on who opened it. */
const Typed = /^(\d{1,2})[/.-](\d{1,2})(?:[/.-](\d{4}))?$/

const pad = (n: number) => String(n).padStart(2, '0')

/** Whether these numbers name a real day — 31 February parses and then rolls into March. */
function real(year: number, month: number, day: number): boolean {
  const date = new Date(Date.UTC(year, month - 1, day))
  return date.getUTCMonth() === month - 1 && date.getUTCDate() === day
}

/**
 * The stored BDAY as the editor's field shows it.
 *
 * The field used to render the stored value raw, so a card exported by a phone —
 * `BDAY:19930621T115900Z` — read as exactly that, beside a placeholder inviting `27/10/1979`. The
 * card next door showed `21 juin 1993` the whole time, because only the reading side ever parsed.
 *
 * A value this cannot read is still shown verbatim, for `formatBirthday`'s reason: a birthday that
 * vanishes reads as data lost, where an odd-looking one reads as a card to correct.
 */
export function birthdayToInput(raw: string | null | undefined): string {
  const value = raw?.trim()
  if (!value) return ''

  const yearLess = YearLess.exec(value)
  if (yearLess) return `${yearLess[2]}/${yearLess[1]}`

  const dated = Dated.exec(value)
  if (dated) return `${dated[3]}/${dated[2]}/${dated[1]}`

  return value
}

/**
 * What the field's text is stored as — the vCard spelling, not the typed one.
 *
 * The backend bounds this string's length and nothing else, so whatever leaves here is what lands
 * after `BDAY:`. Typing the placeholder's own `27/10/1979` therefore used to write a malformed
 * vCard date, which the card then had to print verbatim because no reader could parse it: the
 * field was teaching its own corruption.
 *
 * Text this cannot read travels exactly as typed, which is décision 7's escape hatch — the vCard
 * admits forms neither this nor a native picker can express, and refusing them here would make a
 * card carrying one permanently unsaveable.
 */
export function inputToBirthday(text: string): string {
  const value = text.trim()
  if (value === '') return ''

  const typed = Typed.exec(value)
  if (!typed) return value

  const day = +typed[1]
  const month = +typed[2]
  if (typed[3] === undefined) {
    // Any leap-safe year: it is never stored, it only proves 29 February is a day.
    return real(2000, month, day) ? `--${pad(month)}${pad(day)}` : value
  }
  const year = +typed[3]
  return real(year, month, day) ? `${typed[3]}-${pad(month)}-${pad(day)}` : value
}
