import type { TFunction } from 'i18next'

/**
 * Display label for a well-known folder role — the seam the codebase reserved for this.
 *
 * It takes `t` rather than being a hook because two of its callers are not components. The keys
 * are a literal map rather than a template, so the typed `t()` still checks every one of them.
 */
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
