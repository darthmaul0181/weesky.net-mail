import { useEffect } from 'react'
import { useAuth } from '../contexts/AuthContext'

/**
 * What the tab said before we touched it — `index.html` sets it to the hostname, which is what
 * distinguishes the dev deployment from production at a glance. Read once at module load, so a
 * re-render can never prefix an already-prefixed title.
 */
const BASE = typeof document === 'undefined' ? '' : document.title

/** The mailbox first: with several tabs open on several accounts, it is the only part that differs. */
export function tabTitle(email: string | null, base: string): string {
  return email ? `${email} · ${base}` : base
}

/**
 * Names the mailbox being read in the tab title. Mounted in the shell, so it follows a switch
 * from anywhere. It waits for the account list rather than falling back to the signed-in
 * address: during that window a persisted connected account would be announced as the primary.
 */
export function useTabTitle(): void {
  const { activeAccount } = useAuth()
  const email = activeAccount?.email ?? null

  useEffect(() => {
    document.title = tabTitle(email, BASE)
  }, [email])
}
