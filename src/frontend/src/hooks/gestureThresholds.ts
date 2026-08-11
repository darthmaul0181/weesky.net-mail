/**
 * Shared between `useLongPress` and `usePullToRefresh`: below this many pixels of travel, a
 * touch is a held finger or jitter, not a drag. One home for the number, so a long press and a
 * pull started on the same row cancel on the same physical movement rather than at two points a
 * few pixels apart — which is otherwise a dead band where one hook has armed and the other has
 * not yet let go.
 */
export const GESTURE_TRAVEL_PX = 10
