import type { TFunction } from 'i18next'

// The editor's type tokens and their labels; the table is the CSV exporter's.
/** Parts of a TYPE that say nothing: `INTERNET` rides on every e-mail, and `PREF` has its own badge. */
const MUTE = new Set(['INTERNET', 'PREF'])

/** A type token minus the parts naming no kind, or ''. The card asks before drawing a chip: an
 * imported e-mail line's `INTERNET,PREF` has no word, and its raw token once showed in capitals. */
export function visibleType(token: string): string {
  return token.split(',').map(part => part.trim())
    .filter(part => part !== '' && !MUTE.has(part.toUpperCase()))
    .join(',')
}

/** A type token's word on screen, shared by the editor's select and the card's chip; an unknown one
 * shows verbatim. `{ ns: 'contacts' }` is for `keys.test.ts`, which finds no `useTranslation` here
 * to bind a namespace from: do not tidy it away. */
export function typeLabel(token: string, t: TFunction<'contacts'>): string {
  switch (token.trim().toUpperCase()) {
    case 'CELL': return t('editor.types.cell', { ns: 'contacts' })
    case 'HOME,VOICE': return t('editor.types.home_voice', { ns: 'contacts' })
    case 'WORK,VOICE': return t('editor.types.work_voice', { ns: 'contacts' })
    case 'HOME,FAX': return t('editor.types.home_fax', { ns: 'contacts' })
    case 'WORK,FAX': return t('editor.types.work_fax', { ns: 'contacts' })
    case 'VOICE': return t('editor.types.voice', { ns: 'contacts' })
    case 'HOME': return t('editor.types.home', { ns: 'contacts' })
    case 'WORK': return t('editor.types.work', { ns: 'contacts' })
    default: return token
  }
}

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

/** `ApplyType` strips PREF before it reaches the card's own type column, but a 3.0 round trip can
    still project it back in (`INTERNET,PREF,WORK`) — never offer it as a choice. */
export function stripPref(type: string): string {
  return type.split(',').filter(part => part.trim().toUpperCase() !== 'PREF').join(',')
}

/** Drops the parts `IsValidTypeToken` refuses: `VCardProjector` unquotes a TYPE like `"Work Email"`,
 * and sent back it would leave the contact unsaveable (a wider grammar would emit a bad TYPE). */
export function sanitizeTypeForSubmit(type: string): string {
  return type.split(',').filter(part => /^[A-Za-z0-9-]*$/.test(part.trim())).join(',')
}
