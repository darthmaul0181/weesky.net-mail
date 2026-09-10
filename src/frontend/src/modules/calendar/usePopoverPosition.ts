import { useLayoutEffect, useState } from 'react'

/** The bubble's own measure, written here and in `calendar.css`'s `.event-preview` — the flip
    below is arithmetic on it, so a width read from the DOM would be a second source of truth. */
export const POPOVER_WIDTH = 300
const GAP = 8
const EDGE = 8

export interface PopoverPosition {
  /** The callback ref the popover carries: its height is what keeps it inside the window. */
  ref: (node: HTMLElement | null) => void
  left: number
  top: number
}

/**
 * Where a bubble hangs off the chip that opened it: to its right, flipped to its left when that
 * would run past the window, and pulled back up when the bottom would fall off the screen. Fixed
 * coordinates, so the scroll container it was opened from is none of its business — the bubble
 * closes on a scroll rather than following one.
 *
 * It takes the chip's rectangle rather than the chip itself: a search result opens its bubble and
 * clears the results in the same commit, so the element is off the screen by the time this runs.
 */
export function usePopoverPosition(rect: DOMRect): PopoverPosition {
  const [node, setNode] = useState<HTMLElement | null>(null)
  const [position, setPosition] = useState({ left: 0, top: 0 })

  useLayoutEffect(() => {
    const right = rect.right + GAP
    const flipped = right + POPOVER_WIDTH > window.innerWidth
    const height = node?.offsetHeight ?? 0
    setPosition({
      left: Math.max(EDGE, flipped ? rect.left - POPOVER_WIDTH - GAP : right),
      top: Math.max(EDGE, Math.min(rect.top, window.innerHeight - height - EDGE)),
    })
  }, [rect, node])

  return { ref: setNode, ...position }
}
