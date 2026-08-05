import {
  createContext, useContext, useEffect, useRef, useState, useCallback, type ReactNode,
} from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, hasSession, clearSession, setUnauthorizedHandler, setIsAdmin } from '../api.js'
import { deriveIdentity, type Account, type AccountIdentity } from '../lib/accountIdentity'
import { forgetNotificationClaim } from '../modules/mail/notify/channels'
import type { MailAuthMode } from '../modules/settings/accounts/useConnectedAccounts'

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

interface ConnectedAccountRow {
  id: string
  email: string
  displayName: string
  domainId: string | null
  domainName: string | null
  sieveSupported: boolean
  credentialsValid: boolean
  creationDate: string
  authMode: MailAuthMode
}

interface AuthContextValue {
  isLoggedIn: boolean
  isAdmin: boolean
  account: Account | null
  accountLoaded: boolean
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

function mapRow(row: ConnectedAccountRow): ActiveAccount {
  return {
    id: row.id,
    email: row.email,
    displayName: row.displayName || row.email,
    isPrimary: false,
    domainName: row.domainName,
    credentialsValid: row.credentialsValid,
    sieveSupported: row.sieveSupported,
    authMode: row.authMode,
  }
}

const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [isLoggedIn, setIsLoggedIn] = useState<boolean>(hasSession())
  const [account, setAccount] = useState<Account | null>(null)
  const [accountLoaded, setAccountLoaded] = useState(false)
  const [activeAccountId, setActiveAccountId] = useState<string>(
    () => localStorage.getItem(ACTIVE_ACCOUNT_KEY) ?? PRIMARY_ACCOUNT_ID)
  const queryClient = useQueryClient()

  // The key is shared with the Connected accounts settings page, whose mutations invalidate it —
  // which is what refreshes this list when an account is added, repaired or removed.
  const { data: connectedRows, isLoading: accountsLoading } = useQuery<ConnectedAccountRow[]>({
    queryKey: ['connectedAccounts'],
    queryFn: () => api.getConnectedAccounts(),
    enabled: isLoggedIn,
    staleTime: 60_000,
  })

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

  // Only once the list is in hand: run while the query is in flight and a reload on a connected
  // account clears the stored id and flashes the primary mailbox before jumping back. Invalid
  // credentials fall back too — the reload path would otherwise reach the broken mailbox that
  // switchAccount refuses to open, and answer every folder and message request with a failure.
  useEffect(() => {
    if (!connectedRows || activeAccountId === PRIMARY_ACCOUNT_ID) return
    if (connectedRows.find(row => row.id === activeAccountId)?.credentialsValid) return
    localStorage.removeItem(ACTIVE_ACCOUNT_KEY)
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
