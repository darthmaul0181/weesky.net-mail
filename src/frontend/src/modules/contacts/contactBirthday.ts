import { dateFormat } from '../../lib/intl'

/** `19930621`, `1993-06-21`, and the date-time a phone exports — only the date half is read. */
const Dated = /^(\d{4})-?(\d{2})-?(\d{2})/

/** vCard's year-less birthday: `--0315` and its extended spelling. */
const YearLess = /^--(\d{2})-?(\d{2})$/

/** A card's BDAY in the interface's language, never inventing a year. Unreadable values come back
 * raw: a birthday that vanishes reads as data lost, an odd one as a card to correct. */
export function formatBirthday(raw: string | null | undefined, locale?: string): string | null {
  const value = raw?.trim()
  if (!value) return null

  const yearLess = YearLess.exec(value)
  if (yearLess) {
    // Both groups are mandatory in the pattern (no `?`), so always captured on a match.
    return format(2000, +yearLess[1]!, +yearLess[2]!, { day: 'numeric', month: 'long' }, locale)
      ?? value
  }

  const dated = Dated.exec(value)
  if (dated) {
    // All three groups are mandatory in the pattern, so always captured on a match.
    return format(+dated[1]!, +dated[2]!, +dated[3]!,
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

/** Day, month, optional year, with any keyboard separator, as the placeholder shows. Day first in
 * both languages: one card must not read `03/04/1990` as April in English and March in French. */
const Typed = /^(\d{1,2})[/.-](\d{1,2})(?:[/.-](\d{4}))?$/

const pad = (n: number) => String(n).padStart(2, '0')

/** Whether these numbers name a real day — 31 February parses and then rolls into March. */
function real(year: number, month: number, day: number): boolean {
  const date = new Date(Date.UTC(year, month - 1, day))
  return date.getUTCMonth() === month - 1 && date.getUTCDate() === day
}

/** The stored BDAY as the editor's field shows it (a phone's `19930621T115900Z` read raw before);
 * an unreadable value is shown verbatim, for `formatBirthday`'s reason. */
export function birthdayToInput(raw: string | null | undefined): string {
  const value = raw?.trim()
  if (!value) return ''

  const yearLess = YearLess.exec(value)
  if (yearLess) return `${yearLess[2]}/${yearLess[1]}`

  const dated = Dated.exec(value)
  if (dated) return `${dated[3]}/${dated[2]}/${dated[1]}`

  return value
}

/** The field's text as the vCard spelling: the backend only bounds the length, so a typed
 * `27/10/1979` once landed as a malformed BDAY. Unreadable text travels as typed, or a card holding
 * a form no picker expresses could never be saved. */
export function inputToBirthday(text: string): string {
  const value = text.trim()
  if (value === '') return ''

  const typed = Typed.exec(value)
  if (!typed) return value

  // Groups 1 and 2 are mandatory in the pattern (no `?`), so always captured on a match.
  const day = +typed[1]!
  const month = +typed[2]!
  if (typed[3] === undefined) {
    // Any leap-safe year: it is never stored, it only proves 29 February is a day.
    return real(2000, month, day) ? `--${pad(month)}${pad(day)}` : value
  }
  const year = +typed[3]
  return real(year, month, day) ? `${typed[3]}-${pad(month)}-${pad(day)}` : value
}
