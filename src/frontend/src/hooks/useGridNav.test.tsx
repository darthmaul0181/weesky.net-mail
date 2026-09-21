import { describe, it, expect, afterEach } from 'vitest'
import { useLayoutEffect, useRef } from 'react'
import { render, screen, waitFor, within, fireEvent } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { pushLayer, type LayerHandle } from '../lib/layerStack'
import { useGridNav } from './useGridNav'

const THREE = [['A1', 'A2', 'A3'], ['B1', 'B2', 'B3'], ['C1', 'C2', 'C3']]

interface GridProps {
  rows: string[][]
  /** The widget that carries `aria-pressed`, as a picked swatch or a selected row does. */
  pressed?: string
  disable?: string
  /** The row (named by its first widget) that stops being a `role="row"` at all. */
  unrow?: string
}

/** A row named by no widget holds a text cell and nothing focusable. */
function Grid({ rows, pressed, disable, unrow }: GridProps) {
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

  it('clamps to the last widget when the next row is shorter', () => {
    render(<Grid rows={[['A1', 'A2', 'A3'], ['B1', 'B2']]} />)
    widget('A3').focus()

    press('ArrowDown')
    expect(widget('B2')).toHaveFocus()

    press('ArrowUp')
    expect(widget('A2')).toHaveFocus()
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

  /* Inside the grid is not the same question as inside one of its rows: a widget whose row stopped
     being a row is unreachable by the arrows, so it cannot go on holding the stop either. */
  it('takes the stop off a widget whose row stopped being one', async () => {
    const { rerender } = render(<Grid rows={THREE} />)
    widget('B2').focus()

    rerender(<Grid rows={[THREE[0], THREE[1], [...THREE[2], 'C4']]} unrow="B1" />)

    await waitFor(() => expect(widget('A1')).toHaveAttribute('tabindex', '0'))
    expect(widget('B2')).not.toHaveAttribute('tabindex', '0')
  })
})
