/** What the user is searching for. Sent as-is (plus paging) to POST Messages/Search. */
export interface SearchCriteria {
  folderPath: string
  allFolders: boolean
  /** Fast-bar text: subject OR sender, compiled server-side. */
  quick?: string
  from?: string
  to?: string
  subject?: string
  text?: string
  /** Compiled server-side to SINCE (today - N): the client never sends a literal date. */
  sinceDays?: number
  unread?: boolean
  flagged?: boolean
  hasAttachment?: boolean
}

/** The advanced popup's raw fields, before trimming. */
export interface AdvancedForm {
  from: string
  to: string
  subject: string
  text: string
  sinceDays: number | null
  unread: boolean
  flagged: boolean
  hasAttachment: boolean
  allFolders: boolean
}

export const TEXT_FIELDS = ['quick', 'subject', 'text', 'from', 'to'] as const

export function isEmptyCriteria(criteria: SearchCriteria): boolean {
  return TEXT_FIELDS.every(field => !criteria[field]?.trim())
    && !criteria.sinceDays
    && !criteria.unread && !criteria.flagged && !criteria.hasAttachment
}

/**
 * What the list heading's star toggle writes on its own: this folder, starred, nothing else.
 * The lit star is the whole indication, so the heading keeps the folder name instead of handing
 * it to the results banner — until another criterion joins, or the search spans every folder,
 * where there is no folder name to keep and the count is worth showing.
 */
export function isStarredOnly(criteria: SearchCriteria): boolean {
  return criteria.flagged === true
    && !criteria.allFolders
    && TEXT_FIELDS.every(field => !criteria[field]?.trim())
    && !criteria.sinceDays && !criteria.unread && !criteria.hasAttachment
}

/** The text the results banner quotes — TEXT_FIELDS order, so the fast bar wins. */
export function labelOf(criteria: SearchCriteria): string | null {
  for (const field of TEXT_FIELDS) {
    const value = criteria[field]?.trim()
    if (value) return value
  }
  return null
}

/** Builds the criteria a submitted advanced form means, or null when it asks nothing. */
export function criteriaFromForm(folderPath: string, form: AdvancedForm): SearchCriteria | null {
  const criteria: SearchCriteria = { folderPath, allFolders: form.allFolders }
  if (form.from.trim()) criteria.from = form.from.trim()
  if (form.to.trim()) criteria.to = form.to.trim()
  if (form.subject.trim()) criteria.subject = form.subject.trim()
  if (form.text.trim()) criteria.text = form.text.trim()
  if (form.sinceDays) criteria.sinceDays = form.sinceDays
  if (form.unread) criteria.unread = true
  if (form.flagged) criteria.flagged = true
  if (form.hasAttachment) criteria.hasAttachment = true
  return isEmptyCriteria(criteria) ? null : criteria
}

/**
 * "This year" as a day count, so the server still receives SinceDays, never a date.
 * Built from local Y/M/D via Date.UTC so a DST transition between Jan 1 and now can't
 * shave off an hour and swallow a day.
 */
export function daysSinceYearStart(now: Date): number {
  const start = Date.UTC(now.getFullYear(), 0, 1)
  const today = Date.UTC(now.getFullYear(), now.getMonth(), now.getDate())
  return Math.max(1, Math.floor((today - start) / 86_400_000) + 1)
}
