export type PartStat = 'accepted' | 'tentative' | 'declined' | 'needs-action'

/** An iCalendar answer as the file spells it, in any case (RFC 5545 values are case-insensitive);
    null when it is missing or not one of these four. */
export function partStatOf(raw: string | undefined): PartStat | null {
  switch (raw?.toUpperCase()) {
    case 'ACCEPTED': return 'accepted'
    case 'TENTATIVE': return 'tentative'
    case 'DECLINED': return 'declined'
    case 'NEEDS-ACTION': return 'needs-action'
    default: return null
  }
}
