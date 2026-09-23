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

/** Where a bubble hangs: right of the chip, flipped left or pulled up to stay on screen, or under
 * (else above) a chip that spans the window. Fixed coordinates; it closes on a scroll. Takes the
 * rect, not the chip: a search result leaves the screen in the commit that opens its bubble. */
export function usePopoverPosition(rect: DOMRect): PopoverPosition {
  const [node, setNode] = useState<HTMLElement | null>(null)
  const [position, setPosition] = useState({ left: 0, top: 0 })

  useLayoutEffect(() => {
    const height = node?.offsetHeight ?? 0
    const right = rect.right + GAP
    const fitsRight = right + POPOVER_WIDTH <= window.innerWidth
    const fitsLeft = rect.left - GAP - POPOVER_WIDTH >= EDGE
    if (fitsRight || fitsLeft) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- measures the node, which exists only after commit
      setPosition({
        left: fitsRight ? right : rect.left - POPOVER_WIDTH - GAP,
        top: Math.max(EDGE, Math.min(rect.top, window.innerHeight - height - EDGE)),
      })
      return
    }
    const below = rect.bottom + GAP
    setPosition({
      left: Math.max(EDGE, Math.min(rect.left, window.innerWidth - POPOVER_WIDTH - EDGE)),
      top: Math.max(EDGE, below + height + EDGE <= window.innerHeight ? below : rect.top - GAP - height),
    })
  }, [rect, node])

  return { ref: setNode, ...position }
}
