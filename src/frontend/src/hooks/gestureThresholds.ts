/** Below this travel a touch is a held finger or jitter, not a drag. Shared by `useLongPress` and
 * `usePullToRefresh` so both cancel on the same movement, with no dead band between them. */
export const GESTURE_TRAVEL_PX = 10
