import { useEffect, useRef, useState, type PointerEvent as ReactPointerEvent } from 'react'
import type { Occurrence } from './calendarTypes'
import { SNAP_MINUTES, snapMinutes } from './gridGeometry'
import { durationMinutesOf } from './multiDay'
import { occurrenceKey } from './occurrenceStyle'
import { trackPointer } from './pointerGesture'

export interface ResizeState {
  key: string
  durationMinutes: number
  /** The duration the drag started from, in the same zone `durationMinutes` is counted in
      (the occurrence's own, `durationMinutesOf`'s default) — what a preview in the grid's own
      zone must subtract before adding its own duration back, so the two zones never mix. */
  baseMinutes: number
}

export interface ResizeEventOptions {
  enabled: boolean
  onResize(o: Occurrence, newDurationMinutes: number): void
}

/**
 * Dragging a block's foot. The handle sends an absolute duration rather than a delta, so what the
 * server is told is what the grid drew; a quarter of an hour is the floor, under which a chip has
 * neither a title nor a hit area left.
 */
export function useResizeEvent({ enabled, onResize }: ResizeEventOptions) {
  const [resize, setResize] = useState<ResizeState | null>(null)
  const stop = useRef<(() => void) | null>(null)
  useEffect(() => () => stop.current?.(), [])

  function onPointerDown(o: Occurrence, event: ReactPointerEvent) {
    if (!enabled || event.button !== 0) return

    const key = occurrenceKey(o)
    const base = durationMinutesOf(o)
    let duration = base

    stop.current = trackPointer(event, {
      move: (_dx, dy) => {
        duration = Math.max(SNAP_MINUTES, base + snapMinutes(dy))
        setResize({ key, durationMinutes: duration, baseMinutes: base })
      },
      drop: moved => {
        setResize(null)
        if (moved && duration !== base) onResize(o, duration)
      },
      cancel: () => setResize(null),
    })
  }

  return { onPointerDown, resize }
}
