/**
 * Folded to what a user actually types: nobody reaches for the accent to find
 * "Courrier indésirable". NFD splits each letter from its diacritics, and \p{M} drops them.
 */
export function normalizeQuery(value: string): string {
  return value.toLowerCase().normalize('NFD').replace(/\p{M}/gu, '')
}

/** Substring match on the folder's own name — blind to case and accents both ways. */
export function folderMatches(name: string, query: string): boolean {
  return normalizeQuery(name).includes(normalizeQuery(query))
}
