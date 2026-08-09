import { useEffect, useState } from 'react'
import type { RefObject } from 'react'

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
      if (travel <= 0) { travelled = 0; setPull(0); return }
      // Only once it is really a pull: preventing default earlier would kill ordinary scrolling.
      if (travel > 8 && event.cancelable) event.preventDefault()
      travelled = Math.min(MAX, travel)
      setPull(travelled)
    }
    function end() {
      if (origin !== null && travelled >= THRESHOLD) onRefresh()
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
  }, [ref, onRefresh])

  return { pull, armed: pull >= THRESHOLD }
}
