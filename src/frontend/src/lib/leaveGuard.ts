/** Asks whoever holds unsaved work whether the user may leave it: a mailbox switch or a sign-out is a
 * state change the router's blocker never sees. A module-level set, like api.ts's unauthorized
 * handler, since the call sites sit on opposite sides of the tree. */
type LeaveGuard = () => Promise<boolean>

const guards = new Set<LeaveGuard>()

/** Registers a guard while unsaved work exists; the returned function withdraws it. */
export function registerLeaveGuard(fn: LeaveGuard): () => void {
  const own: LeaveGuard = () => fn()
  guards.add(own)
  return () => { guards.delete(own) }
}

/** True when nothing is at stake, or when the user chose to leave it all behind. Guards are asked
 * one after another in registration order, and the first refusal ends the round. */
export async function confirmLeave(): Promise<boolean> {
  for (const guard of [...guards]) if (guards.has(guard) && !(await guard())) return false
  return true
}
