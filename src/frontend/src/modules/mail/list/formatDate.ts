import { activeLocale, dateFormat } from '../../../lib/intl'

/**
 * A list row has one line for the date, so precision shrinks as the message ages: a time
 * today, a day and month this year, a year beyond that.
 *
 * `locale` defaults to the active one rather than to the browser's: an account whose browser is
 * English and whose choice is French must not read a French interface printing English months.
 */
export function formatListDate(iso: string, now: Date = new Date(), locale: string = activeLocale()): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return ''

  if (date.toDateString() === now.toDateString()) {
    return dateFormat({ hour: '2-digit', minute: '2-digit' }, locale).format(date)
  }

  if (date.getFullYear() === now.getFullYear()) {
    return dateFormat({ day: 'numeric', month: 'short' }, locale).format(date)
  }

  return dateFormat({ year: 'numeric', month: 'short', day: 'numeric' }, locale).format(date)
}
