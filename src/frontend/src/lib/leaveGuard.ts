/** Asks whoever holds unsaved work whether the user may leave it: a mailbox switch is a state
 * change the router's blocker never sees. A module-level slot, like api.ts's unauthorized handler,
 * since the two call sites sit on opposite sides of the tree. */
type LeaveGuard = () => Promise<boolean>

let guard: LeaveGuard | null = null

/** Called with a function while unsaved work exists, and with null once it is gone. */
export function registerLeaveGuard(fn: LeaveGuard | null): void {
  guard = fn
}

/** True when nothing is at stake, or when the user chose to leave it behind. */
export function confirmLeave(): Promise<boolean> {
  return guard ? guard() : Promise.resolve(true)
}
