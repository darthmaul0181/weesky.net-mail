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
