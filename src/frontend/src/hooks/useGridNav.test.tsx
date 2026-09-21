import { describe, it, expect, afterEach, vi } from 'vitest'
import { useLayoutEffect, useRef, type AriaAttributes } from 'react'
import { render, screen, waitFor, within, fireEvent } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { pushLayer, type LayerHandle } from '../lib/layerStack'
import { useGridNav } from './useGridNav'

const THREE = [['A1', 'A2', 'A3'], ['B1', 'B2', 'B3'], ['C1', 'C2', 'C3']]

interface GridProps {
  rows: string[][]
  /** The widget that carries `aria-pressed`, as a picked swatch or a selected row does. */
  pressed?: string
  /** The widget that carries `aria-current`, and the value it carries: a mail row spells it
      `"true"`, a calendar's today cell `"date"`. */
  current?: [string, AriaAttributes['aria-current']]
  disable?: string
  /** The row (named by its first widget) that stops being a `role="row"` at all. */
  unrow?: string
}

/** A row named by no widget holds a text cell and nothing focusable. */
function Grid({ rows, pressed, current, disable, unrow }: GridProps) {
  const ref = useRef<HTMLDivElement>(null)
  useGridNav({ ref })
  return (
    <>
      <button type="button">Before</button>
      <div role="grid" aria-label="Cells" ref={ref}>
        {rows.map(labels => (
          <div role={labels[0] === unrow ? undefined : 'row'} key={labels.join('-') || 'empty'}>
            {labels.length === 0 ? <div role="gridcell">—</div> : labels.map(label => (
              <div role="gridcell" key={label}>
                <button type="button" disabled={label === disable}
                  aria-current={current && label === current[0] ? current[1] : undefined}
                  aria-pressed={label === pressed ? true : undefined}>{label}</button>
              </div>
            ))}
          </div>
        ))}
      </div>
      <button type="button">After</button>
    </>
  )
}

/** Cells holding the three controls a caret's keys have to be told apart by. */
function FieldGrid() {
  const ref = useRef<HTMLDivElement>(null)
  useGridNav({ ref })
  return (
    <div role="grid" aria-label="Cells" ref={ref}>
      <div role="row">
        <div role="gridcell"><input aria-label="Name" /></div>
        <div role="gridcell"><textarea aria-label="Notes" /></div>
        <div role="gridcell"><button type="button">A3</button></div>
      </div>
      <div role="row">
        <div role="gridcell"><button type="button">B1</button></div>
        <div role="gridcell"><button type="button">B2</button></div>
        <div role="gridcell"><button type="button">B3</button></div>
      </div>
      <div role="row">
        <div role="gridcell"><input type="checkbox" aria-label="Pick" /></div>
        <div role="gridcell"><button type="button">C2</button></div>
        <div role="gridcell"><button type="button">C3</button></div>
      </div>
    </div>
  )
}

/** A cell that answers a key itself, where React's own delegated handler cannot: the hook must
    leave a keydown something nearer the target has already spent. */
function GuardedGrid() {
  const ref = useRef<HTMLDivElement>(null)
  useGridNav({ ref })
  return (
    <div role="grid" aria-label="Cells" ref={ref}>
      <div role="row">
        <div role="gridcell">
          <button type="button"
            ref={node => { node?.addEventListener('keydown', event => event.preventDefault()) }}>
            A1
          </button>
        </div>
        <div role="gridcell"><button type="button">A2</button></div>
      </div>
    </div>
  )
}

/** The mail row's content cell: a cell that is the activation target itself and holds a widget
    besides. Without `cellEntry` the two are stops alike. */
function NestedGrid() {
  const ref = useRef<HTMLDivElement>(null)
  useGridNav({ ref })
  return (
    <div role="grid" aria-label="Cells" ref={ref}>
      <div role="row">
        <div role="gridcell"><button type="button">A1</button></div>
        <div role="gridcell" tabIndex={-1} aria-label="Outer">
          <button type="button">Inner</button>
        </div>
      </div>
    </div>
  )
}

/** The other case the pattern names: a cell that is the activation target itself AND holds
    widgets of its own — a month's day cell, with its chips and its "+N more". */
function CellGrid({ guard }: { guard?: boolean }) {
  const ref = useRef<HTMLDivElement>(null)
  useGridNav({ ref, cellEntry: true })
  return (
    <>
      <button type="button">Before</button>
      <div role="grid" aria-label="Days" ref={ref}>
        {['A', 'B'].map(row => (
          <div role="row" key={row}>
            <div role="rowheader">{row}</div>
            {[1, 2].map(column => (
              <div role="gridcell" tabIndex={-1} aria-label={`${row}${column}`} key={column}>
                {row === 'A' && column === 1 && (
                  <>
                    <button type="button"
                      ref={node => {
                        if (guard) node?.addEventListener('keydown', e => e.preventDefault())
                      }}>One</button>
                    <button type="button">Two</button>
                  </>
                )}
              </div>
            ))}
          </div>
        ))}
      </div>
      <button type="button">After</button>
    </>
  )
}

const opened: LayerHandle[] = []

/** The shipped shape of the leak: a roving grid inside a dialog's Tab trap. */
function TrappedGrid() {
  const ref = useRef<HTMLDivElement>(null)
  const trap = useRef<HTMLDivElement>(null)
  useGridNav({ ref })
  useLayoutEffect(() => {
    const handle = pushLayer({ trap: trap.current })
    opened.push(handle)
    return () => handle.remove()
  }, [])
  return (
    <>
      <button type="button">Outside</button>
      <div ref={trap}>
        <button type="button">Before</button>
        <div role="grid" aria-label="Cells" ref={ref}>
          {THREE.map(labels => (
            <div role="row" key={labels[0]}>
              {labels.map(label => (
                <div role="gridcell" key={label}>
                  <button type="button">{label}</button>
                </div>
              ))}
            </div>
          ))}
        </div>
      </div>
    </>
  )
}

const widget = (name: string) => screen.getByRole('button', { name })
const stops = () => within(screen.getByRole('grid')).getAllByRole('button')
  .map(button => button.getAttribute('tabindex'))

function press(key: string, init: Partial<KeyboardEventInit> = {}) {
  return fireEvent.keyDown(document.activeElement as HTMLElement, { key, ...init })
}

/* jsdom lays nothing out, so every element answers `getClientRects()` with nothing at all — which
   is the answer a `display: none` widget gives in a browser. The visible ones are therefore given
   a rect by hand, so the one the stylesheet would hide is the only one without. */
function drawn(element: Element, yes: boolean) {
  Object.defineProperty(element, 'getClientRects', {
    configurable: true, value: () => (yes ? [{ width: 26, height: 26 }] : []),
  })
}

afterEach(() => { opened.splice(0).forEach(handle => handle.remove()) })

describe('useGridNav', () => {
  it('puts exactly one widget in the tab sequence', () => {
    render(<Grid rows={THREE} />)

    expect(stops()).toEqual(['0', '-1', '-1', '-1', '-1', '-1', '-1', '-1', '-1'])
  })

  it('moves along the row with the arrow keys', () => {
    render(<Grid rows={THREE} />)
    widget('A1').focus()

    press('ArrowRight')
    expect(widget('A2')).toHaveFocus()
    press('ArrowRight')
    expect(widget('A3')).toHaveFocus()
    // The grid pattern clamps where the menu pattern wraps: a row's end is not its start.
    press('ArrowRight')
    expect(widget('A3')).toHaveFocus()
    press('ArrowLeft')
    expect(widget('A2')).toHaveFocus()
  })

  it('carries the tab stop along with the focus', () => {
    render(<Grid rows={THREE} />)
    widget('A1').focus()

    press('ArrowDown')

    expect(stops()).toEqual(['-1', '-1', '-1', '0', '-1', '-1', '-1', '-1', '-1'])
  })

  it('carries the tab stop to a widget clicked with the pointer', async () => {
    render(<Grid rows={THREE} />)

    await userEvent.click(widget('C3'))

    expect(stops()).toEqual(['-1', '-1', '-1', '-1', '-1', '-1', '-1', '-1', '0'])
  })

  it('moves to the same index in the next row', () => {
    render(<Grid rows={THREE} />)
    widget('A2').focus()

    press('ArrowDown')
    expect(widget('B2')).toHaveFocus()
    press('ArrowDown')
    expect(widget('C2')).toHaveFocus()
    press('ArrowDown')
    expect(widget('C2')).toHaveFocus()
    press('ArrowUp')
    expect(widget('B2')).toHaveFocus()
  })

  /* A consumer reveals a widget hidden at rest on `[tabindex="0"]`, and that attribute is the
     hook's: an arrow has to write it BEFORE focusing, or `.focus()` is a no-op, no `focusin` follows
     and the key is dead. jsdom focuses hidden elements, so only the order is visible here. */
  it('arms the target with the tab stop before it focuses it', () => {
    render(<Grid rows={THREE} />)
    widget('A2').focus()
    let armed: string | null = 'never focused'
    const target = widget('B2')
    const spy = vi.spyOn(target, 'focus').mockImplementation(function (this: HTMLElement) {
      armed = target.getAttribute('tabindex')
      HTMLElement.prototype.focus.call(this)
    })

    press('ArrowDown')

    expect(armed).toBe('0')
    spy.mockRestore()
  })

  // And rolls it back when the widget refuses after all, or the arrow would leave the stop on
  // something nothing can focus — the very state the reveal exists to prevent.
  it('leaves the stop where it was when the target refuses focus', () => {
    render(<Grid rows={THREE} />)
    widget('A2').focus()
    const target = widget('B2')
    const spy = vi.spyOn(target, 'focus').mockImplementation(() => {})

    press('ArrowDown')

    expect(widget('A2')).toHaveAttribute('tabindex', '0')
    expect(target).toHaveAttribute('tabindex', '-1')
    expect(widget('A2')).toHaveFocus()
    spy.mockRestore()
  })

  it('clamps to the last widget when the next row is shorter', () => {
    render(<Grid rows={[['A1', 'A2', 'A3'], ['B1', 'B2']]} />)
    widget('A3').focus()

    press('ArrowDown')
    expect(widget('B2')).toHaveFocus()

    press('ArrowUp')
    expect(widget('A2')).toHaveFocus()
  })

  /* The rows the arrows walk are built by `repoint` and kept, so a keydown does no walk of its own
     — the observer below invalidates them, and it watches exactly the changes that can: a row or a
     widget added or removed, and a control gone disabled. These two prove both halves of that
     invalidation; without it the arrow would walk a grid that no longer exists. */
  it('walks the rows a mutation rebuilt, not the ones it had', async () => {
    const { rerender } = render(<Grid rows={[THREE[0], THREE[1]]} />)
    widget('A2').focus()
    press('ArrowDown')
    expect(widget('B2')).toHaveFocus()

    rerender(<Grid rows={THREE} />)
    await waitFor(() => expect(screen.queryByRole('button', { name: 'C2' })).not.toBeNull())

    press('ArrowDown')

    expect(widget('C2')).toHaveFocus()
  })

  // The other half, and the one only the attribute filter can catch: nothing was added or removed,
  // a button simply stopped being somewhere a keyboard can work from.
  it('steps over a widget that went disabled since the last walk', async () => {
    const { rerender } = render(<Grid rows={THREE} />)
    widget('B1').focus()

    rerender(<Grid rows={THREE} disable="B2" />)
    await waitFor(() => expect(widget('B2')).toBeDisabled())

    press('ArrowRight')

    expect(widget('B3')).toHaveFocus()
  })

  it('takes Home and End to the ends of the row', () => {
    render(<Grid rows={THREE} />)
    widget('B2').focus()

    press('End')
    expect(widget('B3')).toHaveFocus()
    press('Home')
    expect(widget('B1')).toHaveFocus()
  })

  it('takes Control+Home and Control+End to the ends of the grid', () => {
    render(<Grid rows={THREE} />)
    widget('B2').focus()

    press('Home', { ctrlKey: true })
    expect(widget('A1')).toHaveFocus()
    press('End', { ctrlKey: true })
    expect(widget('C3')).toHaveFocus()
  })

  /* One stop in, one stop out: Tab leaves the grid rather than walking its widgets, which is the
     whole point of the roving tabindex. */
  it('leaves the grid on Tab', async () => {
    render(<Grid rows={THREE} />)
    widget('B2').focus()

    await userEvent.tab()
    expect(widget('After')).toHaveFocus()

    widget('B2').focus()
    await userEvent.tab({ shift: true })
    expect(widget('Before')).toHaveFocus()
  })

  /* A demoted widget is still focusable, so it is still in `focusablesIn` — and a trap reading that
     list would find its last item past the last one Tab can reach, prevent nothing, and let focus
     walk out of the dialog. `tabbablesIn` is the list Tab asks for. */
  it('keeps Tab inside a trap standing over a roving grid', async () => {
    render(<TrappedGrid />)
    widget('A1').focus()

    await userEvent.tab()

    expect(widget('Before')).toHaveFocus()
    expect(widget('Outside')).not.toHaveFocus()
  })

  /* The layer stack's own listener and the mail list's row keys both read `defaultPrevented`
     before they act, so a key the grid does not spend has to reach them unmarked. */
  it('does not preventDefault a key it does not spend', () => {
    render(<Grid rows={THREE} />)
    widget('A1').focus()

    expect(press('ArrowRight')).toBe(false)
    expect(press('Escape')).toBe(true)
    expect(press('Tab')).toBe(true)
    expect(press('PageDown')).toBe(true)
    // F2 is Task 5's cell-entry key and means nothing here yet.
    expect(press('F2')).toBe(true)
    expect(press('a')).toBe(true)
    expect(press('ArrowDown', { altKey: true })).toBe(true)
    expect(press('ArrowDown', { shiftKey: true })).toBe(true)
  })

  it('leaves a key alone once a handler nearer the target has spent it', () => {
    render(<GuardedGrid />)
    widget('A1').focus()

    press('ArrowRight')

    expect(widget('A1')).toHaveFocus()
  })

  /* `useRovingFocus`' guards, unchanged: Home and End are the caret's in any text box, and ←/→
     are too, while ↓/↑ stay the grid's in a one-line field. */
  it('yields Home and End to a text box inside a cell', () => {
    render(<FieldGrid />)
    const field = screen.getByLabelText('Name')
    field.focus()

    expect(press('Home')).toBe(true)
    expect(press('End')).toBe(true)
    expect(press('ArrowRight')).toBe(true)
    expect(field).toHaveFocus()

    press('ArrowDown')
    expect(widget('B1')).toHaveFocus()
  })

  // A textarea has lines of its own for the vertical arrows to move between.
  it('yields the vertical arrows to a multi-line box as well', () => {
    render(<FieldGrid />)
    const notes = screen.getByLabelText('Notes')
    notes.focus()

    expect(press('ArrowDown')).toBe(true)
    expect(press('ArrowUp')).toBe(true)
    expect(notes).toHaveFocus()
  })

  /* A checkbox is an `<input>` holding no text, and the mail row begins with one: reading it as a
     text box would hand it every key the grid navigates with. */
  it('moves off a checkbox with the arrow keys', () => {
    render(<FieldGrid />)
    const box = screen.getByLabelText('Pick')
    box.focus()

    expect(press('ArrowRight')).toBe(false)
    expect(widget('C2')).toHaveFocus()

    box.focus()
    press('End')
    expect(widget('C3')).toHaveFocus()
  })

  it('skips a row that holds no widget', () => {
    render(<Grid rows={[['A1'], [], ['C1']]} />)
    widget('A1').focus()

    press('ArrowDown')
    expect(widget('C1')).toHaveFocus()

    press('ArrowUp')
    expect(widget('A1')).toHaveFocus()
  })

  // APG puts the stop on the selected item, so Tab into the grid lands where the state is.
  it('opens the tab stop on a pressed widget rather than the first', () => {
    render(<Grid rows={THREE} pressed="B2" />)

    expect(stops()).toEqual(['-1', '-1', '-1', '-1', '0', '-1', '-1', '-1', '-1'])
  })

  /* A mail row mid-departure and a contacts row dropped by a filter both delete the widget holding
     the stop. The hook owns the stop, so it owns the recovery — three consumers do not. And the
     browser keeps its sequential-navigation point where the row was, so the top of a thousand rows
     is the wrong place to land. */
  it('moves the tab stop to a live widget when the focused one unmounts', async () => {
    const { rerender } = render(<Grid rows={THREE} />)
    widget('B2').focus()
    expect(stops()).toEqual(['-1', '-1', '-1', '-1', '0', '-1', '-1', '-1', '-1'])

    rerender(<Grid rows={[THREE[0], THREE[2]]} />)

    await waitFor(() => expect(stops()).toEqual(['-1', '-1', '-1', '-1', '0', '-1']))
    expect(widget('C2')).toHaveAttribute('tabindex', '0')
  })

  it('recovers upward when the row that went was the last', async () => {
    const { rerender } = render(<Grid rows={THREE} />)
    widget('C3').focus()

    rerender(<Grid rows={[THREE[0], THREE[1]]} />)

    await waitFor(() => expect(widget('B3')).toHaveAttribute('tabindex', '0'))
    expect(stops()).toEqual(['-1', '-1', '-1', '-1', '-1', '0'])
  })

  /* A native that goes disabled is focusable by nothing, so the grid would otherwise hold zero tab
     stops until some unrelated node changed. */
  it('hands the tab stop on when the widget holding it goes disabled', async () => {
    const { rerender } = render(<Grid rows={THREE} />)
    widget('B2').focus()

    rerender(<Grid rows={THREE} disable="B2" />)

    await waitFor(() => expect(widget('B3')).toHaveAttribute('tabindex', '0'))
    expect(widget('B2')).not.toHaveAttribute('tabindex', '0')
  })

  // `aria-current` names the one item of a set the view is on — the message open in the reader,
  // today's date — and Tab into the grid has to land there, or Enter opens the wrong thing.
  it('opens the tab stop on the current widget as well as a pressed one', () => {
    render(<Grid rows={THREE} current={['C1', 'true']} />)

    expect(stops()).toEqual(['-1', '-1', '-1', '-1', '-1', '-1', '0', '-1', '-1'])
  })

  // A calendar's today cell carries a token, not a boolean: the test is present and not "false".
  it('reads a token aria-current', () => {
    render(<Grid rows={THREE} current={['B3', 'date']} />)

    expect(stops()).toEqual(['-1', '-1', '-1', '-1', '-1', '0', '-1', '-1', '-1'])
  })

  /* Both at once, the mark drawn first: DOM order used to decide, so the current month with its
     anchor after today opened Tab on today rather than on the day the URL is about. */
  it('opens on an explicit state ahead of a contextual mark that comes before it', () => {
    render(<Grid rows={THREE} pressed="C1" current={['A2', 'date']} />)

    expect(stops()).toEqual(['-1', '-1', '-1', '-1', '-1', '-1', '0', '-1', '-1'])
  })

  // Asserted on a first render, not a rerender: `aria-current` is not in the observer's filter, so
  // a changed value fires no repoint and a rerender would pass whatever the predicate answered.
  it('ignores an explicit aria-current of false', () => {
    render(<Grid rows={THREE} current={['B3', 'false']} />)

    expect(stops()).toEqual(['0', '-1', '-1', '-1', '-1', '-1', '-1', '-1', '-1'])
  })

  /* A row deleted from the keyboard leaves focus on `<body>`: the stop's recovery is an attribute,
     and an attribute is not somewhere a keyboard can go on working from. */
  it('hands focus back, not only the stop, when the row holding it goes', async () => {
    const { rerender } = render(<Grid rows={THREE} />)
    widget('B2').focus()

    rerender(<Grid rows={[THREE[0], THREE[2]]} />)

    await waitFor(() => expect(widget('C2')).toHaveFocus())
  })

  /* And never when the user has already moved on: a background removal must not yank focus out of
     whatever it is now in. */
  it('leaves focus alone when the user has already left the grid', async () => {
    const { rerender } = render(<Grid rows={THREE} />)
    // Drawn, so leaving the grid is the ordinary departure rather than the hidden-stop one below.
    screen.getAllByRole('button').forEach(button => drawn(button, true))
    widget('B2').focus()
    widget('After').focus()

    rerender(<Grid rows={[THREE[0], THREE[2]]} />)

    await waitFor(() => expect(widget('C2')).toHaveAttribute('tabindex', '0'))
    expect(widget('After')).toHaveFocus()
  })

  /* The stylesheet may hide the widget holding the stop — the mail row's action cluster is
     `display: none` until the row is hovered — and a hidden widget is in no tab order at all, so
     Tab would never come back into the grid. One layout read, on the way out. */
  it('moves a stop nothing draws when focus leaves the grid', async () => {
    render(<Grid rows={THREE} />)
    const hidden = widget('B3')
    screen.getAllByRole('button').forEach(button => drawn(button, button !== hidden))
    hidden.focus()
    expect(hidden).toHaveAttribute('tabindex', '0')

    await userEvent.tab()

    expect(widget('B1')).toHaveAttribute('tabindex', '0')
    expect(hidden).toHaveAttribute('tabindex', '-1')
  })

  it('leaves a stop that is drawn exactly where it is', async () => {
    render(<Grid rows={THREE} />)
    screen.getAllByRole('button').forEach(button => drawn(button, true))
    widget('B3').focus()

    await userEvent.tab()

    expect(widget('B3')).toHaveAttribute('tabindex', '0')
  })

  /* Inside the grid is not the same question as inside one of its rows: a widget whose row stopped
     being a row is unreachable by the arrows, so it cannot go on holding the stop either. */
  it('takes the stop off a widget whose row stopped being one', async () => {
    const { rerender } = render(<Grid rows={THREE} />)
    widget('B2').focus()

    rerender(<Grid rows={[THREE[0], THREE[1], [...THREE[2], 'C4']]} unrow="B1" />)

    await waitFor(() => expect(widget('A1')).toHaveAttribute('tabindex', '0'))
    expect(widget('B2')).not.toHaveAttribute('tabindex', '0')
  })

  /* The stop follows focus to the widget it landed on, never to the one around it: a row of this
     shape is the mail list's own, where the content cell and its thread toggle are two stops. */
  it('points the stop at a nested widget itself, not at the widget around it', () => {
    render(<NestedGrid />)
    const outer = screen.getByRole('gridcell', { name: 'Outer' })
    widget('Inner').focus()

    expect(widget('Inner')).toHaveAttribute('tabindex', '0')
    expect(outer).toHaveAttribute('tabindex', '-1')

    press('ArrowLeft')
    expect(outer).toHaveFocus()
  })

  /* The pattern's cell-entry mode, for the other case of the two: a cell holding SEVERAL widgets
     is entered rather than walked into, so the arrows stay on the cells and F2 is the way in. */
  describe('entering a cell', () => {
    const cell = (name: string) => screen.getByRole('gridcell', { name })

    it('walks the cells rather than the widgets they hold', () => {
      render(<CellGrid />)
      cell('A1').focus()

      press('ArrowRight')
      expect(cell('A2')).toHaveFocus()
      press('ArrowDown')
      expect(cell('B2')).toHaveFocus()
    })

    it('holds one tab stop, on a cell, whatever the cells hold', () => {
      render(<CellGrid />)

      expect(within(screen.getByRole('grid')).getAllByRole('button')
        .map(button => button.getAttribute('tabindex'))).toEqual(['-1', '-1'])
      expect(cell('A1')).toHaveAttribute('tabindex', '0')
    })

    it('places focus on the first widget in the cell on F2', () => {
      render(<CellGrid />)
      cell('A1').focus()

      expect(press('F2')).toBe(false)

      expect(widget('One')).toHaveFocus()
      // The stop stays on the cell: the widgets inside it are no more stops of the grid than a
      // dialog's are, and Tab still leaves the grid altogether.
      expect(cell('A1')).toHaveAttribute('tabindex', '0')
    })

    it('walks the widgets of the cell it is inside', () => {
      render(<CellGrid />)
      cell('A1').focus()
      press('F2')

      press('ArrowDown')
      expect(widget('Two')).toHaveFocus()
      press('ArrowUp')
      expect(widget('One')).toHaveFocus()
    })

    // A one-column grid has no row to walk, so a key that names an end has to name the cell's.
    it('takes Home and End to the ends of the cell it is inside', () => {
      render(<CellGrid />)
      cell('A1').focus()
      press('F2')

      expect(press('End')).toBe(false)
      expect(widget('Two')).toHaveFocus()
      press('Home')
      expect(widget('One')).toHaveFocus()
    })

    it('restores grid navigation on Escape', () => {
      render(<CellGrid />)
      cell('A1').focus()
      press('F2')

      expect(press('Escape')).toBe(false)

      expect(cell('A1')).toHaveFocus()
      press('ArrowRight')
      expect(cell('A2')).toHaveFocus()
    })

    it('restores grid navigation on a second F2', () => {
      render(<CellGrid />)
      cell('A1').focus()
      press('F2')
      expect(widget('One')).toHaveFocus()

      press('F2')

      expect(cell('A1')).toHaveFocus()
    })

    /* Escape has a standing owner: a grid is not a layer, so the key is spent only when there is
       a cell to come back from — and marked when it is, or the dialog underneath closes too. */
    it('leaves Escape to the layer stack while focus is on the cell itself', () => {
      const onEscape = vi.fn()
      render(<CellGrid />)
      opened.push(pushLayer({ onEscape }))
      cell('A1').focus()

      press('Escape')

      expect(onEscape).toHaveBeenCalledTimes(1)
    })

    it('keeps the layer stack out of an Escape that left a cell', () => {
      const onEscape = vi.fn()
      render(<CellGrid />)
      opened.push(pushLayer({ onEscape }))
      cell('A1').focus()
      press('F2')

      press('Escape')

      expect(onEscape).not.toHaveBeenCalled()
      expect(cell('A1')).toHaveFocus()
    })

    it('does not swallow Escape when the event is already defaultPrevented', () => {
      render(<CellGrid guard />)
      cell('A1').focus()
      press('F2')

      press('Escape')

      expect(widget('One')).toHaveFocus()
    })

    it('leaves F2 alone in a cell that holds no widget', () => {
      render(<CellGrid />)
      cell('A2').focus()

      expect(press('F2')).toBe(true)

      expect(cell('A2')).toHaveFocus()
    })

    // A chip clicked with the pointer is inside a cell too, and the grid has to know which day
    // the arrows resume from — the cell holding it, not wherever the stop happened to be.
    it('points the stop at the cell holding a widget focused from outside', () => {
      render(<CellGrid />)
      widget('Two').focus()

      expect(cell('A1')).toHaveAttribute('tabindex', '0')
      press('Escape')
      expect(cell('A1')).toHaveFocus()
    })
  })
})
