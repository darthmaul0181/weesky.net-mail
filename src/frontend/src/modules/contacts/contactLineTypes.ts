import type { TFunction } from 'i18next'

/** The type tokens the editor offers, and the labels they wear. The table is the CSV exporter's,
    which is where the mapping between a vCard type and a human word already lives. */
/** The parts of a TYPE that say nothing to a reader. `INTERNET` is what every vCard e-mail
    carries and means only "this is an e-mail address"; `PREF` is the primary flag, which the card
    already renders as its own badge beside the value. */
const MUTE = new Set(['INTERNET', 'PREF'])

/** What is left of a type token once the parts that name no kind are dropped, or '' when nothing
    is. The card asks this before drawing a chip at all: an e-mail line's type is `INTERNET,PREF`
    on essentially every imported card, and `typeLabel` has no word for it, so its `default` arm
    put the raw vCard token on screen in capitals — measured on a real contact, reading
    `INTERNET,PREF` where the phone beside it read `Mobile`.

    The editor never showed this because it offers no type control for an address: only phones and
    postal addresses have one. So the chip is where these tokens first became visible. */
export function visibleType(token: string): string {
  return token.split(',').map(part => part.trim())
    .filter(part => part !== '' && !MUTE.has(part.toUpperCase()))
    .join(',')
}

/** The word a type token wears on screen. The editor puts it in a select and the card puts it in a
    chip, so it lives here rather than in either: the two naming one token two ways is the bug.
    An unknown token is shown verbatim — an imported card's own word beats a wrong guess, and
    `typeOptions` keeps it selectable for the same reason.

    `{ ns: 'contacts' }` is redundant to i18next, which reads the namespace off the TFunction, and
    is not redundant to `locales/keys.test.ts`: that guard binds a file's namespace from its
    `useTranslation(...)` call, and this file has none to bind from. Without it every key here is
    checked against `common` and the build reddens. Do not tidy it away. */
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
