import { useEffect, useRef, useState } from 'react'
import type { RefObject } from 'react'
import { GESTURE_TRAVEL_PX } from './gestureThresholds'

const THRESHOLD = 64
const MAX = 96

/**
 * The gesture that replaces the refresh button once the folder column is a drawer. It only
 * starts at the very top of the list: a downward drag anywhere else is a scroll.
 *
 * Returns the current pull in pixels and whether releasing now would refresh, so the caller can
 * draw a band. Native listeners rather than React's, because touchmove has to be non-passive to
 * be preventable, and React attaches its own passively.
 */
export function usePullToRefresh(ref: RefObject<HTMLElement | null>, onRefresh: () => void) {
  const [pull, setPull] = useState(0)

  // Held in a ref, like ContextDrawer's onCloseRef: the caller's real call site hands in a fresh
  // arrow every render, and `setPull` below re-renders it on every touch frame. Depending on the
  // callback directly re-ran the listener effect mid-gesture, resetting `origin`/`travelled` to
  // nothing before the drag ever reached the threshold.
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
      origin = element!.scrollTop === 0 ? event.touches[0].clientY : null
      travelled = 0
    }
    function move(event: TouchEvent) {
      if (origin === null) return
      const travel = event.touches[0].clientY - origin
      // Negative travel is the list scrolling up under the finger; nulling origin ends the
      // gesture rather than leaving it to resume from the original start point, which would let
      // a later downward drag over the same touch read as a pull past a list that already moved.
      // Strictly negative, not <= 0: a frame whose clientY exactly repeats the start — routine on
      // a real device when the finger's first movement is sideways — is zero travel, not a
      // scroll, and must fall through to the ordinary sub-threshold branch below.
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
