import type { TFunction } from 'i18next'

// Takes `t` rather than being a hook, since two callers are not components; a literal key map, so
// the typed `t()` still checks every key.
const KEYS = {
  inbox: 'folders.roles.inbox',
  sent: 'folders.roles.sent',
  drafts: 'folders.roles.drafts',
  trash: 'folders.roles.trash',
  junk: 'folders.roles.junk',
  archive: 'folders.roles.archive',
} as const

export function roleLabel(role: string, t: TFunction<'mail'>): string {
  // hasOwnProperty, not `KEYS[role]` directly: `role` comes off IMAP's SPECIAL-USE, and
  // 'constructor' resolves to an inherited function — truthy, and not a translation key.
  const key = Object.prototype.hasOwnProperty.call(KEYS, role)
    ? KEYS[role as keyof typeof KEYS] : undefined
  return key ? t(key) : role
}
