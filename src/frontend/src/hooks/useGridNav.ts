import { useLayoutEffect, useRef, type RefObject } from 'react'
import { focusablesIn } from '../lib/layerStack'
import { reachable } from './useLayer'
import { textBox } from './useRovingFocus'

export interface GridNavOptions {
  /** The element carrying role="grid". Navigation is scoped to its role="row" descendants. */
  ref: RefObject<HTMLElement | null>
}

interface Cell { row: number; col: number }
/** Where one key goes, before any clamping: `widgetAt` is what brings it back inside the grid. */
type Move = (rows: HTMLElement[][], at: Cell) => Cell

const ROW = '[role="row"]'
const END = Infinity

const last = <T, >(list: T[]) => list.length - 1

/** The rows holding a widget, each as its own list. A row holding none is dropped rather than
    listed, which is how vertical movement steps over it instead of landing in it. */
function rowsIn(grid: HTMLElement): HTMLElement[][] {
  return Array.from(grid.querySelectorAll<HTMLElement>(ROW))
    .map(row => focusablesIn(row))
    .filter(widgets => widgets.length > 0)
}

function cellOf(rows: HTMLElement[][], widget: Element | null): Cell | null {
  for (let row = 0; row < rows.length; row += 1) {
    const col = rows[row].indexOf(widget as HTMLElement)
    if (col !== -1) return { row, col }
  }
  return null
}

/** Both axes clamped in one place: the grid pattern has no wrap, and a shorter row is entered at
    its own last widget rather than skipped past. */
function widgetAt(rows: HTMLElement[][], to: Cell): HTMLElement {
  const row = rows[Math.min(Math.max(to.row, 0), last(rows))]
  return row[Math.min(Math.max(to.col, 0), last(row))]
}

/** The one place the key list lives, so nothing spends a key this does not answer. */
function moveOf(event: KeyboardEvent): Move | null {
  if (event.altKey || event.metaKey || event.shiftKey) return null
  switch (event.key) {
    case 'ArrowLeft': return (rows, at) => ({ row: at.row, col: at.col - 1 })
    case 'ArrowRight': return (rows, at) => ({ row: at.row, col: at.col + 1 })
    case 'ArrowUp': return (rows, at) => ({ row: at.row - 1, col: at.col })
    case 'ArrowDown': return (rows, at) => ({ row: at.row + 1, col: at.col })
    case 'Home': return event.ctrlKey
      ? () => ({ row: 0, col: 0 })
      : (rows, at) => ({ row: at.row, col: 0 })
    case 'End': return event.ctrlKey
      ? rows => ({ row: last(rows), col: END })
      : (rows, at) => ({ row: at.row, col: END })
    default: return null
  }
}

/** `useRovingFocus`' guards, unchanged: a caret owns Home, End and ←/→ in any text box, and ↓/↑
    in a multi-line one, where they move between its own lines. */
function yieldsToCaret(event: KeyboardEvent): boolean {
  const box = textBox(event.target)
  if (!box) return false
  const vertical = event.key === 'ArrowUp' || event.key === 'ArrowDown'
  return !vertical || box === 'multiline'
}

/**
 * Roving tabindex over a `role="grid"`: exactly one widget inside it is in the page tab sequence at
 * a time, the arrows, Home and End move between widgets, and Tab leaves the grid altogether.
 *
 * Focus lands on the widget a cell holds rather than on the cell, which is the pattern's own answer
 * wherever that widget needs no arrow key of its own — a checkbox, a star, a colour swatch. The
 * listener is the container's, never `document`'s: a grid is not a layer and must not compete with
 * the stack for Escape or Tab.
 */
export function useGridNav({ ref }: GridNavOptions): void {
  const stop = useRef<HTMLElement | null>(null)

  useLayoutEffect(() => {
    const grid = ref.current
    if (!grid) return undefined

    const tab = (widget: HTMLElement, value: number) => {
      const want = String(value)
      if (widget.getAttribute('tabindex') !== want) widget.setAttribute('tabindex', want)
    }

    const point = (widget: HTMLElement) => {
      if (stop.current && stop.current !== widget) tab(stop.current, -1)
      tab(widget, 0)
      stop.current = widget
    }

    // The whole invariant, restated: one stop, and a stop whose widget has gone recovers to the
    // first live one. The hook owns the stop, so it owns that recovery — no consumer repeats it.
    const repoint = () => {
      const widgets = rowsIn(grid).flat()
      const held = stop.current
      const target = held && widgets.includes(held) && reachable(held) ? held : widgets[0]
      if (!target) { stop.current = null; return }
      widgets.forEach(widget => tab(widget, widget === target ? 0 : -1))
      stop.current = target
    }

    // A click, a `.focus()` of our own, a caret leaving one cell for the next: the stop follows
    // focus wherever it came from, and costs two attributes rather than a walk of the grid.
    const onFocusIn = (event: FocusEvent) => {
      const widget = event.target as HTMLElement
      const row = widget.closest(ROW)
      if (row && grid.contains(row) && focusablesIn(row as HTMLElement).includes(widget)) {
        point(widget)
      }
    }

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.defaultPrevented) return
      const move = moveOf(event)
      if (!move || yieldsToCaret(event)) return
      const rows = rowsIn(grid)
      const at = cellOf(rows, document.activeElement)
      if (!at) return
      event.preventDefault()
      widgetAt(rows, move(rows, at)).focus()
    }

    // Rows and widgets come and go without a render of the grid itself — a hover cluster, a
    // filtered row, a streamed block — so the invariant is kept against the DOM, not the render.
    repoint()
    const observer = new MutationObserver(repoint)
    observer.observe(grid, { childList: true, subtree: true })
    grid.addEventListener('keydown', onKeyDown)
    grid.addEventListener('focusin', onFocusIn)
    return () => {
      observer.disconnect()
      grid.removeEventListener('keydown', onKeyDown)
      grid.removeEventListener('focusin', onFocusIn)
    }
  }, [ref])
}
