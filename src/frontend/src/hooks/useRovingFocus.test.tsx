import { describe, it, expect } from 'vitest'
import { useRef } from 'react'
import { render, screen, fireEvent } from '@testing-library/react'
import { useRovingFocus } from './useRovingFocus'

function Menu({ open, extras }: { open: boolean; extras?: boolean }) {
  const ref = useRef<HTMLDivElement>(null)
  const keys = useRovingFocus({ active: open, containerRef: ref })
  if (!open) return null
  return (
    <div role="menu" ref={ref} tabIndex={-1} onKeyDown={keys}>
      {['One', 'Two', 'Three'].map(label => (
        <button key={label} type="button" role="menuitem" disabled={extras && label === 'Two'}>
          {label}
        </button>
      ))}
      {extras && <button type="button" role="menuitem">Four</button>}
    </div>
  )
}

/** A combobox's own field sits inside the surface the arrows walk. */
function Combobox() {
  const ref = useRef<HTMLDivElement>(null)
  const keys = useRovingFocus({ active: true, containerRef: ref })
  return (
    // eslint-disable-next-line jsx-a11y/no-static-element-interactions -- test harness, not real UI
    <div ref={ref} onKeyDown={keys}>
      {/* eslint-disable-next-line jsx-a11y/no-autofocus -- stands in for a field the surface autofocuses */}
      <input aria-label="Filter" autoFocus />
      <button type="button">Ada</button>
      <button type="button">Grace</button>
    </div>
  )
}

/** The two multi-line boxes, where ↓/↑ move between lines rather than between items. */
function Prose() {
  const ref = useRef<HTMLDivElement>(null)
  const keys = useRovingFocus({ active: true, containerRef: ref })
  return (
    // eslint-disable-next-line jsx-a11y/no-static-element-interactions -- test harness, not real UI
    <div ref={ref} onKeyDown={keys}>
      <textarea aria-label="Body" />
      <div role="textbox" aria-label="Rich" contentEditable tabIndex={0} />
      <button type="button">After</button>
    </div>
  )
}

const item = (name: string) => screen.getByRole('menuitem', { name })

describe('useRovingFocus', () => {
  it('focuses the first item when the surface opens', () => {
    render(<Menu open />)

    expect(item('One')).toHaveFocus()
  })

  it('moves down and up between the items', () => {
    render(<Menu open />)

    fireEvent.keyDown(item('One'), { key: 'ArrowDown' })
    expect(item('Two')).toHaveFocus()

    fireEvent.keyDown(item('Two'), { key: 'ArrowUp' })
    expect(item('One')).toHaveFocus()
  })

  it('wraps at both ends', () => {
    render(<Menu open />)

    fireEvent.keyDown(item('One'), { key: 'ArrowUp' })
    expect(item('Three')).toHaveFocus()

    fireEvent.keyDown(item('Three'), { key: 'ArrowDown' })
    expect(item('One')).toHaveFocus()
  })

  it('jumps to the ends on Home and End', () => {
    render(<Menu open />)

    fireEvent.keyDown(item('One'), { key: 'End' })
    expect(item('Three')).toHaveFocus()

    fireEvent.keyDown(item('Three'), { key: 'Home' })
    expect(item('One')).toHaveFocus()
  })

  /* The stack's own listener and the mail list's row keys both read `defaultPrevented` before they
     act: a key this walk spends must be marked, or something underneath answers it too. */
  it('marks the keys it spends and leaves every other one alone', () => {
    render(<Menu open />)
    const first = item('One')

    expect(fireEvent.keyDown(first, { key: 'ArrowDown' })).toBe(false)
    expect(fireEvent.keyDown(item('Two'), { key: 'Home' })).toBe(false)
    expect(fireEvent.keyDown(item('One'), { key: 'End' })).toBe(false)
    expect(fireEvent.keyDown(item('Three'), { key: 'ArrowUp' })).toBe(false)
    expect(fireEvent.keyDown(item('Two'), { key: 'Escape' })).toBe(true)
    expect(fireEvent.keyDown(item('Two'), { key: 'Tab' })).toBe(true)
    expect(fireEvent.keyDown(item('Two'), { key: 'a' })).toBe(true)
  })

  /* In the menu pattern ←/→ open and close a submenu; a menu that walked on them would be teaching
     a dialect of its own. */
  it('leaves the horizontal arrows alone', () => {
    render(<Menu open />)

    expect(fireEvent.keyDown(item('One'), { key: 'ArrowRight' })).toBe(true)
    expect(fireEvent.keyDown(item('One'), { key: 'ArrowLeft' })).toBe(true)
    expect(item('One')).toHaveFocus()
  })

  /* `tabbablesIn` is the layer stack's own list, so the walk and its Tab cannot disagree about
     where focus may go — a disabled row is in neither. */
  it('steps over a disabled item, the way Tab does', () => {
    render(<Menu open extras />)

    fireEvent.keyDown(item('One'), { key: 'ArrowDown' })

    expect(item('Three')).toHaveFocus()
  })

  it('leaves the focus where it is when the surface already holds it', () => {
    render(<Combobox />)

    expect(screen.getByLabelText('Filter')).toHaveFocus()
  })

  /* In a text box Home and End are the caret's, which is why the combobox's field can sit inside
     the surface the arrows walk — ↓ is what reaches its list. */
  it('yields Home and End to a caret and keeps the arrows in a one-line field', () => {
    render(<Combobox />)
    const field = screen.getByLabelText('Filter')

    expect(fireEvent.keyDown(field, { key: 'Home' })).toBe(true)
    expect(fireEvent.keyDown(field, { key: 'End' })).toBe(true)
    expect(field).toHaveFocus()

    fireEvent.keyDown(field, { key: 'ArrowDown' })
    expect(screen.getByRole('button', { name: 'Ada' })).toHaveFocus()
  })

  /* A textarea and a contenteditable have lines of their own for the vertical arrows to move
     between, so those go to the caret as well. */
  it('yields the vertical arrows to a multi-line box too', () => {
    render(<Prose />)
    const body = screen.getByLabelText('Body')
    const rich = screen.getByLabelText('Rich')

    body.focus()
    expect(fireEvent.keyDown(body, { key: 'ArrowDown' })).toBe(true)
    expect(fireEvent.keyDown(body, { key: 'End' })).toBe(true)
    expect(body).toHaveFocus()

    rich.focus()
    expect(fireEvent.keyDown(rich, { key: 'ArrowUp' })).toBe(true)
    expect(rich).toHaveFocus()
  })
})
