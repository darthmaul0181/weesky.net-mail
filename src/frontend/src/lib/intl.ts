import i18next from 'i18next'

/** The one place the active language is read for formatting. i18next is the single source of
    truth for what the interface speaks, so nothing here takes a locale from anywhere else. */
export function activeLocale(): string {
  return i18next.language || 'en'
}

const dateFormats = new Map<string, Intl.DateTimeFormat>()
const collators = new Map<string, Intl.Collator>()

/** Object.keys(options).sort() before stringifying, so `{day, month}` and `{month, day}` — the
    same options, spelled in a different order — collide on one cache entry rather than two. */
function cacheKey(locale: string, options: object): string {
  return `${locale}|${JSON.stringify(options, Object.keys(options).sort())}`
}

/** Constructing an Intl formatter is expensive and these run once per rendered list row, so the
    instances are cached per locale-and-options rather than per call. */
export function dateFormat(
  options: Intl.DateTimeFormatOptions,
  locale: string = activeLocale(),
): Intl.DateTimeFormat {
  const key = cacheKey(locale, options)
  let formatter = dateFormats.get(key)
  if (!formatter) {
    formatter = new Intl.DateTimeFormat(locale, options)
    dateFormats.set(key, formatter)
  }
  return formatter
}

export function collator(
  options: Intl.CollatorOptions = {},
  locale: string = activeLocale(),
): Intl.Collator {
  const key = cacheKey(locale, options)
  let instance = collators.get(key)
  if (!instance) {
    instance = new Intl.Collator(locale, options)
    collators.set(key, instance)
  }
  return instance
}

const relativeFormats = new Map<string, Intl.RelativeTimeFormat>()

function relativeFormat(locale: string = activeLocale()): Intl.RelativeTimeFormat {
  let formatter = relativeFormats.get(locale)
  if (!formatter) {
    formatter = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' })
    relativeFormats.set(locale, formatter)
  }
  return formatter
}

const RELATIVE_UNITS: [Intl.RelativeTimeFormatUnit, number][] = [
  ['year', 365 * 24 * 3600], ['month', 30 * 24 * 3600], ['day', 24 * 3600],
  ['hour', 3600], ['minute', 60], ['second', 1],
]

/** Formats an instant expected to be in the past, in the largest unit that still says something.
    An unparseable value comes back unchanged rather than throwing. Any future delta reads "now":
    on the one field this renders, "last used", the future can only mean a clock fault. */
export function relativeFromNow(iso: string, now: Date = new Date()): string {
  const then = new Date(iso).getTime()
  if (Number.isNaN(then)) return iso

  const seconds = Math.round((then - now.getTime()) / 1000)
  if (seconds > -5) return relativeFormat().format(0, 'second')

  const [unit, size] = RELATIVE_UNITS.find(([, s]) => Math.abs(seconds) >= s) ?? RELATIVE_UNITS[5]
  return relativeFormat().format(Math.round(seconds / size), unit)
}
