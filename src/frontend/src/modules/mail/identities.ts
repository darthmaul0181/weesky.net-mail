import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import i18next from 'i18next'
import { ApiError, api } from '../../api.js'
import { useAccountId, useComposeAccountId } from '../../hooks/useAccountId'
import type { IdentityWrite, SendingIdentity } from './api/mailTypes'
import { mailKeys } from './mailKeys'

/** The curated From list. Long staleTime: it changes only from Settings, which invalidates it.
    Exported as options so an imperative `ensureQueryData` shares the hook's one definition; the
    `select` stays on the hook, since `ensureQueryData` answers the raw response either way. */
export const identitiesQueryOptions = (accountId: string) => ({
  queryKey: mailKeys.identities(accountId),
  queryFn: () => api.getIdentities({ accountId }),
  staleTime: 5 * 60_000,
})

/** `pinnedAccountId` is the composer's — the From list has to be the bound account's, or the
    picker offers an address the send's account does not own and every attempt is refused. */
export function useIdentities(pinnedAccountId?: string) {
  const accountId = useComposeAccountId(pinnedAccountId)

  return useQuery({
    ...identitiesQueryOptions(accountId),
    select: (data): SendingIdentity[] => data.identities,
  })
}

export function useReplaceIdentities() {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (identities: IdentityWrite[]) => api.putIdentities(identities, { accountId }),
    // Settled, not success: after a refused PUT the page must fall back to the server's state.
    onSettled: () => queryClient.invalidateQueries({ queryKey: mailKeys.identities(accountId) }),
  })
}

/** The senders whose remote images load without asking. Long staleTime: it changes only from
    the reader, which invalidates it. The Set is built by `select` so the reader can test one
    address per render without rebuilding it. */
export function useTrustedSenders() {
  const accountId = useAccountId()

  return useQuery({
    queryKey: mailKeys.trustedSenders(accountId),
    queryFn: () => api.getTrustedSenders(),
    staleTime: 5 * 60_000,
    select: (addresses): Set<string> => new Set(addresses),
  })
}

/** One mutation for both directions — the two differ by a verb, not by a workflow. */
export function useTrustSender(onError?: (message: string) => void) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ address, trusted }: { address: string; trusted: boolean }) =>
      trusted ? api.trustSender(address) : api.untrustSender(address),
    // A 400 is a refusal worded to be read; any other statusText tells the reader nothing, so it gets
    // the generic message. A 401 is silent: api.ts is already redirecting to /login.
    onError: (error, { trusted }) => {
      if (error instanceof ApiError && error.status === 401) return
      const generic = i18next.t(
        trusted ? 'mail:mutations.trustFailed' : 'mail:mutations.untrustFailed')
      onError?.(error instanceof ApiError && error.status === 400 ? error.message : generic)
    },
    // Settled, not success: a refused call must leave the reader showing the server's state.
    onSettled: () => queryClient.invalidateQueries({ queryKey: mailKeys.trustedSenders(accountId) }),
  })
}

/** Five minutes for the readers; a screen managing the list passes its own `staleTime`. */
export function useAliases(enabled = true, { staleTime = 5 * 60_000 }: { staleTime?: number } = {}) {
  const accountId = useAccountId()

  return useQuery({
    queryKey: mailKeys.aliases(accountId),
    queryFn: () => api.getAliases(),
    enabled,
    staleTime,
  })
}
