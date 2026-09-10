import { useEffect, useRef, useState, type PointerEvent as ReactPointerEvent } from 'react'
import type { Occurrence } from './calendarTypes'
import { columnAt, snapMinutes } from './gridGeometry'
import { occurrenceKey } from './occurrenceStyle'
import type { PlainDate } from './plainDate'
import { trackPointer } from './pointerGesture'

export interface DragState {
  key: string
  deltaMinutes: number
  deltaDays: number
}

export interface DragEventOptions {
  enabled: boolean
  days: PlainDate[]
  onDrop(o: Occurrence, deltaMinutes: number, deltaDays: number): void
}

interface Area {
  left: number
  width: number
  columns: number
  /** The band has no hours to move through: a bandeau changes its day and nothing else. */
  vertical: boolean
}

/**
 * The box the day columns share, from the chip's own ancestry rather than measured on the grid:
 * `.day-column` carries its day, so the area's left edge is that column's minus the days before
 * it — which is exact whatever the scrollbar takes off the body's width.
 */
function areaOf(chip: HTMLElement, days: PlainDate[]): Area | null {
  const band = chip.closest<HTMLElement>('.allday-days')
  if (band) {
    const box = band.getBoundingClientRect()
    return { left: box.left, width: box.width, columns: days.length, vertical: false }
  }
  const column = chip.closest<HTMLElement>('.day-column')
  if (!column) return null
  const box = column.getBoundingClientRect()
  const index = Math.max(0, days.indexOf(column.dataset.day ?? ''))
  return {
    left: box.left - index * box.width, width: box.width * days.length,
    columns: days.length, vertical: true,
  }
}

/** A grid with no width is one nothing has laid out: a drag there moves by hours, never by days. */
function columnOf(area: Area, x: number): number {
  return area.width > 0 ? columnAt(x, area.left, area.width, area.columns) : 0
}

/**
 * Dragging a block to another hour or another day. It knows nothing of what a drop writes — the
 * layout wires `onDrop` — and nothing of where a chip is drawn: the view reads `drag` and offsets
 * the chip itself, so one gesture serves the hour grid and the all-day band alike.
 */
export function useDragEvent({ enabled, days, onDrop }: DragEventOptions) {
  const [drag, setDrag] = useState<DragState | null>(null)
  const stop = useRef<(() => void) | null>(null)
  useEffect(() => () => stop.current?.(), [])

  function onPointerDown(o: Occurrence, event: ReactPointerEvent) {
    if (!enabled || event.button !== 0) return
    const area = areaOf(event.currentTarget as HTMLElement, days)
    if (!area) return

    const key = occurrenceKey(o)
    const from = columnOf(area, event.clientX)
    let state: DragState = { key, deltaMinutes: 0, deltaDays: 0 }

    stop.current = trackPointer(event, {
      move: (_dx, dy, at) => {
        state = {
          key,
          deltaMinutes: area.vertical ? snapMinutes(dy) : 0,
          deltaDays: columnOf(area, at.clientX) - from,
        }
        setDrag(state)
      },
      // A block put back where it was is not a change, and a write that says nothing is a write
      // the server has to answer and the grid has to flicker for.
      drop: moved => {
        setDrag(null)
        if (!moved || (state.deltaMinutes === 0 && state.deltaDays === 0)) return
        onDrop(o, state.deltaMinutes, state.deltaDays)
      },
      cancel: () => setDrag(null),
    })
  }

  return { onPointerDown, drag }
}
