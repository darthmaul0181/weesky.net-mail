import { createContext, useContext, useEffect, useState, useCallback, type ReactNode } from 'react'
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

  useEffect(() => {
    if (isLoggedIn) {
      refreshAccount()
    } else {
      setAccount(null)
      setAccountLoaded(false)
      // The query keys are account-scoped in shape only — the id is the constant 'primary' until
      // linked accounts ship — so folders, messages, contacts and preferences left in the cache
      // are served to whoever signs in next. It runs here, not in logout(), so the 401 path is
      // covered too and so RequireAuth has already unmounted the readers of what is being dropped.
      queryClient.clear()
      forgetNotificationClaim()
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
