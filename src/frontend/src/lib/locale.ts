/**
 * Which language the interface speaks, resolved once from four sources.
 *
 * The chain is stored preference → localStorage mirror → browser → English. `auto` and any value
 * this build does not recognise fall through at every link alike: both mean "this link has no
 * answer", and treating an unknown value as an answer would strand an account on a locale the
 * build cannot render.
 */
export const SUPPORTED_LOCALES = ['en', 'fr'] as const
export type Locale = typeof SUPPORTED_LOCALES[number]

/**
 * The mirror covers the two cases the server cannot: the login page, which has no session and so
 * no preferences to read, and the first render, which precedes the answer to GET /api/Preferences.
 * It is written whenever preferences arrive and deliberately never cleared on sign-out — clearing
 * it would send the login page back to the browser's language every time.
 */
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
    .map(tag => asLocale(String(tag).split('-')[0].toLowerCase()))
    .find(Boolean)

  return asLocale(stored) ?? asLocale(mirrored) ?? fromBrowser ?? 'en'
}

export function readLanguageMirror(): string | undefined {
  return localStorage.getItem(LANGUAGE_MIRROR_KEY) ?? undefined
}

export function writeLanguageMirror(value: string): void {
  localStorage.setItem(LANGUAGE_MIRROR_KEY, value)
}
