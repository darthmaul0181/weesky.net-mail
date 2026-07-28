import { useQueryClient } from '@tanstack/react-query'
import { api } from '../../api.js'
import { useAccountId } from '../../hooks/useAccountId'
import type { CaptureCandidate } from './captureModel'
import { contactKeys } from './queries'
import type { Contact } from './contactTypes'

/**
 * Creating and un-creating captured contacts. Deliberately not `useMutation`: both halves are
 * started by the composer and finish after it has navigated away — the create resolves during the
 * unmount, and the undo is clicked from a toast the composer no longer owns.
 */
export function useCaptureContacts() {
  const queryClient = useQueryClient()
  const accountId = useAccountId()

  const invalidate = () =>
    queryClient.invalidateQueries({ queryKey: contactKeys.all(accountId) })

  /** Every failure is swallowed: the message is already gone, and a refusal here is not the
      user's problem. */
  async function create(candidates: CaptureCandidate[]): Promise<Contact[]> {
    const results = await Promise.allSettled(candidates.map(candidate =>
      api.createContact({
        firstName: candidate.firstName,
        lastName: candidate.lastName,
        nickname: null,
        isFavorite: false,
        addresses: [candidate.address],
        source: 'captured',
      }) as Promise<Contact>))

    const created = results.flatMap(r => r.status === 'fulfilled' ? [r.value] : [])
    if (created.length > 0) await invalidate()
    return created
  }

  /** Answers whether every deletion landed — an undo was asked for, so its failure is spoken. */
  async function remove(ids: string[]): Promise<boolean> {
    const results = await Promise.allSettled(ids.map(id => api.deleteContact(id)))
    await invalidate()
    return results.every(r => r.status === 'fulfilled')
  }

  return { create, remove }
}
