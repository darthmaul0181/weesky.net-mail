import i18next from 'i18next'
import { initReactI18next } from 'react-i18next'
import type { Locale } from './locale'

/** One catalogue is loaded, the active one: neither language weighs on the main bundle, and
    there is no flash of the wrong language. */
async function bundleOf(locale: Locale) {
  return locale === 'fr'
    ? (await import('../locales/fr')).default
    : (await import('../locales/en')).default
}

/** Shared with test-setup.js's synchronous init, so the two configurations cannot drift apart —
    written twice, a production-only change to escaping or the fallback would leave the suite
    silently validating semantics the app no longer runs under. */
export const I18N_OPTIONS = {
  // No fallbackLng: src/locales/parity.test.ts fails the build on a key present in one
  // catalogue and absent from the other, which is what a fallback would otherwise paper over.
  fallbackLng: false as const,
  defaultNS: 'common' as const,
  // React escapes for us; i18next doing it again turns an apostrophe into an entity.
  interpolation: { escapeValue: false },
}

export async function initI18n(locale: Locale): Promise<void> {
  await i18next.use(initReactI18next).init({
    ...I18N_OPTIONS,
    lng: locale,
    resources: { [locale]: await bundleOf(locale) },
  })
}

/** Switching language at runtime: fetch the other catalogue, then change. Adding the bundle
    before changeLanguage is what stops one render landing between the two with no strings. */
export async function loadLocale(locale: Locale): Promise<void> {
  if (i18next.language === locale) return

  if (!i18next.hasResourceBundle(locale, 'common')) {
    const bundle = await bundleOf(locale)
    for (const [namespace, resources] of Object.entries(bundle)) {
      i18next.addResourceBundle(locale, namespace, resources)
    }
  }

  await i18next.changeLanguage(locale)
}
