import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { useAuth } from './AuthContext'
import { languageOf, usePreferences, useSetPreference, PREFERENCE_KEYS } from '../hooks/usePreferences'
import { loadLocale } from '../lib/i18n'
import {
  readLanguageMirror,
  resolveLocale,
  writeLanguageMirror,
  type Locale,
} from '../lib/locale'

interface LocaleContextValue {
  /** What the interface actually speaks. */
  locale: Locale
  /** The stored choice, `auto` included — what the settings radio reflects. */
  preference: string
  setPreference: (value: string) => void
  saving: boolean
}

const LocaleContext = createContext<LocaleContextValue | null>(null)

export function LocaleProvider({ children }: { children: ReactNode }) {
  const { isLoggedIn } = useAuth()
  const { data: preferences } = usePreferences({ enabled: isLoggedIn })
  const setPreferenceMutation = useSetPreference()

  // The optimism lives in useSetPreference's onMutate, on the shared ['preferences'] cache — not
  // here. This reads it back exactly the way every other consumer of usePreferences() does, so a
  // refused write rolls back through the same cache this derives from, instead of through a
  // second, provider-local notion of "pending" that a failure would have had to unwind by hand.
  const preference = preferences ? languageOf(preferences) : 'auto'
  const [locale, setLocale] = useState<Locale>(
    () => resolveLocale(undefined, readLanguageMirror(), navigator.languages),
  )

  useEffect(() => {
    const resolved = resolveLocale(
      preferences ? preference : undefined,
      readLanguageMirror(),
      navigator.languages,
    )
    // The mirror stores the *preference*, not the resolved locale: an account on "auto" that
    // moves to a French-configured machine must follow that machine, and mirroring `fr` would
    // pin it to the language of the machine it last signed in from.
    if (preferences) writeLanguageMirror(preference)
    if (resolved === locale) return

    // `current` guards against two rapid preference changes resolving out of order — without it,
    // an earlier catalogue landing after a later one would leave `locale` behind what i18next
    // actually holds. A rejected import — a deploy having rotated the chunk hashes out from under
    // an open tab — reloads instead: the preference is already saved server-side and mirrored, so
    // a fresh index.html fetches the current hashes and paints the language the user asked for.
    let current = true
    void loadLocale(resolved)
      .then(() => { if (current) setLocale(resolved) })
      .catch(() => { if (current) location.reload() })
    return () => { current = false }
  }, [preferences, preference, locale])

  useEffect(() => {
    document.documentElement.lang = locale
  }, [locale])

  function setPreference(value: string) {
    setPreferenceMutation.mutate({ key: PREFERENCE_KEYS.language, value })
  }

  return (
    <LocaleContext.Provider
      value={{ locale, preference, setPreference, saving: setPreferenceMutation.isPending }}
    >
      {children}
    </LocaleContext.Provider>
  )
}

export function useLocale(): LocaleContextValue {
  const ctx = useContext(LocaleContext)
  if (!ctx) throw new Error('useLocale must be used within LocaleProvider')
  return ctx
}
