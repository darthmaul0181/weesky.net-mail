import {
  createContext, useContext, useEffect, useRef, useState, useCallback, type ReactNode,
} from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, hasSession, clearSession, setUnauthorizedHandler } from '../api.js'
import { deriveIdentity, type Account, type AccountIdentity } from '../lib/accountIdentity'
import { forgetNotificationClaim } from '../modules/mail/notify/channels'
import type { ConnectedAccount, MailAuthMode } from '../modules/settings/accounts/useConnectedAccounts'
import type { Capabilities } from '../types/capabilities'

const ACTIVE_ACCOUNT_KEY = 'mail.activeAccount'
/** The account every session starts on; the one id that can never turn out to be stale. */
export const PRIMARY_ACCOUNT_ID = 'primary'

export interface ActiveAccount {
  /** 'primary' or the connected account's GUID. */
  id: string
  email: string
  displayName: string
  isPrimary: boolean
  /** null for the primary account and for local shared mailboxes. */
  domainName: string | null
  /** false → the stored credential no longer decrypts: not switchable, and the repair it is sent
   *  to depends on `authMode`. */
  credentialsValid: boolean
  sieveSupported: boolean
  /** Which repair an unusable mailbox needs: a password, or a fresh consent at the provider. */
  authMode: MailAuthMode
}

interface AuthContextValue {
  isLoggedIn: boolean
  isAdmin: boolean
  account: Account | null
  accountLoaded: boolean
  /** What the platform wires up. Null before it loads and for a backend that predates
   *  the endpoint — every gate elsewhere reads a field `!== false` for exactly that reason. */
  capabilities: Capabilities | null
  identity: AccountIdentity | null
  /** The active account's metadata, absent until the list holding it has loaded. */
  activeAccount: ActiveAccount | null
  /** The id every query key is scoped by. Known from storage before the list loads. */
  activeAccountId: string
  /** The primary account followed by the connected ones. */
  accounts: ActiveAccount[]
  /** The list is still in flight: `activeAccountId` may yet turn out to be stale. */
  accountsLoading: boolean
  /** No-op on the current id, an unknown one, or a target whose credentials no longer work. */
  switchAccount: (id: string) => void
  /** Re-read the session flag after LoginPage completed api.login(). */
  syncFromSession: () => void
  logout: () => Promise<void>
  refreshAccount: () => Promise<void>
}

function mapRow(row: ConnectedAccount): ActiveAccount {
  return {
    id: row.id,
    email: row.email,
    displayName: row.displayName || row.email,
    isPrimary: false,
    domainName: row.domainName ?? null,
    credentialsValid: row.credentialsValid,
    sieveSupported: row.sieveSupported,
    authMode: row.authMode,
  }
}

// An older backend (or an unmocked test) throws synchronously from api.getCapabilities(), so the
// try/catch wraps the call itself, or the throw escapes as an unhandled rejection.
async function fetchCapabilities(): Promise<Capabilities | null> {
  try {
    return (await api.getCapabilities()) ?? null
  } catch {
    return null
  }
}

const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [isLoggedIn, setIsLoggedIn] = useState<boolean>(hasSession())
  const [account, setAccount] = useState<Account | null>(null)
  const [accountLoaded, setAccountLoaded] = useState(false)
  const [capabilities, setCapabilities] = useState<Capabilities | null>(null)
  const [activeAccountId, setActiveAccountId] = useState<string>(
    () => localStorage.getItem(ACTIVE_ACCOUNT_KEY) ?? PRIMARY_ACCOUNT_ID)
  const queryClient = useQueryClient()

  // The key is shared with the Connected accounts settings page, whose mutations invalidate it —
  // which is what refreshes this list when an account is added, repaired or removed.
  const { data: connectedRows, isLoading: accountsLoading } = useQuery({
    queryKey: ['connectedAccounts'],
    queryFn: () => api.getConnectedAccounts(),
    enabled: isLoggedIn,
    staleTime: 60_000,
  })

  // Bumped when a session ends; refreshAccount checks it before each late setState, so a slow
  // answer from an ended session cannot resurrect its account, isAdmin or capabilities.
  const sessionGeneration = useRef(0)

  const refreshAccount = useCallback(async () => {
    const myGeneration = sessionGeneration.current
    const current = () => sessionGeneration.current === myGeneration
    // Kicked off first, awaited last: the two are independent and the account (which gates the
    // rest of the app) must not wait on capabilities to resolve.
    const capabilitiesPromise = fetchCapabilities()
    try {
      const data = await api.getAccount()
      if (current()) setAccount(data)
    } catch {
      if (current()) setAccount(null)
    } finally {
      if (current()) setAccountLoaded(true)
    }
    const caps = await capabilitiesPromise
    if (current()) setCapabilities(caps)
  }, [])

  useEffect(() => {
    setUnauthorizedHandler(() => {
      setIsLoggedIn(false)
      setAccount(null)
      setAccountLoaded(false)
    })
    return () => setUnauthorizedHandler(null)
  }, [])

  // Whether a session was running, for the flush below. Seeded from the session flag, not a
  // "first run" boolean, which StrictMode's second mount pass would spend.
  const wasLoggedIn = useRef(isLoggedIn)

  useEffect(() => {
    if (isLoggedIn) {
      wasLoggedIn.current = true
      // eslint-disable-next-line react-hooks/set-state-in-effect -- follows the session flag, set by login, logout and a 401: loads the account, or drops it and flushes the caches
      void refreshAccount()
    } else {
      sessionGeneration.current += 1
      setAccount(null)
      setAccountLoaded(false)
      setCapabilities(null)
      // Flushed here, not in logout(), so a 401 is covered: account-scoped keys still hand one
      // session's cache to the next. resetQueries, never clear(), which detaches the observers above
      // the router (the install manifest). Only on a transition: a first-mount flush killed their reads.
      if (wasLoggedIn.current) {
        void queryClient.resetQueries()
        queryClient.getMutationCache().clear()
        forgetNotificationClaim()
        localStorage.removeItem(ACTIVE_ACCOUNT_KEY)
        setActiveAccountId(PRIMARY_ACCOUNT_ID)
      }
      wasLoggedIn.current = false
    }
  }, [isLoggedIn, refreshAccount, queryClient])

  function syncFromSession() {
    setIsLoggedIn(hasSession())
  }

  async function logout() {
    try {
      await api.logout()
    } catch {
      // best effort — the cookie may already be gone
    } finally {
      clearSession()
      setIsLoggedIn(false)
    }
  }

  const identity = account ? deriveIdentity(account) : null
  const primaryAccount: ActiveAccount | null = identity
    ? {
      id: PRIMARY_ACCOUNT_ID, email: identity.email, displayName: identity.displayName, isPrimary: true,
      domainName: null, credentialsValid: true, sieveSupported: true, authMode: 'Password',
    }
    : null
  const accounts: ActiveAccount[] = primaryAccount
    ? [primaryAccount, ...(connectedRows ?? []).map(mapRow)]
    : []
  // Unresolved rather than the primary while the list loads: the stored id is most likely valid,
  // and naming the primary as active marks its row as the one in use — a click on the row shown
  // as current would then move the user off their own mailbox.
  const activeAccount = accounts.find(a => a.id === activeAccountId)
    ?? (accountsLoading ? null : primaryAccount)

  // Only once the list is in hand, or a reload on a connected account flashes the primary. Invalid
  // credentials fall back too: the reload would reach a mailbox switchAccount refuses to open.
  useEffect(() => {
    if (!connectedRows || activeAccountId === PRIMARY_ACCOUNT_ID) return
    if (connectedRows.find(row => row.id === activeAccountId)?.credentialsValid) return
    localStorage.removeItem(ACTIVE_ACCOUNT_KEY)
    // eslint-disable-next-line react-hooks/set-state-in-effect -- the stored id is only known stale once the account list arrives, and it is corrected in storage too
    setActiveAccountId(PRIMARY_ACCOUNT_ID)
  }, [connectedRows, activeAccountId])

  function switchAccount(id: string) {
    if (id === activeAccountId) return
    const target = accounts.find(a => a.id === id)
    if (!target?.credentialsValid) return
    // The new account refetches under its own keys regardless; this is about not keeping the
    // previous mailbox's folders and messages in the cache behind it.
    queryClient.removeQueries({ queryKey: ['mail', activeAccountId] })
    setActiveAccountId(id)
    localStorage.setItem(ACTIVE_ACCOUNT_KEY, id)
  }

  return (
    <AuthContext.Provider value={{
      isLoggedIn,
      isAdmin: account?.isAdmin === true,
      account,
      accountLoaded,
      capabilities,
      identity,
      activeAccount,
      activeAccountId,
      accounts,
      accountsLoading,
      switchAccount,
      syncFromSession,
      logout,
      refreshAccount,
    }}>
      {children}
    </AuthContext.Provider>
  )
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within AuthProvider')
  return ctx
}
