/**
 * The uid to open once `uid`'s row departs. `departing` is the whole batch a bulk action removed
 * (the open uid alone for a single-row action): the survivor is the first loaded uid after the
 * open row that is not in the batch, else the nearest one before it, else null. Skipping only the
 * open uid would land the reader on a sibling the same action just dropped from the cache.
 */
export function nextUidOf(uids: number[], uid: number, departing: number[] = [uid]): number | null {
  const index = uids.indexOf(uid)
  if (index === -1) return null
  const gone = new Set(departing)
  for (let i = index + 1; i < uids.length; i++) if (!gone.has(uids[i])) return uids[i]
  for (let i = index - 1; i >= 0; i--) if (!gone.has(uids[i])) return uids[i]
  return null
}
