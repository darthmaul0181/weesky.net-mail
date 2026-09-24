import { readStored, writeStored } from './safeStorage'

/** The interface language: stored preference → localStorage mirror → browser → English. `auto` and
 * any unknown value fall through alike: an unknown one taken as an answer would strand an account
 * on a locale the build cannot render. */
export const SUPPORTED_LOCALES = ['en', 'fr'] as const
export type Locale = typeof SUPPORTED_LOCALES[number]

/** Covers what the server cannot: the login page and the render before GET /api/Preferences.
 * Never cleared on sign-out, or the login page would revert to the browser's language. */
export const LANGUAGE_MIRROR_KEY = 'ui_language'

function asLocale(value: string | undefined): Locale | undefined {
  return SUPPORTED_LOCALES.includes(value as Locale) ? value as Locale : undefined
}

export function resolveLocale(
  stored: string | undefined,
  mirrored: string | undefined,
  preferred: readonly string[],
): Locale {
  const fromBrowser = preferred
    .map(tag => asLocale(String(tag).split('-')[0]?.toLowerCase()))
    .find(Boolean)

  return asLocale(stored) ?? asLocale(mirrored) ?? fromBrowser ?? 'en'
}

export function readLanguageMirror(): string | undefined {
  return readStored(LANGUAGE_MIRROR_KEY) ?? undefined
}

export function writeLanguageMirror(value: string): void {
  writeStored(LANGUAGE_MIRROR_KEY, value)
}
