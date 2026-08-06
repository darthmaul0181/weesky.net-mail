import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api } from '../../../api.js'

/** How a row authenticates to its mail server. Frozen at creation on the backend. */
export type MailAuthMode = 'Password' | 'OAuth2'

export interface ConnectedAccount {
  id: string
  email: string
  displayName: string
  /** null for a local shared mailbox. */
  domainId: string | null
  domainName: string | null
  sieveSupported: boolean
  credentialsValid: boolean
  creationDate: string
  authMode: MailAuthMode
}

export interface ConnectableDomain {
  id: string
  name: string
  authMode: MailAuthMode
}

/** What Start answers: the provider page to go to, and the handle the callback brings back. */
export interface OAuthStart {
  authorizationUrl: string
  state: string
}

/** Shared with AuthContext: invalidating it is what refreshes the account switcher. */
export const CONNECTED_ACCOUNTS_KEY = ['connectedAccounts'] as const
const CONNECTABLE_DOMAINS_KEY = ['connectableDomains'] as const

/** The mail server's own refusal is never relayed, so an empty message needs one of ours. */
export const SERVER_REFUSED = 'Could not sign in to this mailbox. Check the address and the password.'
export const THROTTLED = 'Too many attempts. Wait a moment and try again.'
export const NO_LONGER_CONNECTED = 'This account is no longer connected.'
/** A bodyless refusal from the OAuth endpoints: there is no password to check, so SERVER_REFUSED
 *  would name the one thing the user cannot act on. */
export const PROVIDER_REFUSED = 'Could not reach the sign-in provider. Try again in a moment.'
export const HANDSHAKE_GONE = 'That sign-in took too long. Try connecting again.'

function statusOf(error: unknown): number | null {
  const status = (error as { status?: unknown } | null)?.status
  return typeof status === 'number' ? status : null
}

export function errorText(error: unknown, fallback = SERVER_REFUSED): string {
  const status = statusOf(error)
  // The rate limiter answers with no body at all, and 404 answers with the bare code
  // `account_not_found` — both would otherwise read as "your password is wrong".
  if (status === 429) return THROTTLED
  if (status === 404) return NO_LONGER_CONNECTED
  return error instanceof Error && error.message ? error.message : fallback
}

/** Complete's 404 is the handshake being unknown, expired or already used — never the account.
 *  A first-time attach has no account to have been disconnected, and a reconnect's row is still
 *  there; both are told the truth, which is that the consent has to be started again. */
export function oauthCompleteErrorText(error: unknown): string {
  return statusOf(error) === 404 ? HANDSHAKE_GONE : errorText(error, PROVIDER_REFUSED)
}

export function useConnectedAccounts() {
  return useQuery<ConnectedAccount[]>({
    queryKey: CONNECTED_ACCOUNTS_KEY,
    queryFn: () => api.getConnectedAccounts(),
  })
}

export function useConnectableDomains() {
  return useQuery<ConnectableDomain[]>({
    queryKey: CONNECTABLE_DOMAINS_KEY,
    queryFn: () => api.getConnectableDomains(),
    staleTime: 5 * 60 * 1000,
  })
}

// onSettled, not onSuccess: a refused write must leave the screen on server state rather than
// on an optimistic lie.
function refreshList(client: QueryClient) {
  return () => { client.invalidateQueries({ queryKey: CONNECTED_ACCOUNTS_KEY }) }
}

export function useConnectAccount() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: ({ domainId, email, password }: {
      domainId: string | null; email: string; password: string
    }) => api.connectAccount(domainId, email, password),
    onSettled: refreshList(client),
  })
}

export function useUpdateConnectedAccountPassword() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: ({ id, password }: { id: string; password: string }) =>
      api.updateConnectedAccountPassword(id, password),
    onSettled: refreshList(client),
  })
}

// No invalidation: nothing has changed yet, and the page is about to be replaced by the
// provider's.
export function useStartOAuthConnect() {
  return useMutation({
    mutationFn: (target: { domainId?: string; accountId?: string }): Promise<OAuthStart> =>
      api.startOAuthConnect(target),
  })
}

export function useCompleteOAuthConnect() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (state: string): Promise<ConnectedAccount> => api.completeOAuthConnect(state),
    onSettled: refreshList(client),
  })
}

/** The one place the browser leaves for the provider — stubbed in tests. */
export const leaveTo = (url: string) => { window.location.assign(url) }

export function useDeleteConnectedAccount() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (id: string) => api.deleteConnectedAccount(id),
    onSettled: refreshList(client),
  })
}
