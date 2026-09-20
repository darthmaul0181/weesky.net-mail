import { useEffect, useLayoutEffect, useRef, type RefObject } from 'react'
import { reachable, useLayer } from './useLayer'

interface Options {
  open: boolean
  /** The surface itself: a press inside it never dismisses. */
  rootRef: RefObject<HTMLElement | null>
  onDismiss: () => void
  /** What opened the surface and toggles it: a press on it is the trigger's business, not a
      dismissal — closing here would unmount a surface the trigger is about to reopen. */
  anchorRef?: RefObject<HTMLElement | null>
  /** Where focus goes when Escape closes the surface from inside it — the trigger, the chip. */
  refocusRef?: RefObject<HTMLElement | null>
  /** A surface pinned to a rectangle rather than to the flow: a scroll strands it. */
  closeOnScroll?: boolean
}

/** Closing unmounts the surface, so focus held inside it would fall to <body>: it goes back. A
    target nothing can focus is no target, so focus stays put and whatever fallback the caller has
    runs instead. */
export function returnFocus(
  root: HTMLElement | null, to: HTMLElement | null | undefined, options?: FocusOptions) {
  if (reachable(to) && root?.contains(document.activeElement)) to.focus(options)
}

/**
 * A dismissible surface that is not a dialog — a menu, a popover, the calendar bubble — on the
 * layer stack: Escape reaches it only while it is the topmost one, and a press outside it closes
 * it. No trap: Tab may leave a menu, and what it walks into is the page behind it.
 */
export function useDismiss({
  open, rootRef, onDismiss, anchorRef, refocusRef, closeOnScroll = false,
}: Options) {
  // A layout effect, like `useLayer`'s: a press landing between the commit and a passive effect
  // would otherwise reach the handler the render before it.
  const latest = useRef({ onDismiss, refocusRef })
  useLayoutEffect(() => { latest.current = { onDismiss, refocusRef } })

  const layer = useLayer({
    active: open,
    onEscape: () => {
      returnFocus(rootRef.current, refocusRef?.current)
      onDismiss()
    },
  })

  useEffect(() => {
    if (!open) return undefined
    // A press that lands on a surface above this one is that surface's business: the confirm a
    // menu row opened is "outside" by containment alone, and closing under it is the bug. The
    // question is a trap above, not the top place: a menu over a popover suspends neither, so one
    // press on the page closes both.
    function outside(event: MouseEvent) {
      const target = event.target as Node
      if (layer.coveredByTrap()) return
      if (rootRef.current?.contains(target) || anchorRef?.current?.contains(target)) return
      latest.current.onDismiss()
    }
    // The surface leaves with the scroll, so the focus it holds has to go somewhere first — and
    // `preventScroll`, or refocusing what was just scrolled away from fights the gesture.
    function scrolled() {
      if (!layer.isTop()) return
      returnFocus(rootRef.current, latest.current.refocusRef?.current, { preventScroll: true })
      latest.current.onDismiss()
    }
    document.addEventListener('mousedown', outside)
    // Capture, so any scroller carries it — the week body, the month stage, the upcoming list.
    if (closeOnScroll) document.addEventListener('scroll', scrolled, true)
    return () => {
      document.removeEventListener('mousedown', outside)
      if (closeOnScroll) document.removeEventListener('scroll', scrolled, true)
    }
  }, [open, closeOnScroll, layer, rootRef, anchorRef])
}
