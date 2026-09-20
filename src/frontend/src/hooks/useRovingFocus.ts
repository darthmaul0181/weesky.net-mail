import { useLayoutEffect, type KeyboardEvent, type RefObject } from 'react'
import { focusablesIn } from '../lib/layerStack'

interface Options {
  active: boolean
  /** The surface the walk stays inside: the menu itself, never the trigger's wrapper. */
  containerRef: RefObject<HTMLElement | null>
}

/**
 * Which keys belong to a caret rather than to the walk: Home and End in any text box, and ↓/↑ in a
 * multi-line one, where they move between its own lines. A single-line `<input>` keeps ↓/↑, which
 * is how a combobox's field reaches its list.
 */
export function textBox(target: EventTarget | null): 'single' | 'multiline' | null {
  const node = target as HTMLElement | null
  if (node?.tagName === 'INPUT') return 'single'
  if (node?.tagName === 'TEXTAREA') return 'multiline'
  return node?.closest('[contenteditable]:not([contenteditable="false"])') ? 'multiline' : null
}

/**
 * The ARIA menu pattern's keyboard walk over one open surface: it focuses the first item, ↓/↑ move
 * and wrap, and Home/End jump to the ends. ←/→ are deliberately left alone — in that pattern they
 * belong to a submenu, and answering them would teach a dialect of our own.
 *
 * Focus itself moves and no `tabindex` is rewritten, so the walk and the layer stack's Tab read one
 * and the same list — `focusablesIn` — and cannot disagree about where focus may go inside a menu
 * standing over a trapped dialog. What it spends it marks `preventDefault`, so nothing underneath
 * answers the same key.
 *
 * Two bounds worth knowing. The handler is the surface's own, so a key pressed once focus has left
 * it never reaches the walk at all — there is nothing to stop, and the arrows are the page's again.
 * And a surface holding no focusable returns before marking anything, so the arrows fall through to
 * whatever is behind it rather than being swallowed by an empty menu.
 */
export function useRovingFocus({ active, containerRef }: Options) {
  // `preventScroll`: the first item sits against the trigger just pressed, so there is nothing to
  // scroll to — and a scroll would close an upward menu, whose flip is state and so lands a frame
  // later. Skipped when focus is already inside: a surface with a field of its own has placed it.
  useLayoutEffect(() => {
    const container = active ? containerRef.current : null
    if (!container || container.contains(document.activeElement)) return
    focusablesIn(container)[0]?.focus({ preventScroll: true })
  }, [active, containerRef])

  return function onKeyDown(event: KeyboardEvent<HTMLElement>) {
    const container = containerRef.current
    if (!container || event.defaultPrevented) return
    const end = event.key === 'Home' ? 'first' : event.key === 'End' ? 'last' : null
    const step = event.key === 'ArrowDown' ? 1 : event.key === 'ArrowUp' ? -1 : 0
    if (end === null && step === 0) return
    const box = textBox(event.target)
    if (box && (end !== null || box === 'multiline')) return
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
