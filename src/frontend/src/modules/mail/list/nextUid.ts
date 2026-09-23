// The first loaded uid after the open row outside `departing`, else the nearest before, else null.
// Skipping only the open uid would land the reader on a sibling the same batch just dropped.
export function nextUidOf(uids: number[], uid: number, departing: number[] = [uid]): number | null {
  const index = uids.indexOf(uid)
  if (index === -1) return null
  const gone = new Set(departing)
  // Both loops keep i within [0, uids.length - 1], so uids[i] is always in bounds.
  for (let i = index + 1; i < uids.length; i++) if (!gone.has(uids[i]!)) return uids[i]!
  for (let i = index - 1; i >= 0; i--) if (!gone.has(uids[i]!)) return uids[i]!
  return null
}
