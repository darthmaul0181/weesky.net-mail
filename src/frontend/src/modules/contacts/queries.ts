import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../api.js'
import { useAccountId } from '../../hooks/useAccountId'
import { compareContacts } from './contactSearch'
import type { Contact, ContactDraft, ContactImportReport, ContactListResponse } from './contactTypes'

/** Scoped by account from the outset, like the mail keys: linking a second account later isolates
    its book instead of mixing two. */
export const contactKeys = {
  all: (accountId: string) => ['contacts', accountId] as const,
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

// Settled, not success: after a refused write the screen must fall back to the server's state
// rather than keep an optimistic list nobody stored.
function useContactMutation<TArgs, TResult = unknown>(mutationFn: (args: TArgs) => Promise<TResult>) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn,
    onSettled: () => queryClient.invalidateQueries({ queryKey: contactKeys.all(accountId) }),
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
  return useContactMutation((id: string) => api.deleteContact(id))
}

export function useSetContactFavorite() {
  return useContactMutation(
    ({ id, isFavorite }: { id: string; isFavorite: boolean }) =>
      api.setContactFavorite(id, isFavorite))
}

export function useImportContacts() {
  return useContactMutation((file: File) => api.importContacts(file) as Promise<ContactImportReport>)
}
