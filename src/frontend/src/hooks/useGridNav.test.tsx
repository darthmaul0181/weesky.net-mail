import { describe, it, expect } from 'vitest'
import { useRef } from 'react'
import { render, screen, waitFor, within, fireEvent } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useGridNav } from './useGridNav'

const THREE = [['A1', 'A2', 'A3'], ['B1', 'B2', 'B3'], ['C1', 'C2', 'C3']]

/** A row named by no label holds a text cell and no widget at all. */
function Grid({ rows }: { rows: string[][] }) {
  const ref = useRef<HTMLDivElement>(null)
  useGridNav({ ref })
  return (
    <>
      <button type="button">Before</button>
      <div role="grid" aria-label="Cells" ref={ref}>
        {rows.map(labels => (
          <div role="row" key={labels.join('-') || 'empty'}>
            {labels.length === 0 ? <div role="gridcell">—</div> : labels.map(label => (
              <div role="gridcell" key={label}>
                <button type="button">{label}</button>
              </div>
            ))}
          </div>
        ))}
      </div>
      <button type="button">After</button>
    </>
  )
}

/** A cell holding a text box, where the caret owns some of the same keys. */
function FieldGrid() {
  const ref = useRef<HTMLDivElement>(null)
  useGridNav({ ref })
  return (
    <div role="grid" aria-label="Cells" ref={ref}>
      <div role="row">
        <div role="gridcell"><input aria-label="Name" /></div>
        <div role="gridcell"><button type="button">A2</button></div>
      </div>
      <div role="row">
        <div role="gridcell"><button type="button">B1</button></div>
        <div role="gridcell"><button type="button">B2</button></div>
      </div>
    </div>
  )
}

const widget = (name: string) => screen.getByRole('button', { name })
const stops = () => within(screen.getByRole('grid')).getAllByRole('button')
  .map(button => button.getAttribute('tabindex'))

function press(key: string, init: Partial<KeyboardEventInit> = {}) {
  return fireEvent.keyDown(document.activeElement as HTMLElement, { key, ...init })
}

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

  it('skips a row that holds no widget', () => {
    render(<Grid rows={[['A1'], [], ['C1']]} />)
    widget('A1').focus()

    press('ArrowDown')
    expect(widget('C1')).toHaveFocus()

    press('ArrowUp')
    expect(widget('A1')).toHaveFocus()
  })

  /* A mail row mid-departure and a contacts row dropped by a filter both delete the widget holding
     the stop. The hook owns the stop, so it owns the recovery — three consumers do not. */
  it('moves the tab stop to a live widget when the focused one unmounts', async () => {
    const { rerender } = render(<Grid rows={THREE} />)
    widget('B2').focus()
    expect(stops()).toEqual(['-1', '-1', '-1', '-1', '0', '-1', '-1', '-1', '-1'])

    rerender(<Grid rows={[THREE[0], THREE[2]]} />)

    await waitFor(() => expect(stops()).toEqual(['0', '-1', '-1', '-1', '-1', '-1']))
  })
})
