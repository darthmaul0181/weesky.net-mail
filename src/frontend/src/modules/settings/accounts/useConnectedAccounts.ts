import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api } from '../../../api.js'

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
}

export interface ConnectableDomain {
  id: string
  name: string
}

/** Shared with AuthContext: invalidating it is what refreshes the account switcher. */
export const CONNECTED_ACCOUNTS_KEY = ['connectedAccounts'] as const
const CONNECTABLE_DOMAINS_KEY = ['connectableDomains'] as const

/** The mail server's own refusal is never relayed, so an empty message needs one of ours. */
export const SERVER_REFUSED = 'Could not sign in to this mailbox. Check the address and the password.'
export const THROTTLED = 'Too many attempts. Wait a moment and try again.'
export const NO_LONGER_CONNECTED = 'This account is no longer connected.'

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

export function useDeleteConnectedAccount() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (id: string) => api.deleteConnectedAccount(id),
    onSettled: refreshList(client),
  })
}
