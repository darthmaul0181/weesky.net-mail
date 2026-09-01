import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api } from '../../api.js'
import { useAccountId } from '../../hooks/useAccountId'
import { compareContacts } from './contactSearch'
import type {
  Contact, ContactDetail, ContactDraft, ContactImportReport, ContactListResponse,
} from './contactTypes'
import type { ContactGroup, ContactGroupsResponse } from './contactGroupTypes'

/** Scoped by account from the outset, like the mail keys: linking a second account later isolates
    its book instead of mixing two. */
export const contactKeys = {
  all: (accountId: string) => ['contacts', accountId] as const,
  /** Under `all`, so the invalidation every write already fires refreshes the open card too. */
  detail: (accountId: string, id: string) => ['contacts', accountId, id] as const,
  photo: (accountId: string, id: string) => ['contacts', accountId, id, 'photo'] as const,
}

export const contactGroupKeys = {
  all: (accountId: string) => ['contactGroups', accountId] as const,
}

/** Shared by the three contact mutations that change group membership by construction — delete
    drops a member, import can create one — and by every group mutation itself. */
function invalidateBook(queryClient: QueryClient, accountId: string) {
  queryClient.invalidateQueries({ queryKey: contactKeys.all(accountId) })
  queryClient.invalidateQueries({ queryKey: contactGroupKeys.all(accountId) })
}

/**
 * The whole book, cached. Sorted in `select`, so the page and the composer read one already-
 * ordered list rather than each sorting its own copy. The reader passes false when its
 * contact-trust setting is off, so an account that never opens Contacts pays nothing.
 */
export function useContacts(enabled = true) {
  const accountId = useAccountId()

  return useQuery({
    queryKey: contactKeys.all(accountId),
    queryFn: () => api.getContacts() as Promise<ContactListResponse>,
    staleTime: 5 * 60_000,
    select: (data): Contact[] => [...data.contacts].sort(compareContacts),
    enabled,
  })
}

/**
 * One contact's whole card. Only the open one is fetched: the list carries what a tile needs, and
 * hauling every contact's phones, notes and postal addresses through it would make the book pay
 * for a column that shows one.
 */
export function useContact(id: string | null) {
  const accountId = useAccountId()

  return useQuery({
    queryKey: contactKeys.detail(accountId, id ?? ''),
    queryFn: () => api.getContact(id) as Promise<ContactDetail>,
    enabled: id != null,
    staleTime: 5 * 60_000,
  })
}

/** The avatar's bytes, asked for only once the card says there are some. */
export function useContactPhoto(id: string | null, hasPhoto: boolean) {
  const accountId = useAccountId()

  return useQuery({
    queryKey: contactKeys.photo(accountId, id ?? ''),
    queryFn: () => api.getContactPhoto(id) as Promise<Blob>,
    enabled: hasPhoto && id != null,
    staleTime: 5 * 60_000,
  })
}

// Settled, not success: after a refused write the screen must fall back to the server's state
// rather than keep an optimistic list nobody stored. `invalidatesGroups` is for the three writes
// that change group membership by construction — deleting a contact drops it from its groups,
// importing can create members — and routes through the shared `invalidateBook` helper.
function useContactMutation<TArgs, TResult = unknown>(
  mutationFn: (args: TArgs) => Promise<TResult>,
  invalidatesGroups = false,
) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn,
    onSettled: () => invalidatesGroups
      ? invalidateBook(queryClient, accountId)
      : queryClient.invalidateQueries({ queryKey: contactKeys.all(accountId) }),
  })
}

export function useCreateContact() {
  return useContactMutation((contact: ContactDraft) => api.createContact(contact))
}

export function useUpdateContact() {
  return useContactMutation(
    ({ id, contact }: { id: string; contact: ContactDraft }) => api.updateContact(id, contact))
}

export function useDeleteContact() {
  return useContactMutation((id: string) => api.deleteContact(id), true)
}

export function useSetContactFavorite() {
  return useContactMutation(
    ({ id, isFavorite }: { id: string; isFavorite: boolean }) =>
      api.setContactFavorite(id, isFavorite))
}

export function useDeleteContacts() {
  return useContactMutation((ids: string[]) => api.deleteContacts(ids), true)
}

export function useSetContactsFavorite() {
  return useContactMutation(
    ({ ids, isFavorite }: { ids: string[]; isFavorite: boolean }) =>
      api.setContactsFavorite(ids, isFavorite))
}

export function useImportContacts() {
  return useContactMutation(
    (file: File) => api.importContacts(file) as Promise<ContactImportReport>, true)
}

/**
 * The whole group list, cached. A write to one group changes every contact's chip set, so the
 * page and the composer read one already-fetched truth rather than each polling their own.
 */
export function useContactGroups(enabled = true) {
  const accountId = useAccountId()

  return useQuery({
    queryKey: contactGroupKeys.all(accountId),
    queryFn: () => api.getContactGroups() as Promise<ContactGroupsResponse>,
    staleTime: 5 * 60_000,
    select: (data) => data.groups,
    enabled,
  })
}

/** Group writes invalidate both keys — the group count on a contact changes its chips.
    onSettled, never onSuccess. */
function useContactGroupMutation<TArgs, TResult = unknown>(
  mutationFn: (args: TArgs) => Promise<TResult>,
) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn,
    onSettled: () => invalidateBook(queryClient, accountId),
  })
}

export function useCreateContactGroup() {
  return useContactGroupMutation(
    (name: string) => api.createContactGroup(name) as Promise<ContactGroup>)
}

export function useRenameContactGroup() {
  return useContactGroupMutation(
    ({ id, name }: { id: string; name: string }) => api.renameContactGroup(id, name))
}

export function useDeleteContactGroup() {
  return useContactGroupMutation((id: string) => api.deleteContactGroup(id))
}

export function useAddContactGroupMembers() {
  return useContactGroupMutation(
    ({ id, contactIds }: { id: string; contactIds: string[] }) =>
      api.addContactGroupMembers(id, contactIds))
}

export function useRemoveContactGroupMembers() {
  return useContactGroupMutation(
    ({ id, contactIds }: { id: string; contactIds: string[] }) =>
      api.removeContactGroupMembers(id, contactIds))
}
