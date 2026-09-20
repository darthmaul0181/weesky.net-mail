import { useLayoutEffect, type KeyboardEvent, type RefObject } from 'react'
import { focusablesIn } from '../lib/layerStack'

interface Options {
  active: boolean
  /** The surface the walk stays inside: the menu itself, never the trigger's wrapper. */
  containerRef: RefObject<HTMLElement | null>
  /**
   * Items per row. Above 1, ←/→ step one item and ↓/↑ a whole row — a swatch palette walked as a
   * flat list sends the down arrow sideways. A plain menu is one column and ignores ←/→.
   */
  columns?: number
}

/** Home and End belong to the caret while focus is in a text box, never to the walk. */
function typing(target: EventTarget | null): boolean {
  const tag = (target as HTMLElement | null)?.tagName
  return tag === 'INPUT' || tag === 'TEXTAREA'
}

/**
 * The ARIA menu pattern's keyboard walk over one open surface: it focuses the first item, ↓/↑ move
 * and wrap, Home/End jump to the ends. Focus itself moves and no `tabindex` is rewritten, so the
 * walk and the layer stack's Tab read one and the same list — `focusablesIn` — and cannot disagree
 * about where focus may go inside a menu standing over a trapped dialog. What it spends it marks
 * `preventDefault`, so nothing underneath answers the same key.
 */
export function useRovingFocus({ active, containerRef, columns = 1 }: Options) {
  // Skipped when focus is already inside: a surface holding a field of its own has already placed
  // it, and taking it back would put the caret somewhere the user did not ask for.
  useLayoutEffect(() => {
    const container = active ? containerRef.current : null
    if (!container || container.contains(document.activeElement)) return
    focusablesIn(container)[0]?.focus()
  }, [active, containerRef])

  return function onKeyDown(event: KeyboardEvent<HTMLElement>) {
    const container = containerRef.current
    if (!container || event.defaultPrevented) return
    const end = event.key === 'Home' ? 'first' : event.key === 'End' ? 'last' : null
    const step = event.key === 'ArrowDown' ? columns : event.key === 'ArrowUp' ? -columns
      : columns === 1 ? 0 : event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0
    if (end === null && step === 0) return
    if (end !== null && typing(event.target)) return
    const items = focusablesIn(container)
    if (items.length === 0) return
    const last = items.length - 1
    const at = items.indexOf(document.activeElement as HTMLElement)
    const to = end !== null ? (end === 'first' ? 0 : last)
      : at === -1 ? (step > 0 ? 0 : last)
        : (at + step + items.length) % items.length
    event.preventDefault()
    items[to].focus()
  }
}
