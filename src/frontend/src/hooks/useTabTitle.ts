import { useEffect } from 'react'
import { useAuth } from '../contexts/AuthContext'
import { APP_SETTING_KEYS, useAppSettings } from './useAppSettings'

/** Mirrors the instance's application name, so `index.html` can paint the tab before React runs —
    the palette's own trick. It is a name, not a secret, and it is not cleared on sign-out. */
export const APP_NAME_STORAGE_KEY = 'app_name'

/** The mailbox first: with several tabs open on several accounts, it is the only part that differs. */
export function tabTitle(email: string | null, base: string): string {
  return email ? `${email} · ${base}` : base
}

function remembered(): string | null {
  try {
    return localStorage.getItem(APP_NAME_STORAGE_KEY)
  } catch {
    return null
  }
}

/** Names the mailbox in the tab, under the administrator's application name; mounted in the shell
 * and on the login page. Waits for the account list, or a persisted connected account reads as the
 * primary; the name falls back to the last load's, then to the hostname. */
export function useTabTitle(): void {
  const { activeAccount } = useAuth()
  const { data } = useAppSettings()
  const email = activeAccount?.email ?? null
  const configured = data?.[APP_SETTING_KEYS.name]
  const base = configured || remembered() || window.location.hostname

  useEffect(() => {
    if (configured) {
      try {
        localStorage.setItem(APP_NAME_STORAGE_KEY, configured)
      } catch { /* a private window keeps the name for this load only */ }
    }
  }, [configured])

  useEffect(() => {
    document.title = tabTitle(email, base)
  }, [email, base])
}
