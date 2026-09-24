import type { Contact } from './contactTypes'

export function contactOf(fields: Partial<Contact> & { id: string }): Contact {
  return { isFavorite: false, addresses: [], ...fields }
}
