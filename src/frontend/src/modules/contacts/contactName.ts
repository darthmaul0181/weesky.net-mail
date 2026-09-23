import { canonicalAddress } from '../../lib/canonicalAddress'
import type { Contact } from './contactTypes'

/** The name the user gave, or null; never an address fallback. One of the contact's own addresses
 * is not a name: exports write it into a name column, where it out-sorted the real name for the
 * same address. A diverging display name comes first (docs/architecture-contacts.md). */
export function contactNameOf(contact: Contact): string | null {
  const full = [contact.firstName, contact.lastName].filter(Boolean).join(' ')
  const name = contact.displayName || full || contact.nickname
  if (!name) return null
  const canonical = canonicalAddress(name)
  return contact.addresses.some(a => canonicalAddress(a) === canonical) ? null : name
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

/** An avatar's two fallback letters, shared by the editor (from its boxes) and the card (from the
 * list row), so one disc is never drawn two ways. */
export function initialsOf(first: string, last: string, nickname: string): string {
  const letters = [first.trim(), last.trim()].filter(Boolean).map(part => part[0])
  if (letters.length > 0) return letters.join('').toUpperCase()
  const fallback = nickname.trim()
  return fallback === '' ? '' : fallback.charAt(0).toUpperCase()
}
