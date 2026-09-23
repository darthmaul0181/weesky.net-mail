import { activeLocale, dateFormat, localDay } from '../../../lib/intl'

// Precision shrinks with age: a time today, day and month this year, a year beyond. `today` is
// required, since a clock read in here would leave a memoised row stale overnight; `locale` defaults
// to the active one, never the browser's.
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
