/**
 * Display label for a well-known folder role. This function is the i18n seam: today it
 * returns hard-coded English, and when the site goes multilingual only this function changes
 * — the role stays the language-independent canonical key everywhere else.
 */
const LABELS: Record<string, string> = {
  inbox: 'Inbox',
  sent: 'Sent',
  drafts: 'Drafts',
  trash: 'Trash',
  junk: 'Junk',
  archive: 'Archive',
}

export function roleLabel(role: string): string {
  return LABELS[role] ?? role
}
