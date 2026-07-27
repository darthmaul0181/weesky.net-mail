import type { Contact } from './contactTypes'

/** The one place a contact is named. The tile, the card, the editor's heading and the composer's
    suggestion list all call this — four screens naming one contact four ways is the bug it
    prevents. */
export function displayNameOf(contact: Contact): string {
  const full = [contact.firstName, contact.lastName].filter(Boolean).join(' ')
  return full || contact.nickname || contact.addresses[0] || ''
}

export function primaryAddressOf(contact: Contact): string | null {
  return contact.addresses[0] ?? null
}
