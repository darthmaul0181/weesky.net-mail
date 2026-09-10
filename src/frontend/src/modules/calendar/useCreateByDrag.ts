import { useEffect, useRef, useState, type PointerEvent as ReactPointerEvent } from 'react'
import { minutesAt, pxToMinutes, SNAP_MINUTES } from './gridGeometry'
import { trackPointer } from './pointerGesture'
import { utcOfLocalTime, type PlainDate } from './plainDate'

export interface GhostState {
  day: PlainDate
  startMinute: number
  endMinute: number
}

export interface CreateByDragOptions {
  enabled: boolean
  days: PlainDate[]
  tz: string
  /** A bubble is open over the grid: the click that dismisses it is spent doing that alone. */
  previewOpen?: boolean
  onCreate(start: Date, end: Date): void
}

/** What a plain click opens: the half hour it fell in, and the hour after it. */
const SLOT_MINUTES = 30
const CLICK_MINUTES = 60

/**
 * Drawing a new event on an empty column. The instants it hands over are the day's own midnight
 * in the calendar's zone plus the minutes traced — never a `Date` built from local components,
 * which would read the machine's zone and answer one thing in Brussels and another on a runner.
 */
export function useCreateByDrag({
  enabled, days, tz, previewOpen, onCreate,
}: CreateByDragOptions) {
  const [ghost, setGhost] = useState<GhostState | null>(null)
  const stop = useRef<(() => void) | null>(null)
  useEffect(() => () => stop.current?.(), [])

  function onPointerDown(day: PlainDate, event: ReactPointerEvent) {
    if (!enabled || event.button !== 0 || !days.includes(day)) return
    // The chips are children of the column their block sits in: without this, grabbing one would
    // draw a ghost underneath it and open an editor on the drop.
    if ((event.target as HTMLElement).closest('.event-chip')) return

    const top = (event.currentTarget as HTMLElement).getBoundingClientRect().top
    const anchor = pxToMinutes(event.clientY - top)
    let range: [number, number] = [anchor, anchor]

    const instant = (minute: number) => utcOfLocalTime(day, minute, tz)

    stop.current = trackPointer(event, {
      move: (_dx, _dy, at) => {
        const minute = pxToMinutes(at.clientY - top)
        range = [Math.min(anchor, minute), Math.max(anchor, minute)]
        setGhost({ day, startMinute: range[0], endMinute: range[1] })
      },
      drop: moved => {
        setGhost(null)
        if (!moved) {
          if (previewOpen) return
          const slot = Math.floor(minutesAt(event.clientY - top) / SLOT_MINUTES) * SLOT_MINUTES
          return onCreate(instant(slot), instant(slot + CLICK_MINUTES))
        }
        // A drag that snapped back onto its own start is still a drag: it opens the shortest
        // event the grid can draw rather than an event of no length at all.
        onCreate(instant(range[0]), instant(Math.max(range[1], range[0] + SNAP_MINUTES)))
      },
      cancel: () => setGhost(null),
    })
  }

  return { onPointerDown, ghost }
}
