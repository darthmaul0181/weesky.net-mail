import { useEffect, useRef, type RefObject } from 'react'
import { useLayer } from './useLayer'

interface Options {
  open: boolean
  /** The surface itself: a press inside it never dismisses. */
  rootRef: RefObject<HTMLElement | null>
  onDismiss: () => void
  /** Where focus goes when Escape closes the surface from inside it — the trigger, the chip. */
  refocusRef?: RefObject<HTMLElement | null>
  /** A surface pinned to a rectangle rather than to the flow: a scroll strands it. */
  closeOnScroll?: boolean
}

/** Closing unmounts the surface, so focus held inside it would fall to <body>: it goes back. */
export function returnFocus(root: HTMLElement | null, to: HTMLElement | null | undefined) {
  if (root?.contains(document.activeElement)) to?.focus()
}

/**
 * A dismissible surface that is not a dialog — a menu, a popover, the calendar bubble — on the
 * layer stack: Escape reaches it only while it is the topmost one, and a press outside it closes
 * it. No trap: Tab may leave a menu, and what it walks into is the page behind it.
 */
export function useDismiss({
  open, rootRef, onDismiss, refocusRef, closeOnScroll = false,
}: Options) {
  const latest = useRef(onDismiss)
  useEffect(() => { latest.current = onDismiss })

  const isTop = useLayer({
    active: open,
    onEscape: () => {
      returnFocus(rootRef.current, refocusRef?.current)
      onDismiss()
    },
  })

  useEffect(() => {
    if (!open) return undefined
    // A press that lands on a surface above this one is that surface's business: the confirm a
    // menu row opened is "outside" by containment alone, and closing under it is the bug.
    function outside(event: MouseEvent) {
      if (!isTop() || rootRef.current?.contains(event.target as Node)) return
      latest.current()
    }
    function scrolled() { if (isTop()) latest.current() }
    document.addEventListener('mousedown', outside)
    // Capture, so any scroller carries it — the week body, the month stage, the upcoming list.
    if (closeOnScroll) document.addEventListener('scroll', scrolled, true)
    return () => {
      document.removeEventListener('mousedown', outside)
      if (closeOnScroll) document.removeEventListener('scroll', scrolled, true)
    }
  }, [open, closeOnScroll, isTop, rootRef])
}
