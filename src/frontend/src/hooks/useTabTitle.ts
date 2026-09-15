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

/**
 * Names the mailbox being read in the tab, under the application name an administrator set in
 * Administration › Application. Mounted in the shell and on the login page, which has no shell.
 *
 * It waits for the account list rather than falling back to the signed-in address: during that
 * window a persisted connected account would be announced as the primary. The name falls back to
 * the one the last load remembered, then to the hostname — a tab must always say something.
 */
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
