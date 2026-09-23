import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError } from '../../api.js'
import type { AddToast } from '../../hooks/useToasts'
import { apiErrorMessage } from '../../lib/apiErrorMessage'
import type { ContactScope } from './ContactScopes'
import { groupIdOf } from './ContactScopes'
import { displayNameOf } from './contactName'
import type { ContactDragPayload } from './dragContacts'
import type { Contact, ContactDraft } from './contactTypes'
import type { ContactGroup } from './contactGroupTypes'
import {
  useAddContactGroupMembers, useCreateContact, useDeleteContact, useDeleteContacts,
  useRemoveContactGroupMembers, useSetContactFavorite, useSetContactsFavorite, useUpdateContact,
} from './queries'

interface Options {
  edited: Contact | null
  selected: Contact | null
  openGroup: ContactGroup | null
  seededHash: string | undefined
  editorKey: string | null
  addToast: AddToast
  onSaved: () => void
  onDeletedOpen: (id: string) => void
  onReloaded: () => void
  refetchDetail: () => Promise<{ isError: boolean }>
}

/** The contact writes — save, delete, favorite, the bulk and drag actions, and group membership —
    with the toasts and the save-conflict state that go with them. */
export function useContactActions({
  edited, selected, openGroup, seededHash, editorKey, addToast, onSaved, onDeletedOpen, onReloaded,
  refetchDetail,
}: Options) {
  const { t } = useTranslation('contacts')
  const createContact = useCreateContact()
  const updateContact = useUpdateContact()
  const deleteContact = useDeleteContact()
  const deleteMany = useDeleteContacts()
  const setFavorite = useSetContactFavorite()
  const setManyFavorite = useSetContactsFavorite()
  const addMembers = useAddContactGroupMembers()
  const removeMembers = useRemoveContactGroupMembers()

  const [pendingDelete, setPendingDelete] = useState<Contact | null>(null)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [conflict, setConflict] = useState(false)

  // A refusal belongs to the form it happened in: opening another contact's editor, or coming
  // back to a fresh one, must not inherit it (render-time reset, the MailLayout pattern).
  const [errorKey, setErrorKey] = useState(editorKey)
  if (editorKey !== errorKey) {
    setErrorKey(editorKey)
    setSaveError(null)
    setConflict(false)
  }

  async function save(draft: ContactDraft) {
    setSaveError(null)
    try {
      // Spread rather than `cardHash: seededHash`: a card the backfill never reached has none, and
      // the key present at undefined is a version claim the API cannot match.
      if (edited) {
        await updateContact.mutateAsync({
          id: edited.id, contact: seededHash ? { ...draft, cardHash: seededHash } : draft,
        })
      } else await createContact.mutateAsync(draft)
      onSaved()
      addToast(t('layout.saved'), 'success')
    } catch (error) {
      // Stay in the form carrying the reason: bouncing back to a list that kept nothing is how a
      // user loses what they typed without being told why. A stale write gets the box instead of
      // the banner — it has a way out to offer, and two messages read as two failures.
      if (error instanceof ApiError && error.status === 409) setConflict(true)
      else setSaveError(apiErrorMessage(error, t('layout.saveFailed')))
    }
  }

  // Recharging is the user's choice and never a consequence of the refusal: the form stands
  // untouched behind the box until this runs. A refetch that failed has nothing to seed, so the
  // box stays open rather than closing over the same stale form.
  async function reloadEdited() {
    const { isError: failed } = await refetchDetail()
    if (!failed) onReloaded()
  }

  async function confirmDelete() {
    if (!pendingDelete) return
    const name = displayNameOf(pendingDelete)
    try {
      await deleteContact.mutateAsync(pendingDelete.id)
      // The open card must not survive its contact.
      onDeletedOpen(pendingDelete.id)
      addToast(t('layout.deleted', { name }), 'success')
    } catch (error) {
      addToast(apiErrorMessage(error, t('layout.deleteFailed')), 'error')
    } finally {
      setPendingDelete(null)
    }
  }

  // One call for the whole batch: fifty contacts would otherwise be fifty requests, and a failure
  // at the thirtieth leaves a half-state nobody can word. The list clears its own boxes on confirm.
  function deleteSelection(ids: string[]) {
    deleteMany.mutate(ids, {
      onError: error => addToast(apiErrorMessage(error, t('layout.deleteManyFailed')), 'error'),
    })
  }

  function toggleFavorite(contact: Contact) {
    setFavorite.mutate({ id: contact.id, isFavorite: !contact.isFavorite }, {
      onError: error => addToast(apiErrorMessage(error, t('layout.favouriteFailed')), 'error'),
    })
  }

  // The drop adds — the favourite, or the membership — and never removes: a gesture that added or
  // removed per row would land a different result on each contact it carried.
  function dropOnScope(target: ContactScope, payload: ContactDragPayload) {
    const groupId = groupIdOf(target)
    if (groupId) {
      addMembers.mutate({ id: groupId, contactIds: payload.ids }, {
        onError: error => addToast(apiErrorMessage(error, t('groups.addFailed')), 'error'),
      })
      return
    }
    if (target !== 'favorites') return
    setManyFavorite.mutate({ ids: payload.ids, isFavorite: true }, {
      onError: error => addToast(apiErrorMessage(error, t('layout.favouriteFailed')), 'error'),
    })
  }

  // No dialog: a group's membership is what a drop restores, never a loss the way deleting the
  // contact itself is.
  function removeFromOpenGroup(ids: string[]) {
    if (!openGroup) return
    removeMembers.mutate({ id: openGroup.id, contactIds: ids }, {
      onError: error => addToast(apiErrorMessage(error, t('groups.removeFailed')), 'error'),
    })
  }

  function removeFromGroup(groupId: string) {
    if (!selected) return
    removeMembers.mutate({ id: groupId, contactIds: [selected.id] }, {
      onError: error => addToast(apiErrorMessage(error, t('groups.removeFailed')), 'error'),
    })
  }

  return {
    save, saving: createContact.isPending || updateContact.isPending, saveError,
    conflict, closeConflict: () => setConflict(false), reloadEdited,
    pendingDelete, setPendingDelete, confirmDelete, deleting: deleteContact.isPending,
    deleteSelection, toggleFavorite, dropOnScope, removeFromOpenGroup, removeFromGroup,
  }
}
