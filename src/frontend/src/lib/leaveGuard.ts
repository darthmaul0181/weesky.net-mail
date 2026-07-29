/**
 * Asks whoever holds unsaved work whether the user may be taken away from it.
 *
 * The composer already guards every *navigation* through the router's blocker, but switching
 * mailbox is a state change rather than a navigation, so nothing in the router sees it. This is
 * the seam for that case, registered the way `api.js` registers its unauthorized handler: a
 * module-level slot rather than a context, since the two call sites sit on opposite sides of the
 * tree and neither renders the other.
 */
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
