import type { AliasInfo } from '../../mail/api/mailTypes'

/** An alias as the index draws it: always in lower case, and without its family's prefix when it
    sits on that family's line. `key` is the address, the identity every action works from. */
export interface IndexEntry { alias: AliasInfo; key: string; shown: string }
/** One line of a letter: its loose names when `prefix` is empty, one family's otherwise. */
export interface IndexLine { prefix: string; entries: IndexEntry[] }
export interface IndexLetter { letter: string; lines: IndexLine[] }

/** Below three, a shared start is a coincidence rather than a habit worth a line of its own. */
export const FAMILY_MIN = 3
/** Where the letters no alphabet orders go, after Z: a digit, a symbol. */
export const OTHER = '#'

/** The start up to the first `_`, `.` or `-`, separator included; '' when the name has none, opens
    with one, or ends there — a family member always keeps a suffix to show. */
export function prefixOf(name: string): string {
  const at = name.search(/[_.-]/)
  return at > 0 && at < name.length - 1 ? name.slice(0, at + 1) : ''
}

/** The index letter, accents folded so `élodie` files under E rather than past Z. */
export function letterOf(name: string): string {
  const first = name.normalize('NFD').replace(/\p{M}/gu, '').charAt(0).toUpperCase()
  return first >= 'A' && first <= 'Z' ? first : OTHER
}

export function buildIndex(aliases: AliasInfo[], compare: (a: string, b: string) => number): IndexLetter[] {
  const entries = aliases
    .map(alias => {
      const name = alias.name.toLowerCase()
      return { alias, name, key: `${name}@${alias.domain}` }
    })
    .sort((a, b) => compare(a.name, b.name))
  const sizes = new Map<string, number>()
  for (const { name } of entries) {
    const prefix = prefixOf(name)
    if (prefix) sizes.set(prefix, (sizes.get(prefix) ?? 0) + 1)
  }
  const familyOf = (name: string) => {
    const prefix = prefixOf(name)
    return (sizes.get(prefix) ?? 0) >= FAMILY_MIN ? prefix : ''
  }

  const letters = new Map<string, Map<string, IndexEntry[]>>()
  for (const { alias, name, key } of entries) {
    const prefix = familyOf(name)
    const letter = letterOf(name)
    const lines = letters.get(letter) ?? new Map<string, IndexEntry[]>([['', []]])
    letters.set(letter, lines)
    const line = lines.get(prefix) ?? []
    lines.set(prefix, line)
    line.push({ alias, key, shown: name.slice(prefix.length) })
  }

  const byLetter = (a: string, b: string) => (a === OTHER ? 1 : b === OTHER ? -1 : a.localeCompare(b))
  return [...letters.keys()].sort(byLetter).map(letter => {
    const lines = letters.get(letter)!
    const families = [...lines.keys()].filter(Boolean).sort(compare)
    return {
      letter,
      lines: ['', ...families]
        .map(prefix => ({ prefix, entries: lines.get(prefix)! }))
        .filter(line => line.entries.length > 0),
    }
  })
}
