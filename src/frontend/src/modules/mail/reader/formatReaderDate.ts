import { activeLocale, dateFormat } from '../../../lib/intl'

// The Date header, when the sender wrote it, spelled out without seconds. The list shows arrival,
// which is what orders it.
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

// The phone header's date: `dateStyle`/`timeStyle` so field order and separators are the locale's.
// The long form stays one chevron away in the details grid.
export function formatReaderDateShort(iso: string, locale: string = activeLocale()): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return ''

  return dateFormat({ dateStyle: 'short', timeStyle: 'short' }, locale).format(date)
}
