import { useEffect } from 'react'
import { useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../../api.js'
import type { AddToast } from '../../../hooks/useToasts'
import { useListLoadState, type ListQuery } from '../../../hooks/useListLoadState'
import { invalidateOnSettled } from '../invalidateOnSettled'
import type { Quota } from '../../../types/account'
import type {
  AdminDomain, AdminDomainPayload, AdminUser, AdminUserPayload, VirtualDomain,
} from './adminTypes'

/** The three admin lists share one root: a user or domain write reaches every list that shows it. */
const ADMIN_KEY = ['admin'] as const
const USERS_KEY = [...ADMIN_KEY, 'users'] as const
const DOMAINS_KEY = [...ADMIN_KEY, 'domains'] as const
const VIRTUAL_DOMAINS_KEY = [...ADMIN_KEY, 'virtualDomains'] as const
const quotaKey = (userId: number) => [...ADMIN_KEY, 'userQuota', userId] as const

export function useAdminUsers() {
  return useQuery<AdminUser[]>({
    queryKey: USERS_KEY,
    queryFn: async () => (await api.adminGetUsers()) ?? [],
  })
}

export function useAdminDomains() {
  return useQuery<AdminDomain[]>({
    queryKey: DOMAINS_KEY,
    queryFn: async () => (await api.adminGetDomains()) ?? [],
  })
}

export function useAdminVirtualDomains() {
  return useQuery<VirtualDomain[]>({
    queryKey: VIRTUAL_DOMAINS_KEY,
    queryFn: async () => (await api.adminGetVirtualDomains()) ?? [],
  })
}

/** One query per user, so a user already on screen keeps the gauge it has. A mailbox without a
    quota answer is a normal state drawn as a dash: no retry, and no fan-out on every window focus. */
export function useAdminUserQuotas(userIds: readonly number[]) {
  const results = useQueries({
    queries: userIds.map(id => ({
      queryKey: quotaKey(id),
      queryFn: () => api.adminGetUserQuota(id),
      retry: false,
      refetchOnWindowFocus: false,
    })),
  })
  return new Map<number, Quota>(
    userIds.flatMap((id, i) => { const quota = results[i]?.data; return quota ? [[id, quota]] : [] }))
}

/** `useListLoadState` with one toast per failure episode, naming the list. While `primary` (the
 * tab's other list) has failed too, this toast is withheld: the primary's note already says it. */
export function useListLoad(
  query: ListQuery, addToast: AddToast, failedMessage: string, primary?: ListQuery,
) {
  const { firstLoad, failed } = useListLoadState([query])
  const primaryFailed = useListLoadState(primary ? [primary] : []).failed
  useEffect(() => {
    if (failed && !primaryFailed) addToast(failedMessage, 'error')
  }, [failed, primaryFailed, addToast, failedMessage])
  return firstLoad
}

// A user's domain name, a deleted owner, the domains the user dialog offers: one write changes
// what several lists draw, so it refreshes them all. Only the lists on screen are refetched.
function useAdminWrite<TVariables, TData>(mutationFn: (variables: TVariables) => Promise<TData>) {
  const client = useQueryClient()
  return useMutation({ mutationFn, onSettled: invalidateOnSettled(client, ADMIN_KEY) })
}

export const useCreateAdminUser = () =>
  useAdminWrite((user: AdminUserPayload) => api.adminCreateUser(user))

export const useUpdateAdminUser = () =>
  useAdminWrite(({ id, user }: { id: number; user: AdminUserPayload }) => api.adminUpdateUser(id, user))

export const useDeleteAdminUser = () =>
  useAdminWrite((id: number) => api.adminDeleteUser(id))

export const useCreateAdminDomain = () =>
  useAdminWrite((domain: AdminDomainPayload) => api.adminCreateDomain(domain))

export const useUpdateAdminDomain = () =>
  useAdminWrite(({ id, domain }: { id: string; domain: AdminDomainPayload }) => api.adminUpdateDomain(id, domain))

export const useDeleteAdminDomain = () =>
  useAdminWrite(({ id, deleteAliases }: { id: string; deleteAliases: boolean }) =>
    api.adminDeleteDomain(id, deleteAliases))

interface OwnerChange { domainId: string; userId: number }

// An owner change touches that one list alone. The answer is written in at once, so the chip
// moves with the click, and the list is still re-read once settled.
function useOwnerWrite<TData>(
  mutationFn: (change: OwnerChange) => Promise<TData>,
  apply: (domain: VirtualDomain, change: OwnerChange, data: TData) => VirtualDomain,
) {
  const client = useQueryClient()
  return useMutation({
    mutationFn,
    onSuccess: (data: TData, change: OwnerChange) => {
      client.setQueryData<VirtualDomain[]>(VIRTUAL_DOMAINS_KEY, list =>
        list?.map(d => (d.domainId === change.domainId ? apply(d, change, data) : d)))
    },
    onSettled: invalidateOnSettled(client, VIRTUAL_DOMAINS_KEY),
  })
}

export const useAddVirtualDomainOwner = () =>
  useOwnerWrite(
    ({ domainId, userId }) => api.adminAddVirtualDomainOwner(domainId, userId),
    (_domain, _change, updated) => updated,
  )

export const useRemoveVirtualDomainOwner = () =>
  useOwnerWrite(
    ({ domainId, userId }) => api.adminRemoveVirtualDomainOwner(domainId, userId),
    (domain, { userId }) => ({ ...domain, owners: domain.owners.filter(o => o.ownerId !== userId) }),
  )
