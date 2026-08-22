/** The type tokens the editor offers, and the labels they wear. The table is the CSV exporter's,
    which is where the mapping between a vCard type and a human word already lives. */
export const PHONE_TYPES = ['CELL', 'HOME,VOICE', 'WORK,VOICE', 'HOME,FAX', 'WORK,FAX', 'VOICE'] as const
export const POSTAL_TYPES = ['HOME', 'WORK'] as const

/** The options one row offers: the known list, plus the row's own token when the card carries one
    we do not list. A type we cannot name is still a type the card holds — offering only the closest
    label would rewrite it on a save that never meant to touch it. */
export function typeOptions(known: readonly string[], current: string): string[] {
  const token = current.trim()
  if (token === '' || known.some(k => k.toUpperCase() === token.toUpperCase())) return [...known]
  return [...known, token]
}

/** décision 4a's `ApplyType` strips PREF before it reaches the card's own type column, but a 3.0
    round trip can still project it back in (`INTERNET,PREF,WORK`) — never offer it as a choice. */
export function stripPref(type: string): string {
  return type.split(',').filter(part => part.trim().toUpperCase() !== 'PREF').join(',')
}

/** `VCardProjector` unquotes a TYPE like `"Work Email"` without filtering, but the write-side
    grammar (`ContactValidator.IsValidTypeToken`) only accepts `[A-Za-z0-9,-]`. Widening the
    grammar would make the composer emit an unquoted, malformed TYPE; sending the token back
    verbatim would leave the contact permanently unsaveable. Dropping it is the least-bad option. */
export function sanitizeTypeForSubmit(type: string): string {
  return type.split(',').filter(part => /^[A-Za-z0-9-]*$/.test(part.trim())).join(',')
}
