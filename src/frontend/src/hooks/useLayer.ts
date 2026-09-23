import {
  useEffect, useInsertionEffect, useLayoutEffect, useMemo, useRef, type RefObject,
} from 'react'
import {
  coveredByTrap, hasOpenLayer, isTopLayer, pushLayer, tabbablesIn, type LayerHandle,
} from '../lib/layerStack'

/** Somewhere a keyboard can work from: still in the document, not <body> — whose `focus()` is a
    silent no-op — and not a control disabled since it was focused. The one question every focus
    hand-back asks, `returnFocus` included, so the answer cannot drift into two. */
export function reachable(node: Element | null | undefined): node is HTMLElement {
  const element = node as (HTMLElement & { disabled?: boolean }) | null
  return !!element && element !== document.body && element.isConnected && !element.disabled
}

interface Options {
  active: boolean
  /** The trap container. Omit it for a surface Tab may leave — a menu, a popover. */
  ref?: RefObject<HTMLElement | null>
  onEscape?: () => void
  initialFocusRef?: RefObject<HTMLElement | null>
  /** Where focus goes on close when the element that opened the layer is gone. */
  returnFocusRef?: RefObject<HTMLElement | null>
  /** Read at close: true and `returnFocusRef` wins over an opener still on screen. A confirmed
      destructive action removes its opener, and often on a second round trip this commit cannot
      see. A ref rather than a prop — the press may close the layer in its own commit. */
  preferReturnRef?: RefObject<boolean>
}

/** Puts a surface on the layer stack while active: Escape and Tab reach it only when topmost, and
 * focus moves in on activation and back on close. It answers whether it is topmost and whether a
 * trap stands over it. */
export function useLayer({
  active, ref, onEscape, initialFocusRef, returnFocusRef, preferReturnRef,
}: Options) {
  const handle = useRef<LayerHandle | null>(null)
  const opener = useRef<HTMLElement | null>(null)
  // Where the cleanup below meant focus to go, for the passive cleanup that checks it landed, and
  // whether a layer stood over this one when it closed.
  const handedTo = useRef<HTMLElement | null>(null)
  const coveredAtClose = useRef(false)
  const latest = useRef({ onEscape, initialFocusRef, returnFocusRef, preferReturnRef })

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
    latest.current = { onEscape, initialFocusRef, returnFocusRef, preferReturnRef }
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
    if (container) {
      (latest.current.initialFocusRef?.current ?? tabbablesIn(container)[0] ?? container).focus()
    }
    return () => {
      // Read before the removal: a layer closing under another one must leave focus where the
      // user is, not yank it back to its own opener.
      const wasTop = !!handle.current && isTopLayer(handle.current)
      handle.current?.remove()
      handle.current = null
      if (!container) return
      // Judged at close, not at open: an opener that was plainly there may have gone since, or
      // have been disabled by the very write this layer asked about.
      const previous = opener.current
      const back = latest.current.returnFocusRef?.current ?? null
      // After a confirmed action the return target wins by decision: the opener may be reachable
      // in this commit and removed by the round trip after it, which nothing here re-checks.
      const preferReturn = latest.current.preferReturnRef?.current === true
      if (wasTop) {
        (preferReturn && reachable(back) ? back : reachable(previous) ? previous : back)?.focus()
      }
      // The return ref first: it is the target that survives an opener removed later in the same
      // commit. Recorded even under a layer above, which may be closing in this very commit —
      // having restored to an opener of its own inside the subtree now going.
      handedTo.current = back ?? previous
      coveredAtClose.current = !wasTop
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [active])

  // What the cleanup above cannot see: focus stranded *later in the same commit* — an opener
  // removed or disabled after this layer's layout cleanup, or a layer above handing focus to one
  // inside the subtree going with it. A passive cleanup runs once the DOM has settled.
  useEffect(() => () => {
    const back = handedTo.current
    const covered = coveredAtClose.current
    handedTo.current = null
    coveredAtClose.current = false
    // A layer still standing above owns the focus, even on a button disabled for its write (which
    // `reachable()` refuses). Recorded at close, not re-derived: a confirm opened inside a dialog
    // must still re-check with that dialog on the stack.
    if (covered && hasOpenLayer()) return
    if (reachable(back) && !reachable(document.activeElement)) back.focus()
  }, [active])

  // One stable object: its readers ask from inside effects that must not be re-installed on a
  // render, and both answers are read at the moment the event arrives rather than at render time.
  return useMemo(() => ({
    isTop: () => !!handle.current && isTopLayer(handle.current),
    coveredByTrap: () => !!handle.current && coveredByTrap(handle.current),
  }), [])
}
