import { useEffect, useRef, useState } from 'react'
import type { RefObject } from 'react'
import { GESTURE_TRAVEL_PX } from './gestureThresholds'

const THRESHOLD = 64
const MAX = 96

/** The pull that replaces the refresh button once the folder column is a drawer, starting only at
 * the list's top. Native listeners: touchmove must be non-passive to be preventable, and React
 * attaches passively. Returns the pull in px and whether releasing would refresh. */
export function usePullToRefresh(ref: RefObject<HTMLElement | null>, onRefresh: () => void) {
  const [pull, setPull] = useState(0)

  // In a ref: the caller hands a fresh arrow every render and `setPull` re-renders every touch
  // frame, so depending on it re-ran the listener effect and reset the gesture mid-drag.
  const onRefreshRef = useRef(onRefresh)
  useEffect(() => { onRefreshRef.current = onRefresh })

  useEffect(() => {
    const element = ref.current
    if (!element) return
    // Locals, not refs: the three handlers are created together inside this effect and close
    // over the same two variables, so `end` reads what `move` last wrote without a ref dance.
    let origin: number | null = null
    let travelled = 0

    function start(event: TouchEvent) {
      // Only from the very top. A downward drag anywhere else is a scroll.
      const touch = event.touches[0]
      origin = touch && element!.scrollTop === 0 ? touch.clientY : null
      travelled = 0
    }
    function move(event: TouchEvent) {
      if (origin === null) return
      const touch = event.touches[0]
      if (!touch) return
      const travel = touch.clientY - origin
      // Negative travel is the list scrolling up: end the gesture, or a later downward drag reads as
      // a pull. Strictly < 0: a repeated clientY (a sideways first move) is not a scroll.
      if (travel < 0) { origin = null; travelled = 0; setPull(0); return }
      // Below the shared jitter floor, neither draw the band nor preventDefault: a 1-2px wobble
      // during an ordinary tap must not re-render the list on every touch frame.
      if (travel < GESTURE_TRAVEL_PX) { travelled = 0; setPull(0); return }
      if (event.cancelable) event.preventDefault()
      travelled = Math.min(MAX, travel)
      setPull(travelled)
    }
    function end() {
      if (origin !== null && travelled >= THRESHOLD) onRefreshRef.current()
      origin = null
      travelled = 0
      setPull(0)
    }

    element.addEventListener('touchstart', start, { passive: true })
    // Non-passive, or preventDefault is ignored and the browser scrolls under the gesture.
    element.addEventListener('touchmove', move, { passive: false })
    element.addEventListener('touchend', end)
    element.addEventListener('touchcancel', end)
    return () => {
      element.removeEventListener('touchstart', start)
      element.removeEventListener('touchmove', move)
      element.removeEventListener('touchend', end)
      element.removeEventListener('touchcancel', end)
    }
    // onRefresh deliberately excluded: it is read through the ref above so the listeners survive
    // every render this hook itself causes.
  }, [ref])

  return { pull, armed: pull >= THRESHOLD }
}
