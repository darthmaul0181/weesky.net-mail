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

const last = <T>(list: T[]) => list.length - 1

/** The widgets of one container: focusable, and somewhere a keyboard can work from. A roving
    `tabindex="-1"` is one; a disabled control is not, however it was made focusable, since
    `.focus()` on one is a no-op and an arrow landing there would strand the walk. */
function widgetsIn(container: HTMLElement): HTMLElement[] {
  return focusablesIn(container).filter(reachable)
}

/** The rows holding a widget, each as its own list. A row holding none is dropped rather than
    listed, which is how vertical movement steps over it instead of landing in it. */
function rowsIn(grid: HTMLElement): HTMLElement[][] {
  return Array.from(grid.querySelectorAll<HTMLElement>(ROW))
    .map(row => widgetsIn(row))
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

/** `aria-current` names the one item of a set the view is on, which a calendar spells as a token
    (`aria-current="date"`) rather than `"true"` — so the test is present and not `"false"`. */
function isCurrent(widget: HTMLElement): boolean {
  const value = widget.getAttribute('aria-current')
  return value !== null && value !== 'false'
}

/** APG puts the stop where the grid already is: a dialog reopened on Coral must not hand Tab to
    Blue, and a list with a message open must not hand Enter to that row's checkbox. */
function pickedIn(widgets: HTMLElement[]): HTMLElement | undefined {
  return widgets.find(widget => widget.getAttribute('aria-pressed') === 'true'
    || widget.getAttribute('aria-selected') === 'true' || isCurrent(widget)) ?? widgets[0]
}

/** Where a lost stop goes: whatever stands where its own row or cell stood, at the same column.
    The record names those neighbours, and the detached subtree still answers `closest`, so the
    column survives the removal that took the widget. */
function nextTo(
  grid: HTMLElement, held: HTMLElement, records: MutationRecord[],
): HTMLElement | undefined {
  const gone = records.find(record => Array.from(record.removedNodes)
    .some(node => node.contains(held)))
  const row = held.closest(ROW)
  // The column is read off the list its own row held, detached or disabled and all; the landing is
  // read off the list a keyboard can use.
  const col = row ? focusablesIn(row as HTMLElement).indexOf(held) : -1
  for (const beside of [gone?.nextSibling, gone?.previousSibling, gone?.target, row]) {
    if (!(beside instanceof Element) || !grid.contains(beside)) continue
    const widgets = widgetsIn(beside as HTMLElement)
    if (widgets.length > 0) return widgets[Math.min(Math.max(col, 0), last(widgets))]
  }
  return undefined
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
  // Where focus last was, which is not where the stop is: a control that goes disabled hands the
  // stop on while focus stays on it, so the removal that follows has to know what it is taking.
  const focused = useRef<HTMLElement | null>(null)

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

    // The whole invariant, restated: one stop, and a stop whose widget has gone recovers next
    // door. The hook owns the stop, so it owns that recovery — no consumer repeats it.
    const repoint = (records: MutationRecord[] = []) => {
      const widgets = rowsIn(grid).flat()
      const held = stop.current
      const kept = held && widgets.includes(held) ? held : null
      const target = kept ?? (held ? nextTo(grid, held, records) : undefined) ?? pickedIn(widgets)
      if (!target) { stop.current = null; return }
      // A stop that left the rows' world — a row that stopped being one, a control gone disabled —
      // is no longer in the list below, and a `0` left on it is a second tab stop.
      if (held && held !== target) tab(held, -1)
      widgets.forEach(widget => tab(widget, widget === target ? 0 : -1))
      stop.current = target
    }

    // A click, a `.focus()` of our own, a caret leaving one cell for the next: the stop follows
    // focus wherever it came from, and costs two attributes rather than a walk of the grid.
    const onFocusIn = (event: FocusEvent) => {
      const widget = event.target as HTMLElement
      const row = widget.closest(ROW)
      if (row && grid.contains(row) && widgetsIn(row as HTMLElement).includes(widget)) {
        point(widget)
        focused.current = widget
      }
    }

    // Focus leaving the grid: a `relatedTarget` of null is a blur to `<body>` — a control gone
    // disabled, a row removed — and not the user moving on, so the memory of where focus was
    // survives it; anything else clears it, or a later removal would yank focus back.
    const onFocusOut = (event: FocusEvent) => {
      const going = event.relatedTarget as Node | null
      if (going && grid.contains(going)) return
      if (going) focused.current = null
      // A stop the stylesheet has hidden is one Tab cannot reach, and the grid would be
      // unreachable: one layout read per departure, never one per mutation. `isConnected` is
      // load-bearing — a removal blurs first, and `repoint` owns that recovery and its column.
      const held = stop.current
      if (!held || !held.isConnected || held.getClientRects().length > 0) return
      const row = held.closest(ROW)
      const widgets = row ? widgetsIn(row as HTMLElement) : []
      if (widgets.length > 0) point(widgets[0])
    }

    // The stop's recovery is an attribute; the focus the removal took with it is not, and nothing
    // else gives it back — the browser drops it on `<body>` and Tab would restart at the top.
    const onMutation = (records: MutationRecord[]) => {
      const lost = focused.current
      const taken = !!lost && records.some(record => Array.from(record.removedNodes)
        .some(node => node.contains(lost)))
      repoint(records)
      if (taken && stop.current && !reachable(document.activeElement)) stop.current.focus()
    }

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.defaultPrevented) return
      const move = moveOf(event)
      if (!move || yieldsToCaret(event)) return
      const rows = rowsIn(grid)
      const at = cellOf(rows, document.activeElement)
      if (!at) return
      event.preventDefault()
      const target = widgetAt(rows, move(rows, at))
      // The stop moves first, and back if focus refuses it: a consumer that reveals a hidden widget
      // on `[tabindex="0"]` arms the arrow the way it already arms Tab, and `.focus()` on a widget
      // nothing draws is a silent no-op that would strand the walk on a dead key.
      const held = stop.current
      point(target)
      target.focus()
      if (document.activeElement !== target && held) point(held)
    }

    // Rows and widgets come and go without a render of the grid itself — a hover cluster, a
    // filtered row, a streamed block — so the invariant is kept against the DOM, not the render.
    // `disabled` is watched too: a native that goes disabled is focusable by nothing.
    repoint()
    const observer = new MutationObserver(onMutation)
    observer.observe(grid, {
      childList: true, subtree: true, attributes: true, attributeFilter: ['disabled'],
    })
    grid.addEventListener('keydown', onKeyDown)
    grid.addEventListener('focusin', onFocusIn)
    grid.addEventListener('focusout', onFocusOut)
    return () => {
      observer.disconnect()
      grid.removeEventListener('keydown', onKeyDown)
      grid.removeEventListener('focusin', onFocusIn)
      grid.removeEventListener('focusout', onFocusOut)
      focused.current = null
    }
  }, [ref])
}
