import i18next from 'i18next'
import { afterEach, describe, expect, it } from 'vitest'
import { activeLocale, collator, dateFormat, relativeFromNow } from './intl'

describe('intl', () => {
  afterEach(async () => { await i18next.changeLanguage('en') })

  it('follows the active i18next language', async () => {
    expect(activeLocale()).toBe('en')
    await i18next.changeLanguage('fr')
    expect(activeLocale()).toBe('fr')
  })

  // Constructing an Intl formatter is expensive and these run per list row.
  it('hands back the same formatter for the same locale and options', () => {
    const options: Intl.DateTimeFormatOptions = { day: 'numeric', month: 'short' }
    expect(dateFormat(options, 'fr')).toBe(dateFormat(options, 'fr'))
    expect(dateFormat(options, 'fr')).not.toBe(dateFormat(options, 'en'))
  })

  it('hands back the same collator for the same locale and options', () => {
    expect(collator({ sensitivity: 'base' }, 'fr')).toBe(collator({ sensitivity: 'base' }, 'fr'))
  })
})

describe('relativeFromNow', () => {
  const now = new Date('2026-08-23T12:00:00Z')

  it('reads in the past, in the largest unit that still says something', () => {
    expect(relativeFromNow('2026-08-23T10:00:00Z', now)).toBe('2 hours ago')
    expect(relativeFromNow('2026-08-21T12:00:00Z', now)).toBe('2 days ago')
    expect(relativeFromNow('2026-08-23T11:59:30Z', now)).toBe('30 seconds ago')
  })

  it('does not drift into the future on a clock a few seconds ahead', () => {
    // The server stamps the date, the browser reads it: a small skew must not print "in 3 seconds".
    expect(relativeFromNow('2026-08-23T12:00:03Z', now)).toBe('now')
  })
})
