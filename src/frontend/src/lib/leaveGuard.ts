/** Asks whoever holds unsaved work whether the user may leave it: a mailbox switch or a sign-out is a
 * state change the router's blocker never sees. A module-level set, like api.ts's unauthorized
 * handler, since the call sites sit on opposite sides of the tree. */
type LeaveGuard = () => Promise<boolean>

interface Entry { ask: LeaveGuard; commit?: () => void }

const guards = new Set<Entry>()

/** Registers a guard while unsaved work exists; the returned function withdraws it. `commit` carries
 * out what its answer decided, once every guard has agreed. */
export function registerLeaveGuard(ask: LeaveGuard, commit?: () => void): () => void {
  const own: Entry = { ask: () => ask(), commit }
  guards.add(own)
  return () => { guards.delete(own) }
}

/** True when nothing is at stake, or when the user chose to leave it all behind. Guards are asked
 * one after another in registration order, and the first refusal ends the round. Nothing is
 * committed before the last answer: a later refusal must not cost an earlier draft. */
export async function confirmLeave(): Promise<boolean> {
  const agreed: Entry[] = []
  for (const guard of [...guards]) {
    if (!guards.has(guard)) continue
    if (!(await guard.ask())) return false
    agreed.push(guard)
  }
  for (const guard of agreed) if (guards.has(guard)) guard.commit?.()
  return true
}
