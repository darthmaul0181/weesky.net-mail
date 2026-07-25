import { createContext, useContext, useEffect, useState, useCallback, type ReactNode } from 'react'
import { api, hasSession, clearSession, setUnauthorizedHandler, setIsAdmin } from '../api.js'
import { deriveIdentity, type Account, type AccountIdentity } from '../lib/accountIdentity'

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
    }
  }, [isLoggedIn, refreshAccount])

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
