import { canonicalAddress } from '../../lib/canonicalAddress'
import { collator } from '../../lib/intl'
import { contactNameOf, displayNameOf, primaryAddressOf } from './contactName'
import type { Contact } from './contactTypes'
import type { ContactGroup } from './contactGroupTypes'

const DEFAULT_LIMIT = 10
/** Three, against the addresses' ten: the two budgets are independent (decision 15), so a matched
    group never costs the field an address it would otherwise have offered. */
const GROUP_LIMIT = 3

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

  // The display name included, or a contact the list shows as `Dr. Le Châtelier` answers to
  // neither word: it is the only name on screen when the card carries one.
  return [contact.firstName, contact.lastName, contact.nickname, contact.displayName,
    ...contact.addresses]
    .some(field => field != null && fold(field).includes(needle))
}

export function filterContacts(contacts: Contact[], query: string): Contact[] {
  return contacts.filter(contact => matches(contact, query))
}

/** Favourites first, then by display name. localeCompare with base sensitivity for the reason the
    folder list uses it: a codepoint sort files every accented name after 'Z'. */
export function compareContacts(a: Contact, b: Contact): number {
  if (a.isFavorite !== b.isFavorite) return a.isFavorite ? -1 : 1
  return collator({ sensitivity: 'base' }).compare(displayNameOf(a), displayNameOf(b))
}

/** A group as the composer offers it: the name and the membership the row shows, plus the
    addresses picking it would insert. */
export interface GroupOption {
  id: string
  name: string
  memberCount: number
  addresses: string[]
}

export type ComposerSuggestion =
  | ({ kind: 'address' } & AddressSuggestion)
  | ({ kind: 'group' } & GroupOption)

/**
 * The resolved primary address of every group's members, deduplicated on `canonicalAddress` — the
 * app's own address identity, distinct from `fold`'s search normalisation, which strips
 * diacritics and would collapse two distinct SMTPUTF8 mailboxes ('josé@x.com', 'jose@x.com') into
 * one, silently dropping a member who never receives the mail.
 *
 * Computed once by the caller and read by the field and the band alike, so "writing to this group
 * reaches nobody" is one answer rather than two that can disagree. `memberCount` counts the
 * membership, never what writing would reach: a group of three whose members carry no address is
 * still a group of three.
 */
export function groupOptionsOf(groups: ContactGroup[], contacts: Contact[]): GroupOption[] {
  const byId = new Map(contacts.map(contact => [contact.id, contact]))
  return groups.map(group => {
    const seen = new Set<string>()
    const addresses: string[] = []
    for (const id of group.memberIds) {
      const member = byId.get(id)
      const address = member && primaryAddressOf(member)
      if (!address) continue
      const key = canonicalAddress(address)
      if (seen.has(key)) continue
      seen.add(key)
      addresses.push(address)
    }
    return { id: group.id, name: group.name, memberCount: group.memberIds.length, addresses }
  })
}

export interface AddressSuggestion {
  address: string
  /** Every contact carrying this address that the user actually named, in contact order. Length
      > 1 for a shared mailbox, and empty for a nameless card — the row is then the address alone,
      never the address printed twice as its own name. */
  names: string[]
}

/**
 * The composer's dropdown. Address rows are keyed on `canonicalAddress`, the app's own address
 * identity, since an address is what gets inserted: one address carried by several contacts — or
 * spelled in different case by two of them — is one row naming all of them, never several rows
 * producing the identical recipient. `fold` stays reserved for matching the query, never identity
 * — it would otherwise collapse two distinct SMTPUTF8 mailboxes that differ only by a diacritic.
 * The rendered `address` keeps its original spelling; only the key is canonicalised.
 *
 * Group rows come first and are capped before the merge, so the ten address places stay the ten
 * address places. A group whose every address is already a token is dropped — picking it could
 * only add nothing — while a group carrying no address at all is kept, because that is a state
 * the user has to be told about and the field's toast is where it is said.
 */
export function suggestionsFor(
  contacts: Contact[],
  query: string,
  options: { exclude?: Set<string>; limit?: number; groups?: GroupOption[] } = {},
): ComposerSuggestion[] {
  const { exclude, limit = DEFAULT_LIMIT, groups = [] } = options
  const needle = fold(query.trim())
  if (needle === '') return []

  // Keyed on canonicalAddress, the app's own address identity — not fold, which strips diacritics
  // and would treat 'josé@x.com' as the 'jose@x.com' already excluded, hiding a distinct mailbox.
  const excludeKeys = exclude && new Set([...exclude].map(address => canonicalAddress(address)))
  const rows = new Map<string, { address: string; names: string[]; favorite: boolean; primary: boolean }>()

  for (const contact of [...contacts].sort(compareContacts)) {
    if (!matches(contact, query)) continue

    const primary = primaryAddressOf(contact)
    const name = contactNameOf(contact)
    for (const address of contact.addresses) {
      const key = canonicalAddress(address)
      if (excludeKeys?.has(key)) continue

      const existing = rows.get(key)
      if (existing) {
        if (name !== null) existing.names.push(name)
        existing.favorite ||= contact.isFavorite
        existing.primary ||= address === primary
        continue
      }
      rows.set(key, {
        address,
        names: name === null ? [] : [name],
        favorite: contact.isFavorite,
        primary: address === primary,
      })
    }
  }

  const groupRows = groups
    .filter(group => fold(group.name).includes(needle))
    .filter(group => group.addresses.length === 0
      || group.addresses.some(address => !excludeKeys?.has(canonicalAddress(address))))
    .slice(0, GROUP_LIMIT)
    .map(group => ({ kind: 'group' as const, ...group }))

  const addressRows = [...rows.values()]
    .sort((left, right) =>
      Number(right.favorite) - Number(left.favorite)
      || Number(right.primary) - Number(left.primary)
      || collator({ sensitivity: 'base' }).compare(left.address, right.address))
    .slice(0, limit)
    .map(({ address, names }) => ({ kind: 'address' as const, address, names }))

  return [...groupRows, ...addressRows]
}
