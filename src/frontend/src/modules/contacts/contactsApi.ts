import { request, requestBlob } from '../../lib/http'
import type {
  Contact, ContactDetail, ContactDraft, ContactImportReport, ContactListResponse,
} from './contactTypes'
import type { ContactGroup, ContactGroupsResponse } from './contactGroupTypes'

/** The address book and its groups. */
export const contactsApi = {
  getContacts: () =>
    request<ContactListResponse>('GET', '/api/Contacts'),

  // The whole card, which the list does not carry: only the open contact pays for it.
  getContact: (id: string) =>
    request<ContactDetail>('GET', `/api/Contacts/${id}`),

  // A blob, not a URL handed to <img>: the picture sits behind the session cookie on another
  // origin, which an image element cannot send.
  getContactPhoto: (id: string) =>
    requestBlob(`/api/Contacts/${id}/Photo`).then(({ blob }) => blob),

  createContact: (contact: ContactDraft) =>
    request<Contact>('POST', '/api/Contacts', contact),

  // Replaces the contact whole — names, favourite flag and the entire address list.
  updateContact: (id: string, contact: ContactDraft) =>
    request<null>('PUT', `/api/Contacts/${id}`, contact),

  deleteContact: (id: string) =>
    request<null>('DELETE', `/api/Contacts/${id}`),

  // Its own route: the star is toggled from a tile holding a possibly stale copy, so a whole
  // contact PUT from there would clobber a concurrent edit.
  setContactFavorite: (id: string, isFavorite: boolean) =>
    request<null>('PUT', `/api/Contacts/${id}/Favorite`, { isFavorite }),

  // The batch travels in the body rather than the URL: a list of ids in a query string breaks past
  // a few dozen and has no agreed shape. Capped at 200 server-side, the mail's own batch size.
  deleteContacts: (ids: string[]) =>
    request<null>('DELETE', '/api/Contacts', { ids }),

  setContactsFavorite: (ids: string[], isFavorite: boolean) =>
    request<null>('PUT', '/api/Contacts/Favorite', { ids, isFavorite }),

  importContacts: (file: File) => {
    const form = new FormData()
    form.append('file', file)
    return request<ContactImportReport>('POST', '/api/Contacts/Import', form)
  },

  exportContacts: () => requestBlob('/api/Contacts/Export'),

  getContactGroups: () => request<ContactGroupsResponse>('GET', '/api/ContactGroups'),

  createContactGroup: (name: string) => request<ContactGroup>('POST', '/api/ContactGroups', { name }),

  renameContactGroup: (id: string, name: string) =>
    request<null>('PUT', `/api/ContactGroups/${id}`, { name }),

  deleteContactGroup: (id: string) => request<null>('DELETE', `/api/ContactGroups/${id}`),

  addContactGroupMembers: (id: string, contactIds: string[]) =>
    request<null>('POST', `/api/ContactGroups/${id}/Members`, { contactIds }),

  removeContactGroupMembers: (id: string, contactIds: string[]) =>
    request<null>('DELETE', `/api/ContactGroups/${id}/Members`, { contactIds }),
}
