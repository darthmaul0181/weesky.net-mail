import { displayNameOf, primaryAddressOf } from './contactName'
import type { Contact } from './contactTypes'

const DEFAULT_LIMIT = 10

/** Diacritics stripped and lower-cased. Nobody reaches for the é key to look somebody up, so a
    query has to match an accented contact and the reverse. \p{M} (combining marks), not
    \p{Diacritic} — the latter also covers ASCII '^' and '`', which would then vanish from plain
    text (`folderFilter.ts`'s `normalizeQuery` uses the same class for the same reason). */
export function fold(value: string): string {
  return value.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase()
}

/** Case- and accent-insensitive substring across every field a user would search by. Shared by
    the page's filter and the composer's dropdown — one rule, so the two can never disagree about
    what "matching" means. */
export function matches(contact: Contact, query: string): boolean {
  const needle = fold(query.trim())
  if (needle === '') return true

  return [contact.firstName, contact.lastName, contact.nickname, ...contact.addresses]
    .some(field => field != null && fold(field).includes(needle))
}

export function filterContacts(contacts: Contact[], query: string): Contact[] {
  return contacts.filter(contact => matches(contact, query))
}

/** Favourites first, then by display name. localeCompare with base sensitivity for the reason the
    folder list uses it: a codepoint sort files every accented name after 'Z'. */
export function compareContacts(a: Contact, b: Contact): number {
  if (a.isFavorite !== b.isFavorite) return a.isFavorite ? -1 : 1
  return displayNameOf(a).localeCompare(displayNameOf(b), undefined, { sensitivity: 'base' })
}

export interface AddressSuggestion {
  address: string
  /** Every contact carrying this address, in contact order. Length > 1 for a shared mailbox. */
  names: string[]
}

/**
 * The composer's dropdown. Rows are keyed by **address**, folded and trimmed, since an address is
 * what gets inserted: one address carried by several contacts — or spelled in different case by
 * two of them — is one row naming all of them, never several rows producing the identical
 * recipient. The rendered `address` keeps its original spelling; only the key is canonicalised.
 */
export function suggestionsFor(
  contacts: Contact[],
  query: string,
  options: { exclude?: Set<string>; limit?: number } = {},
): AddressSuggestion[] {
  const { exclude, limit = DEFAULT_LIMIT } = options
  if (fold(query.trim()) === '') return []

  // Keyed canonically (folded, trimmed) so that 'Info@Example.com' and 'info@example.com' — same
  // mailbox, different spelling — collapse to one row instead of two identical-looking recipients.
  const excludeKeys = exclude && new Set([...exclude].map(address => fold(address.trim())))
  const rows = new Map<string, { address: string; names: string[]; favorite: boolean; primary: boolean }>()

  for (const contact of [...contacts].sort(compareContacts)) {
    if (!matches(contact, query)) continue

    const primary = primaryAddressOf(contact)
    for (const address of contact.addresses) {
      const key = fold(address.trim())
      if (excludeKeys?.has(key)) continue

      const existing = rows.get(key)
      if (existing) {
        existing.names.push(displayNameOf(contact))
        existing.favorite ||= contact.isFavorite
        existing.primary ||= address === primary
        continue
      }
      rows.set(key, {
        address,
        names: [displayNameOf(contact)],
        favorite: contact.isFavorite,
        primary: address === primary,
      })
    }
  }

  return [...rows.values()]
    .sort((left, right) =>
      Number(right.favorite) - Number(left.favorite)
      || Number(right.primary) - Number(left.primary)
      || left.address.localeCompare(right.address, undefined, { sensitivity: 'base' }))
    .slice(0, limit)
    .map(({ address, names }) => ({ address, names }))
}
