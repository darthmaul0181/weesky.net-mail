import { canonicalAddress } from '../../lib/canonicalAddress'
import type { Contact } from './contactTypes'

/** The column width the backend enforces; over it, the whole contact is refused. */
const MAX_NAME_LENGTH = 100

export interface CaptureCandidate {
  firstName: string | null
  lastName: string | null
  /** Canonical: the form the backend stores anyway. */
  address: string
}

/** Slicing counts UTF-16 units, so it can cut an astral character in half; strict utf8mb4 refuses
    the surviving lone surrogate and the contact is lost — the very loss truncating avoids. */
const withoutLoneSurrogate = (value: string): string => {
  const last = value.charCodeAt(value.length - 1)
  return last >= 0xd800 && last <= 0xdbff ? value.slice(0, -1) : value
}

const bounded = (value: string): string | null => {
  const trimmed = withoutLoneSurrogate(value.trim().slice(0, MAX_NAME_LENGTH))
  return trimmed === '' ? null : trimmed
}

/**
 * A header display name split into the two columns a contact has. A comma means the corporate
 * "Last, First"; otherwise the last space separates given names from the family name.
 */
export function splitFullName(
  raw: string, address: string,
): { firstName: string | null; lastName: string | null } {
  const name = raw.trim()
  if (name === '' || name.toLowerCase() === canonicalAddress(address)) {
    return { firstName: null, lastName: null }
  }

  const comma = name.indexOf(',')
  if (comma >= 0) {
    return { firstName: bounded(name.slice(comma + 1)), lastName: bounded(name.slice(0, comma)) }
  }

  const space = name.lastIndexOf(' ')
  if (space < 0) return { firstName: bounded(name), lastName: null }
  return { firstName: bounded(name.slice(0, space)), lastName: bounded(name.slice(space + 1)) }
}

/**
 * Which recipients of a sent message deserve a contact. Blank entries, the account's own
 * addresses, addresses the book already holds and repeats within one send are all dropped.
 */
export function capturable(
  contacts: Contact[],
  recipients: string[],
  nameHints: Record<string, string>,
  mine: Set<string>,
): CaptureCandidate[] {
  const known = new Set<string>()
  for (const contact of contacts) {
    for (const address of contact.addresses) known.add(canonicalAddress(address))
  }
  const own = new Set([...mine].map(canonicalAddress))

  const seen = new Set<string>()
  const candidates: CaptureCandidate[] = []

  for (const recipient of recipients) {
    const address = canonicalAddress(recipient)
    if (address === '' || own.has(address) || known.has(address) || seen.has(address)) continue

    seen.add(address)
    candidates.push({ ...splitFullName(nameHints[address] ?? '', address), address })
  }

  return candidates
}
