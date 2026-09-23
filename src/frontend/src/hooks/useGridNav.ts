import { useLayoutEffect, useRef, type RefObject } from 'react'
import { focusablesIn } from '../lib/layerStack'
import { reachable } from './useLayer'
import { textBox } from './useRovingFocus'

export interface GridNavOptions {
  /** The element carrying role="grid". Navigation is scoped to its role="row" descendants. */
  ref: RefObject<HTMLElement | null>
  /** The pattern's other case: a cell holding SEVERAL widgets is entered rather than walked
      into, so the arrows walk the cells themselves and F2 is the way in. */
  cellEntry?: boolean
}

interface Cell { row: number; col: number }
/** Where one key goes, before any clamping: `widgetAt` is what brings it back inside the grid. */
type Move = (rows: HTMLElement[][], at: Cell) => Cell

/** Exported for `probes/contact-tile-grid.html`, whose bench times `rowsIn`'s own two loops:
    a selector spelled twice there would stop matching the day this one moved, and the bench
    would go on reporting numbers for a grid it had found none of. */
export const ROW = '[role="row"]'
const CELL = '[role="gridcell"]'
const END = Infinity

const last = <T>(list: T[]) => list.length - 1

/** The widgets of one container: focusable, and somewhere a keyboard can work from. A roving
    `tabindex="-1"` is one; a disabled control is not, however it was made focusable, since
    `.focus()` on one is a no-op and an arrow landing there would strand the walk. */
function widgetsIn(container: HTMLElement): HTMLElement[] {
  return focusablesIn(container).filter(reachable)
}

/** What one container contributes to the walk. In cell-entry mode that is its cells themselves:
    a cell holding several widgets is entered with F2, so what it holds is no more a stop of the
    grid than a dialog's is. Elsewhere every focusable is a stop, cells being ARIA's scaffolding. */
type Stops = (nodes: HTMLElement[]) => HTMLElement[]
const everything: Stops = nodes => nodes
const cellsOnly: Stops = nodes => nodes.filter(node => node.matches(CELL))

/** The rows holding a widget, each as its own list. A row holding none is dropped rather than
    listed, which is how vertical movement steps over it instead of landing in it. */
function rowsIn(grid: HTMLElement, stops: Stops): HTMLElement[][] {
  return Array.from(grid.querySelectorAll<HTMLElement>(ROW))
    .map(row => stops(widgetsIn(row)))
    .filter(widgets => widgets.length > 0)
}

function cellOf(rows: HTMLElement[][], widget: Element | null): Cell | null {
  for (const [row, cols] of rows.entries()) {
    const col = cols.indexOf(widget as HTMLElement)
    if (col !== -1) return { row, col }
  }
  return null
}

/** Both axes clamped in one place: the grid pattern has no wrap, and a shorter row is entered at
    its own last widget rather than skipped past. */
function widgetAt(rows: HTMLElement[][], to: Cell): HTMLElement {
  // Clamped into [0, last(rows)] / [0, last(row)], so both indices are always in bounds.
  const row = rows[Math.min(Math.max(to.row, 0), last(rows))]!
  return row[Math.min(Math.max(to.col, 0), last(row))]!
}

/** The one place the key list lives, so nothing spends a key this does not answer. The widgets
    of an entered cell are a grid one column wide, where a row holds no other end to reach: Home
    and End name the cell's own two there, the way Control does in any grid. */
function moveOf(event: KeyboardEvent, oneColumn = false): Move | null {
  // Shift is refused rather than unhandled: Shift+arrow is reserved for the range selection
  // this product has not built, and a grid must not spend it on a plain move meanwhile.
  if (event.altKey || event.metaKey || event.shiftKey) return null
  const ends = event.ctrlKey || oneColumn
  switch (event.key) {
    case 'ArrowLeft': return (rows, at) => ({ row: at.row, col: at.col - 1 })
    case 'ArrowRight': return (rows, at) => ({ row: at.row, col: at.col + 1 })
    case 'ArrowUp': return (rows, at) => ({ row: at.row - 1, col: at.col })
    case 'ArrowDown': return (rows, at) => ({ row: at.row + 1, col: at.col })
    case 'Home': return ends
      ? () => ({ row: 0, col: 0 })
      : (rows, at) => ({ row: at.row, col: 0 })
    case 'End': return ends
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
  // An explicit state before a contextual mark, in two passes: in one, DOM order decided, so the
  // current month with its anchor after today opened Tab on today rather than on the chosen day.
  const chosen = (widget: HTMLElement) => widget.getAttribute('aria-pressed') === 'true'
    || widget.getAttribute('aria-selected') === 'true'
  return widgets.find(chosen) ?? widgets.find(isCurrent) ?? widgets[0]
}

/** Where a lost stop goes: whatever stands where its own row or cell stood, at the same column.
    The record names those neighbours, and the detached subtree still answers `closest`, so the
    column survives the removal that took the widget. */
function nextTo(
  grid: HTMLElement, held: HTMLElement, records: MutationRecord[], stops: Stops,
): HTMLElement | undefined {
  const gone = records.find(record => Array.from(record.removedNodes)
    .some(node => node.contains(held)))
  const row = held.closest(ROW)
  // The column is read off the list its own row held, detached or disabled and all; the landing is
  // read off the list a keyboard can use.
  const col = row ? stops(focusablesIn(row as HTMLElement)).indexOf(held) : -1
  for (const beside of [gone?.nextSibling, gone?.previousSibling, gone?.target, row]) {
    if (!(beside instanceof Element) || !grid.contains(beside)) continue
    const widgets = stops(widgetsIn(beside as HTMLElement))
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
 * Roving tabindex over a `role="grid"`: one tab stop at a time, the arrows and Home/End walking the
 * widgets a cell holds — or, under `cellEntry`, the cells themselves, which F2 enters and Escape
 * leaves. The listener is the container's: a grid is not a layer and must not compete for Escape.
 */
export function useGridNav({ ref, cellEntry }: GridNavOptions): void {
  const stop = useRef<HTMLElement | null>(null)
  // Where focus last was, which is not where the stop is: a control that goes disabled hands the
  // stop on while focus stays on it, so the removal that follows has to know what it is taking.
  const focused = useRef<HTMLElement | null>(null)

  useLayoutEffect(() => {
    const grid = ref.current
    if (!grid) return undefined

    // The rows the arrows walk, built once per change rather than once per keypress — an arrow was
    // 19ms of walk at 2000 rows. Dropped by the observer and by nothing else, which is the same set
    // of changes `repoint` answers to; local to the effect, so no re-run can inherit another grid's.
    const stops: Stops = cellEntry ? cellsOnly : everything
    const widgetsOf = (container: HTMLElement) => stops(widgetsIn(container))
    let matrix: HTMLElement[][] | null = null
    const rowsNow = () => (matrix ??= rowsIn(grid, stops))

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
      const widgets = rowsNow().flat()
      const held = stop.current
      const kept = held && widgets.includes(held) ? held : null
      const target = kept ?? (held ? nextTo(grid, held, records, stops) : undefined)
        ?? pickedIn(widgets)
      if (!target) { stop.current = null; return }
      // A stop that left the rows' world — a row that stopped being one, a control gone disabled —
      // is no longer in the list below, and a `0` left on it is a second tab stop.
      if (held && held !== target) tab(held, -1)
      // Under `cellEntry` every focusable the grid holds, the widgets inside a cell being no
      // stops of its own and a native left at its own 0 one Tab press each; elsewhere the walk's
      // own list, which is that same set and one pass of a 2000-row grid cheaper.
      const owned = cellEntry ? widgetsIn(grid) : widgets
      owned.forEach(widget => tab(widget, widget === target ? 0 : -1))
      stop.current = target
    }

    // A click, a `.focus()` of our own, a caret leaving one cell for the next: the stop follows
    // focus wherever it came from, and costs two attributes rather than a walk of the grid.
    const onFocusIn = (event: FocusEvent) => {
      const target = event.target as HTMLElement
      const row = target.closest(ROW)
      if (!row || !grid.contains(row)) return
      const widgets = widgetsOf(row as HTMLElement)
      // A widget inside an entered cell holds no stop of its own: the cell does, so a chip clicked
      // with the pointer leaves the grid pointing at the day it belongs to.
      const widget = widgets.includes(target) ? target : widgets.find(one => one.contains(target))
      if (!widget) return
      point(widget)
      focused.current = target
    }

    // Focus leaving the grid: a `relatedTarget` of null is a blur to `<body>` — a control gone
    // disabled, a row removed — and not the user moving on, so the memory of where focus was
    // survives it; anything else clears it, or a later removal would yank focus back.
    const onFocusOut = (event: FocusEvent) => {
      const going = event.relatedTarget as Node | null
      if (going && grid.contains(going)) return
      if (going) focused.current = null
      // The fallback for a consumer that forgot the reveal rule: every shipped one reveals on
      // `[tabindex="0"]` or hides nothing, so this is dead on all five. `isConnected` keeps it off
      // a removal, which blurs first and whose recovery is `repoint`'s, with the column.
      const held = stop.current
      if (!held || !held.isConnected || held.getClientRects().length > 0) return
      const row = held.closest(ROW)
      const widgets = row ? widgetsOf(row as HTMLElement) : []
      if (widgets.length > 0) point(widgets[0]!)
    }

    // The stop's recovery is an attribute; the focus the removal took with it is not, and nothing
    // else gives it back — the browser drops it on `<body>` and Tab would restart at the top.
    const onMutation = (records: MutationRecord[]) => {
      matrix = null
      const lost = focused.current
      const taken = !!lost && records.some(record => Array.from(record.removedNodes)
        .some(node => node.contains(lost)))
      repoint(records)
      if (taken && stop.current && !reachable(document.activeElement)) stop.current.focus()
    }

    // The cell focus is inside but not on — where F2 put it, or a click on a chip. `null`
    // everywhere else, which is what leaves Escape to the layer stack and the arrows to the grid.
    const entered = (): HTMLElement | null => {
      const at = document.activeElement
      if (!cellEntry || !(at instanceof HTMLElement) || !grid.contains(at)) return null
      const cell = at.closest<HTMLElement>(CELL)
      return cell && cell !== at ? cell : null
    }

    /** APG: F2 places focus on the first widget a cell holds, and a second F2 restores grid
        navigation; Escape does too. Escape has a standing owner, so it is spent only when there is
        a cell to come back from — and marked when it is, or a dialog underneath closes on it. */
    const cellKey = (event: KeyboardEvent, inside: HTMLElement | null): boolean => {
      if (event.key !== 'F2' && event.key !== 'Escape') return false
      const at = document.activeElement as HTMLElement | null
      const target = inside
        ?? (event.key === 'F2' && at?.matches(CELL) ? widgetsIn(at)[0] : undefined)
      if (!target) return false
      event.preventDefault()
      target.focus()
      return true
    }

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.defaultPrevented) return
      const inside = entered()
      if (cellEntry && cellKey(event, inside)) return
      const move = moveOf(event, inside !== null)
      if (!move || yieldsToCaret(event)) return
      // Inside a cell the arrows walk what it holds, in the order it draws them — a column of one
      // widget each, since a cell stacks its widgets — and the stop stays on the cell itself.
      const rows = inside ? widgetsIn(inside).map(widget => [widget]) : rowsNow()
      const at = cellOf(rows, document.activeElement)
      if (!at) return
      event.preventDefault()
      const target = widgetAt(rows, move(rows, at))
      if (inside) { target.focus(); return }
      // The stop moves first, and back if focus refuses it: a consumer that reveals a hidden widget
      // on `[tabindex="0"]` arms the arrow the way it already arms Tab, and `.focus()` on a widget
      // nothing draws is a silent no-op that would strand the walk on a dead key.
      const held = stop.current
      point(target)
      target.focus()
      if (document.activeElement !== target) point(held ?? widgetAt(rows, at))
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
  }, [ref, cellEntry])
}
