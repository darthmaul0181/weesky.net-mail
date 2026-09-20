import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { useRef, type ReactNode } from 'react'
import { useDismiss, returnFocus } from './useDismiss'
import { useLayer } from './useLayer'

function press(key: string, target: EventTarget = document) {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true })
  target.dispatchEvent(event)
  return event
}

/** A menu-shaped surface: a trigger inside the root, an item to hold the focus. */
function Surface({ open = true, onDismiss, closeOnScroll }: {
  open?: boolean; onDismiss: () => void; closeOnScroll?: boolean
}) {
  const root = useRef<HTMLDivElement>(null)
  const trigger = useRef<HTMLButtonElement>(null)
  useDismiss({ open, rootRef: root, onDismiss, refocusRef: trigger, closeOnScroll })
  return (
    <div ref={root}>
      <button type="button" ref={trigger}>Trigger</button>
      {open && <button type="button">Item</button>}
    </div>
  )
}

/** The chip shape: a trigger outside the surface, which toggles it rather than dismissing it. */
function Anchored({ onDismiss }: { onDismiss: () => void }) {
  const root = useRef<HTMLDivElement>(null)
  const anchor = useRef<HTMLButtonElement>(null)
  useDismiss({ open: true, rootRef: root, onDismiss, anchorRef: anchor })
  return (
    <>
      <button type="button" ref={anchor}>Chip</button>
      <div ref={root}><button type="button">Item</button></div>
    </>
  )
}

/** A trapped layer standing over the surface — the dialog a menu row opened. */
function Dialog({ onEscape, children }: { onEscape?: () => void; children?: ReactNode }) {
  const box = useRef<HTMLDivElement>(null)
  useLayer({ active: true, ref: box, onEscape })
  return <div ref={box}><button type="button">Confirm</button>{children}</div>
}

describe('useDismiss', () => {
  it('closes on a press outside the root', () => {
    const onDismiss = vi.fn()
    render(<Surface onDismiss={onDismiss} />)

    fireEvent.mouseDown(document.body)

    expect(onDismiss).toHaveBeenCalledTimes(1)
  })

  it('leaves a press inside the root alone', () => {
    const onDismiss = vi.fn()
    render(<Surface onDismiss={onDismiss} />)

    fireEvent.mouseDown(screen.getByRole('button', { name: 'Item' }))

    expect(onDismiss).not.toHaveBeenCalled()
  })

  it('closes on Escape', () => {
    const onDismiss = vi.fn()
    render(<Surface onDismiss={onDismiss} />)

    press('Escape')

    expect(onDismiss).toHaveBeenCalledTimes(1)
  })

  it('marks the Escape it spends, so a listener behind it can tell', () => {
    render(<Surface onDismiss={vi.fn()} />)

    expect(press('Escape').defaultPrevented).toBe(true)
  })

  it('registers nothing while closed', () => {
    const onDismiss = vi.fn()
    render(<Surface open={false} onDismiss={onDismiss} />)

    fireEvent.mouseDown(document.body)
    expect(press('Escape').defaultPrevented).toBe(false)

    expect(onDismiss).not.toHaveBeenCalled()
  })

  // The bug this fixes: the calendar bubble closed on a mousedown inside the confirm it had
  // itself opened, because that press is "outside" the bubble by containment alone.
  it('leaves the surface standing while something above it owns Escape and the pointer', () => {
    const onDismiss = vi.fn()
    const onDialogEscape = vi.fn()
    render(
      <>
        <Surface onDismiss={onDismiss} />
        <Dialog onEscape={onDialogEscape} />
      </>)

    press('Escape')
    fireEvent.mouseDown(screen.getByRole('button', { name: 'Confirm' }))
    fireEvent.mouseDown(document.body)

    expect(onDialogEscape).toHaveBeenCalledTimes(1)
    expect(onDismiss).not.toHaveBeenCalled()
  })

  // The regression this fixes: the composer's colour popover with the Font menu open over it —
  // two trapless layers — needed a second press on the page, the lower one having asked whether
  // it was topmost rather than whether anything above it holds a trap.
  it('closes with the trapless surface above it on one press outside', () => {
    const onDismiss = vi.fn()
    const onMenu = vi.fn()
    function Menu() {
      const root = useRef<HTMLDivElement>(null)
      useDismiss({ open: true, rootRef: root, onDismiss: onMenu })
      return <div ref={root}><button type="button">Entry</button></div>
    }
    render(<><Surface onDismiss={onDismiss} /><Menu /></>)

    fireEvent.mouseDown(document.body)

    expect(onMenu).toHaveBeenCalledTimes(1)
    expect(onDismiss).toHaveBeenCalledTimes(1)
  })

  it('answers again once the surface above it is gone', () => {
    const onDismiss = vi.fn()
    function Both({ dialog }: { dialog: boolean }) {
      return <><Surface onDismiss={onDismiss} />{dialog && <Dialog />}</>
    }
    const { rerender } = render(<Both dialog />)

    rerender(<Both dialog={false} />)
    press('Escape')

    expect(onDismiss).toHaveBeenCalledTimes(1)
  })

  it('hands focus back to the refocus target when Escape closes it from inside', () => {
    render(<Surface onDismiss={vi.fn()} />)
    screen.getByRole('button', { name: 'Item' }).focus()

    press('Escape')

    expect(screen.getByRole('button', { name: 'Trigger' })).toHaveFocus()
  })

  it('leaves focus alone when the key arrived from outside the root', () => {
    render(
      <>
        <input aria-label="Elsewhere" />
        <Surface onDismiss={vi.fn()} />
      </>)
    const elsewhere = screen.getByLabelText('Elsewhere')
    elsewhere.focus()

    press('Escape')

    expect(elsewhere).toHaveFocus()
  })

  // The press that toggles a surface off is the anchor's own; dismissing here would unmount what
  // the anchor is about to reopen, and the surface would blink out and back.
  it('leaves a press on the anchor to the anchor', () => {
    const onDismiss = vi.fn()
    render(<Anchored onDismiss={onDismiss} />)

    fireEvent.mouseDown(screen.getByRole('button', { name: 'Chip' }))
    expect(onDismiss).not.toHaveBeenCalled()

    fireEvent.mouseDown(document.body)
    expect(onDismiss).toHaveBeenCalledTimes(1)
  })

  // The surface leaves with the scroll: focus held inside it would fall to <body>, a trapless
  // layer having no container for `useLayer` to restore from.
  it('hands focus back before a scroll dismisses it', () => {
    render(<Surface onDismiss={vi.fn()} closeOnScroll />)
    screen.getByRole('button', { name: 'Item' }).focus()

    fireEvent.scroll(document.body)

    expect(screen.getByRole('button', { name: 'Trigger' })).toHaveFocus()
  })

  // A bubble pinned to a chip's rectangle is stranded by any scroller carrying it, hence capture.
  it('closes on a scroll anywhere only when asked to', () => {
    const onDismiss = vi.fn()
    const { unmount } = render(<Surface onDismiss={onDismiss} />)
    fireEvent.scroll(document.body)
    expect(onDismiss).not.toHaveBeenCalled()
    unmount()

    render(<Surface onDismiss={onDismiss} closeOnScroll />)
    fireEvent.scroll(document.body)

    expect(onDismiss).toHaveBeenCalledTimes(1)
  })

  it('drops its outside listener when it closes', () => {
    const onDismiss = vi.fn()
    const { rerender } = render(<Surface onDismiss={onDismiss} />)

    rerender(<Surface open={false} onDismiss={onDismiss} />)
    fireEvent.mouseDown(document.body)

    expect(onDismiss).not.toHaveBeenCalled()
  })
})

describe('returnFocus', () => {
  it('moves focus only when it is held inside the surface being closed', () => {
    render(
      <>
        <div data-testid="root"><button type="button">Item</button></div>
        <button type="button">Trigger</button>
        <input aria-label="Elsewhere" />
      </>)
    const root = screen.getByTestId('root')
    const trigger = screen.getByRole('button', { name: 'Trigger' })

    screen.getByLabelText('Elsewhere').focus()
    returnFocus(root, trigger)
    expect(screen.getByLabelText('Elsewhere')).toHaveFocus()

    screen.getByRole('button', { name: 'Item' }).focus()
    returnFocus(root, trigger)
    expect(trigger).toHaveFocus()
  })
})
