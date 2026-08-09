import { useCallback, useEffect, useRef } from 'react'
import type { PointerEvent } from 'react'

const TRAVEL = 10

/**
 * A press held still for `ms`. The travel guard is what separates it from a scroll: a finger
 * that moves more than 10px was dragging the list, not choosing a row.
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
      origin.current = { x: event.clientX, y: event.clientY }
      cancel()
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
