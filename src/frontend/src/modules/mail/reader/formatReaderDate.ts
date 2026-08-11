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

/**
 * The phone header's date, sharing a line with the recipients: the long form above wraps that
 * line in two on its own. `dateStyle`/`timeStyle` rather than hand-picked components, so the
 * field order and the separators are the locale's rather than ours — 09/08/2026 16:45 in French,
 * 8/9/26, 4:45 PM in English. The full form stays one chevron away in the details grid.
 */
export function formatReaderDateShort(iso: string, locale: string = activeLocale()): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return ''

  return dateFormat({ dateStyle: 'short', timeStyle: 'short' }, locale).format(date)
}
