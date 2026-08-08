import i18next from 'i18next'
import { afterEach, describe, expect, it } from 'vitest'
import { activeLocale, collator, dateFormat } from './intl'

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
