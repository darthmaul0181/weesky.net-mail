import type { Contact } from './contactTypes'

/** The name the user gave this contact, or null when they gave none. Separate from
    displayNameOf because a caller that already knows which address it is showing must not be
    handed a different one as a fallback. */
export function contactNameOf(contact: Contact): string | null {
  const full = [contact.firstName, contact.lastName].filter(Boolean).join(' ')
  return full || contact.nickname || null
}

/** The one place a contact is named. The tile, the card, the editor's heading and the composer's
    suggestion list all call this — four screens naming one contact four ways is the bug it
    prevents. */
export function displayNameOf(contact: Contact): string {
  return contactNameOf(contact) ?? contact.addresses[0] ?? ''
}

export function primaryAddressOf(contact: Contact): string | null {
  return contact.addresses[0] ?? null
}
