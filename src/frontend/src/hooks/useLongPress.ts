import { useCallback, useEffect, useRef } from 'react'
import type { PointerEvent } from 'react'

const TRAVEL = 10

/**
 * A press held still for `ms`. The travel guard is what separates it from a scroll: a finger
 * that moves more than 10px was dragging the list, not choosing a row.
 *
 * Touch and pen only. A mouse already has hover, a context menu and a click of its own, and a
 * mouse press held still is not a gesture anyone means: on a desktop it enrolled the row in the
 * selection and swallowed the click that would have opened it, and a drag that hesitated before
 * moving carried the whole selection instead of the one row grabbed.
 */
export function useLongPress(onLongPress: () => void, ms = 500) {
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const origin = useRef({ x: 0, y: 0 })

  const cancel = useCallback(() => {
    if (timer.current) clearTimeout(timer.current)
    timer.current = null
  }, [])

  useEffect(() => cancel, [cancel])

  return {
    onPointerDown(event: PointerEvent) {
      // Cancel before the guard, not after: any second pointer ends the press it interrupts, so a
      // mouse click or a pinch's second finger stops a running timer rather than riding it out.
      cancel()
      // isPrimary keeps a second finger from starting one; button 0 keeps a pen's barrel click out.
      if ((event.pointerType !== 'touch' && event.pointerType !== 'pen')
        || !event.isPrimary || event.button !== 0) return
      origin.current = { x: event.clientX, y: event.clientY }
      timer.current = setTimeout(() => { timer.current = null; onLongPress() }, ms)
    },
    onPointerMove(event: PointerEvent) {
      const { x, y } = origin.current
      if (Math.hypot(event.clientX - x, event.clientY - y) > TRAVEL) cancel()
    },
    onPointerUp: cancel,
    onPointerCancel: cancel,
  }
}
