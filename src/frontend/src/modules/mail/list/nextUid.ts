export function nextUidOf(uids: number[], uid: number): number | null {
  const index = uids.indexOf(uid)
  if (index === -1) return null
  if (index + 1 < uids.length) return uids[index + 1]
  return index > 0 ? uids[index - 1] : null
}
