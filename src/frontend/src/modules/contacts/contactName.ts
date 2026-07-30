import { canonicalAddress } from '../../lib/canonicalAddress'
import type { Contact } from './contactTypes'

/** The name the user gave this contact, or null when they gave none. Separate from
    displayNameOf because a caller that already knows which address it is showing must not be
    handed a different one as a fallback.

    One of its own addresses is not a name, and this is where that is enforced rather than at the
    import: an Outlook/Rainloop export writes the address into a name column for every nameless
    card, and such a contact used to out-name — 'd' sorts before 'M' — the one carrying the real
    name for the same address, so a recipient chip showed an address it called a name.
    `splitFullName` applies the identical rule to a header display name at capture time. */
export function contactNameOf(contact: Contact): string | null {
  const full = [contact.firstName, contact.lastName].filter(Boolean).join(' ')
  const name = full || contact.nickname
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
