import { useInsertionEffect, useLayoutEffect, useRef, type RefObject } from 'react'
import { focusablesIn, isTopLayer, pushLayer, type LayerHandle } from '../lib/layerStack'

interface Options {
  active: boolean
  /** The trap container. Omit it for a surface Tab may leave — a menu, a popover. */
  ref?: RefObject<HTMLElement | null>
  onEscape?: () => void
  initialFocusRef?: RefObject<HTMLElement | null>
  /** Where focus goes on close when the element that opened the layer is gone. */
  returnFocusRef?: RefObject<HTMLElement | null>
  autoFocus?: boolean
  restoreFocus?: boolean
}

/**
 * Puts one surface on the layer stack while it is active: Escape and Tab reach it only while it
 * is the topmost one, and focus moves in on activation and back out on close.
 */
export function useLayer({
  active, ref, onEscape, initialFocusRef, returnFocusRef, autoFocus = true, restoreFocus = true,
}: Options) {
  const handle = useRef<LayerHandle | null>(null)
  const opener = useRef<HTMLElement | null>(null)
  const latest = useRef({ onEscape, initialFocusRef, returnFocusRef })

  // React commits a child's `autoFocus` in the layout phase, before any layout effect, so no
  // layout effect can still see the opener. An insertion effect can; and one that finds focus
  // already inside keeps what it captured, rather than taking the layer's own field for it.
  useInsertionEffect(() => {
    if (!active) return
    const focused = document.activeElement as HTMLElement | null
    if (focused && ref?.current?.contains(focused)) return
    opener.current = focused
  }, [active])

  // First and on every render, so the layer below answers with this render's handler and
  // container rather than the ones it was pushed with.
  useLayoutEffect(() => {
    latest.current = { onEscape, initialFocusRef, returnFocusRef }
    handle.current?.update({ onEscape, trap: ref?.current ?? null })
  })

  // `active` alone: a layer keeps its place in the stack for its whole active life, and a handler
  // re-created by a render must never move it above the one that opened after it.
  useLayoutEffect(() => {
    if (!active) return undefined
    const container = ref ? ref.current : null
    // A ref was given but holds nothing: no container, so no trap, and a trapless layer here would
    // swallow Escape and mask the trap under it. The old focus-trap hook no-opped the same way.
    if (ref && !container) return undefined
    handle.current = pushLayer({ onEscape: latest.current.onEscape, trap: container })
    if (container && autoFocus) {
      (latest.current.initialFocusRef?.current ?? focusablesIn(container)[0] ?? container).focus()
    }
    return () => {
      // Read before the removal: a layer closing under another one must leave focus where the
      // user is, not yank it back to its own opener.
      const wasTop = !!handle.current && isTopLayer(handle.current)
      handle.current?.remove()
      handle.current = null
      if (!container || !restoreFocus || !wasTop) return
      // Judged at close, not at open: an opener that was plainly connected may have gone since.
      // <body> is excluded because body.focus() is a silent no-op — the "focus drops to nowhere"
      // this exists to prevent.
      const previous = opener.current
      const usable = previous?.isConnected && previous !== document.body
      ;(usable ? previous : latest.current.returnFocusRef?.current)?.focus()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [active])
}
