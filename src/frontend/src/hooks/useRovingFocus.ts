import { useLayoutEffect, type KeyboardEvent, type RefObject } from 'react'
import { tabbablesIn } from '../lib/layerStack'

interface Options {
  active: boolean
  /** The surface the walk stays inside: the menu itself, never the trigger's wrapper. */
  containerRef: RefObject<HTMLElement | null>
}

/** The types that hold text. `type` reads `text` for an absent or unknown value, so the untyped
    default is in; a checkbox, a radio and a button hold no caret and answer to no key of one. */
const TEXT_TYPES = ['text', 'search', 'url', 'tel', 'email', 'password', 'number']

/** Keys that belong to a caret: Home/End in any text box, ↓/↑ in a multi-line one. A single-line
 * `<input>` keeps ↓/↑, which is how a combobox's field reaches its list. */
export function textBox(target: EventTarget | null): 'single' | 'multiline' | null {
  const node = target as HTMLElement | null
  if (node?.tagName === 'INPUT') {
    return TEXT_TYPES.includes((node as HTMLInputElement).type) ? 'single' : null
  }
  if (node?.tagName === 'TEXTAREA') return 'multiline'
  return node?.closest('[contenteditable]:not([contenteditable="false"])') ? 'multiline' : null
}

/** The ARIA menu walk over one open surface: first item focused, ↓/↑ wrap, Home/End to the ends;
 * ←/→ belong to a submenu. It moves real focus over `tabbablesIn`, the stack's own Tab list, and
 * `preventDefault`s every key it spends (docs/architecture-shell.md). */
export function useRovingFocus({ active, containerRef }: Options) {
  // `preventScroll`: the first item sits against the trigger just pressed, so there is nothing to
  // scroll to — and a scroll would close an upward menu, whose flip is state and so lands a frame
  // later. Skipped when focus is already inside: a surface with a field of its own has placed it.
  useLayoutEffect(() => {
    const container = active ? containerRef.current : null
    if (!container || container.contains(document.activeElement)) return
    tabbablesIn(container)[0]?.focus({ preventScroll: true })
  }, [active, containerRef])

  return function onKeyDown(event: KeyboardEvent<HTMLElement>) {
    const container = containerRef.current
    if (!container || event.defaultPrevented) return
    const end = event.key === 'Home' ? 'first' : event.key === 'End' ? 'last' : null
    const step = event.key === 'ArrowDown' ? 1 : event.key === 'ArrowUp' ? -1 : 0
    if (end === null && step === 0) return
    const box = textBox(event.target)
    if (box && (end !== null || box === 'multiline')) return
    const items = tabbablesIn(container)
    if (items.length === 0) return
    const last = items.length - 1
    const at = items.indexOf(document.activeElement as HTMLElement)
    const to = end !== null ? (end === 'first' ? 0 : last)
      : at === -1 ? (step > 0 ? 0 : last)
        : (at + step + items.length) % items.length
    event.preventDefault()
    // `to` is 0, `last`, or a value modulo items.length, and items.length > 0 (checked above).
    items[to]!.focus()
  }
}
