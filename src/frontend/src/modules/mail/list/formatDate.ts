import { activeLocale, dateFormat, localDay } from '../../../lib/intl'

/**
 * A list row has one line for the date, so precision shrinks as the message ages: a time
 * today, a day and month this year, a year beyond that.
 *
 * `today` is the local calendar day this one is measured against, and it is required: a clock read
 * in here is an input no prop carries, and a memoised row drawn against it goes stale overnight.
 *
 * `locale` defaults to the active one rather than to the browser's: an account whose browser is
 * English and whose choice is French must not read a French interface printing English months.
 */
export function formatListDate(
  iso: string, today: string, locale: string = activeLocale(),
): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return ''
  const day = localDay(date)

  if (day === today) {
    return dateFormat({ hour: '2-digit', minute: '2-digit' }, locale).format(date)
  }

  if (day.slice(0, 4) === today.slice(0, 4)) {
    return dateFormat({ day: 'numeric', month: 'short' }, locale).format(date)
  }

  return dateFormat({ year: 'numeric', month: 'short', day: 'numeric' }, locale).format(date)
}
