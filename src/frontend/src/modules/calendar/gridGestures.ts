import type { PointerEvent } from 'react'
import type { Occurrence } from './calendarTypes'
import type { PlainDate } from './plainDate'
import type { DragState } from './useDragEvent'
import type { GhostState } from './useCreateByDrag'
import type { ResizeState } from './useResizeEvent'

/**
 * What the week's three gestures hand its two inner views. One object rather than six props:
 * `WeekView` owns the hooks — it is the component that has the days and the pointer — and the
 * column and the band only draw what they are told and pass a press back up.
 */
export interface GridGestures {
  drag: DragState | null
  resize: ResizeState | null
  ghost: GhostState | null
  onChipDown(o: Occurrence, event: PointerEvent): void
  onResizeDown(o: Occurrence, event: PointerEvent): void
  onEmptyDown(day: PlainDate, event: PointerEvent): void
}

/** The band moves by days and never resizes, so it is handed the half of that it can use. */
export type BandGestures = Pick<GridGestures, 'drag' | 'onChipDown'>
