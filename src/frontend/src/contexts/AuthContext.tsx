import {
  createContext, useContext, useEffect, useRef, useState, useCallback, type ReactNode,
} from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { api, hasSession, clearSession, setUnauthorizedHandler, setIsAdmin } from '../api.js'
import { deriveIdentity, type Account, type AccountIdentity } from '../lib/accountIdentity'
import { forgetNotificationClaim } from '../modules/mail/notify/channels'

export interface ActiveAccount {
  id: 'primary'
  email: string
  displayName: string
  isPrimary: true
}

interface AuthContextValue {
  isLoggedIn: boolean
  isAdmin: boolean
  account: Account | null
  accountLoaded: boolean
  identity: AccountIdentity | null
  /** The account whose mail context is active. Primary only until sub-project 2. */
  activeAccount: ActiveAccount | null
  /** All linked accounts. Length 1 until sub-project 2. */
  accounts: ActiveAccount[]
  /** Re-read the session flag after LoginPage completed api.login(). */
  syncFromSession: () => void
  logout: () => Promise<void>
  refreshAccount: () => Promise<void>
}

const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [isLoggedIn, setIsLoggedIn] = useState<boolean>(hasSession())
  const [account, setAccount] = useState<Account | null>(null)
  const [accountLoaded, setAccountLoaded] = useState(false)
  const queryClient = useQueryClient()

  const refreshAccount = useCallback(async () => {
    try {
      const data: Account = await api.getAccount()
      setAccount(data)
      setIsAdmin(data?.isAdmin === true)
    } catch {
      setAccount(null)
    } finally {
      setAccountLoaded(true)
    }
  }, [])

  useEffect(() => {
    setUnauthorizedHandler(() => {
      setIsLoggedIn(false)
      setAccount(null)
      setAccountLoaded(false)
    })
    return () => setUnauthorizedHandler(null)
  }, [])

  // What the effect below flushes on is a session *ending*, so it has to know whether one was
  // running. Seeded from the session flag rather than from a "first run" boolean: the question is
  // whether there was a session to end, not how many times the effect has run, and StrictMode's
  // second mount pass would spend a first-run flag on the mount itself and then clear on a mount
  // after all.
  const wasLoggedIn = useRef(isLoggedIn)

  useEffect(() => {
    if (isLoggedIn) {
      wasLoggedIn.current = true
      refreshAccount()
    } else {
      setAccount(null)
      setAccountLoaded(false)
      // The query keys are account-scoped in shape only — the id is the constant 'primary' until
      // linked accounts ship — so folders, messages, contacts and preferences left in the cache
      // are served to whoever signs in next. It runs here, not in logout(), so the 401 path is
      // covered too.
      //
      // resetQueries, not clear(): clear() *removes* every query, and an observer whose query
      // leaves the cache is never told — it keeps its last result and never sees another change.
      // RequireAuth unmounts the authenticated readers before this effect runs, but the install
      // manifest is mounted above the router and outlives every session, so clear() left it
      // observing a detached query and blind to the settings for the rest of the tab. reset drops
      // the data of every query while leaving them in the cache, and refetches the active ones
      // only — the readers RequireAuth just unmounted are inactive, so nothing doomed is refetched.
      // The mutation cache is emptied by hand, the one thing clear() did that reset does not.
      //
      // Only on the transition, never on a logged-out first mount: there is nothing cached to
      // flush yet, and flushing indiscriminately destroyed the in-flight queries that siblings
      // mounted above the router had already started — the app-settings read behind the install
      // manifest among them, which left /login unable to offer installation at all.
      if (wasLoggedIn.current) {
        queryClient.resetQueries()
        queryClient.getMutationCache().clear()
        forgetNotificationClaim()
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
  const activeAccount: ActiveAccount | null = identity
    ? { id: 'primary', email: identity.email, displayName: identity.displayName, isPrimary: true }
    : null

  return (
    <AuthContext.Provider value={{
      isLoggedIn,
      isAdmin: account?.isAdmin === true,
      account,
      accountLoaded,
      identity,
      activeAccount,
      accounts: activeAccount ? [activeAccount] : [],
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
