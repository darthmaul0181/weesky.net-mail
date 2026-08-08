import { activeLocale, dateFormat } from '../../../lib/intl'

/**
 * The reader has room for one unambiguous date, so it spells the whole thing out rather than
 * abbreviating the way a list row must. Seconds are dropped: they are noise no reader of a
 * message needs, and they were the most jarring part of the raw toLocaleString() this replaces.
 *
 * This is the Date header — when the sender wrote the message. The list shows arrival instead,
 * because that is what orders it.
 */
export function formatReaderDate(iso: string, locale: string = activeLocale()): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return ''

  return dateFormat({
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }, locale).format(date)
}
