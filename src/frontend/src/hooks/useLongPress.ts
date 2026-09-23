import { useCallback, useEffect, useRef } from 'react'
import type { PointerEvent } from 'react'
import { GESTURE_TRAVEL_PX } from './gestureThresholds'

/** A press held still for `ms`; moving past 10px is a scroll, not a choice. Touch and pen only: a
 * held mouse press enrolled the row in the selection, swallowed the click that opens it, and made
 * a hesitant drag carry the whole selection. */
export function useLongPress(onLongPress: () => void, ms = 500) {
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const origin = useRef({ x: 0, y: 0 })

  const cancel = useCallback(() => {
    if (timer.current) clearTimeout(timer.current)
    timer.current = null
  }, [])

  useEffect(() => cancel, [cancel])

  return {
    onPointerDown: (event: PointerEvent) => {
      // Cancel before the guard, not after: any second pointer ends the press it interrupts, so a
      // mouse click or a pinch's second finger stops a running timer rather than riding it out.
      cancel()
      // isPrimary keeps a second finger from starting one; button 0 keeps a pen's barrel click out.
      if ((event.pointerType !== 'touch' && event.pointerType !== 'pen')
        || !event.isPrimary || event.button !== 0) return
      origin.current = { x: event.clientX, y: event.clientY }
      timer.current = setTimeout(() => { timer.current = null; onLongPress() }, ms)
    },
    onPointerMove: (event: PointerEvent) => {
      const { x, y } = origin.current
      if (Math.hypot(event.clientX - x, event.clientY - y) > GESTURE_TRAVEL_PX) cancel()
    },
    onPointerUp: cancel,
    onPointerCancel: cancel,
  }
}
