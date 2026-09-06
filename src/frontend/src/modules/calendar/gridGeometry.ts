import { MINUTES_PER_DAY } from './plainDate'

/**
 * The hour grid's own arithmetic, in one place: a pixel figure written twice — in a stylesheet
 * and in a drag handler — is a pixel figure that drifts. These are the pointer's side.
 */
export const HOUR_PX = 56
export const SNAP_MINUTES = 15
/** Where the grid is scrolled to when it opens: the rest of the day is a scroll away. */
export const FIRST_VISIBLE_HOUR = 7

/** The day's hours, drawn as cells by one column and as labels by the gutter beside it. */
export const HOURS = Array.from({ length: 24 }, (_, hour) => hour)

export function minutesToPx(minutes: number): number {
  return minutes * HOUR_PX / 60
}

/** The minutes a pixel offset names, unrounded: what a click reads before it is filed into a
    slot, and the one place the inverse of `minutesToPx` is written. */
export function minutesAt(px: number): number {
  return px * 60 / HOUR_PX
}

/** A signed travel snapped to the quarter hour: a drag is a distance, so it keeps its sign. */
export function snapMinutes(px: number): number {
  return Math.round(minutesAt(px) / SNAP_MINUTES) * SNAP_MINUTES
}

/** Snapped to the quarter hour and held inside the day: a pointer dragged above the grid or
    below it means the first or the last minute, never a negative one. */
export function pxToMinutes(px: number): number {
  return Math.min(MINUTES_PER_DAY, Math.max(0, snapMinutes(px)))
}

/** Which day column a pointer at `x` is over, `left` and `width` being the grid's own box. */
export function columnAt(x: number, left: number, width: number, columns: number): number {
  const index = Math.floor((x - left) / (width / columns))
  return Math.min(columns - 1, Math.max(0, index))
}
