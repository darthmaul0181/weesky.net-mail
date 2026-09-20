import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { StrictMode, useRef, type ReactNode, type RefObject } from 'react'
import { useLayer } from './useLayer'
import { hasOpenLayer } from '../lib/layerStack'

function press(key: string, init: KeyboardEventInit = {}) {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init })
  document.dispatchEvent(event)
  return event
}

/** A trapped layer holding two buttons, named so two of them can be told apart. */
function Dialog({ name, onEscape, children }: { name: string; onEscape?: () => void; children?: ReactNode }) {
  const box = useRef<HTMLDivElement>(null)
  useLayer({ active: true, ref: box, onEscape })
  return (
    <div ref={box}>
      <button type="button">{name} one</button>
      <button type="button">{name} two</button>
      {children}
    </div>
  )
}

describe('useLayer', () => {
  it('calls the topmost layer alone on Escape', () => {
    const lower = vi.fn()
    const upper = vi.fn()
    const { unmount } = render(
      <div>
        <Dialog name="lower" onEscape={lower} />
        <Dialog name="upper" onEscape={upper} />
      </div>,
    )

    press('Escape')

    expect(upper).toHaveBeenCalledTimes(1)
    expect(lower).not.toHaveBeenCalled()
    unmount()
  })

  it('does not move a layer above another when its handler is re-created on a render', () => {
    const upper = vi.fn()
    function Stack({ tag }: { tag: string }) {
      return (
        <div>
          <Dialog name="lower" onEscape={() => tag} />
          <Dialog name="upper" onEscape={upper} />
        </div>
      )
    }
    const { rerender, unmount } = render(<Stack tag="first" />)

    rerender(<Stack tag="second" />)
    press('Escape')

    expect(upper).toHaveBeenCalledTimes(1)
    unmount()
  })

  it('cycles Tab inside the topmost container only', () => {
    const { unmount } = render(
      <div>
        <Dialog name="lower" />
        <Dialog name="upper" />
      </div>,
    )
    screen.getByRole('button', { name: 'upper two' }).focus()

    press('Tab')

    expect(screen.getByRole('button', { name: 'upper one' })).toHaveFocus()
    unmount()
  })

  it('takes no trap, and lets Tab through, when no container is given', () => {
    function Menu() {
      useLayer({ active: true, onEscape: () => {} })
      return <button type="button">trigger</button>
    }
    const { unmount } = render(<Menu />)
    screen.getByRole('button', { name: 'trigger' }).focus()

    expect(press('Tab').defaultPrevented).toBe(false)
    expect(screen.getByRole('button', { name: 'trigger' })).toHaveFocus()
    unmount()
  })

  it('moves focus in on activation and hands it back to the opener on unmount', () => {
    function Wrapper({ open }: { open: boolean }) {
      return <div><button type="button">trigger</button>{open && <Dialog name="dialog" />}</div>
    }
    const { rerender } = render(<Wrapper open={false} />)
    const trigger = screen.getByRole('button', { name: 'trigger' })
    trigger.focus()

    rerender(<Wrapper open />)
    expect(screen.getByRole('button', { name: 'dialog one' })).toHaveFocus()

    rerender(<Wrapper open={false} />)
    expect(trigger).toHaveFocus()
  })

  it('captures the opener before a child of its own autoFocuses', () => {
    function Wrapper({ open }: { open: boolean }) {
      const box = useRef<HTMLDivElement>(null)
      return (
        <div>
          <button type="button">trigger</button>
          {open && <Field box={box} />}
        </div>
      )
    }
    function Field({ box }: { box: RefObject<HTMLDivElement> }) {
      useLayer({ active: true, ref: box })
      return <div ref={box}>
        {/* eslint-disable-next-line jsx-a11y/no-autofocus -- proves the opener is captured before a field of the layer's own autoFocuses */}
        <input autoFocus aria-label="subject" />
      </div>
    }
    const { rerender } = render(<Wrapper open={false} />)
    const trigger = screen.getByRole('button', { name: 'trigger' })
    trigger.focus()

    rerender(<Wrapper open />)
    expect(screen.getByLabelText('subject')).toHaveFocus()

    rerender(<Wrapper open={false} />)
    expect(trigger).toHaveFocus()
  })

  it('hands focus to the return ref when the opener is gone', () => {
    function Wrapper({ open, withTrigger }: { open: boolean; withTrigger: boolean }) {
      const box = useRef<HTMLDivElement>(null)
      const heading = useRef<HTMLHeadingElement>(null)
      return (
        <div>
          <h2 tabIndex={-1} ref={heading}>heading</h2>
          {withTrigger && <button type="button">trigger</button>}
          {open && <Panel box={box} heading={heading} />}
        </div>
      )
    }
    function Panel({ box, heading }: {
      box: RefObject<HTMLDivElement>; heading: RefObject<HTMLHeadingElement>
    }) {
      useLayer({ active: true, ref: box, returnFocusRef: heading })
      return <div ref={box}><button type="button">inside</button></div>
    }
    const { rerender } = render(<Wrapper open={false} withTrigger />)
    screen.getByRole('button', { name: 'trigger' }).focus()
    rerender(<Wrapper open withTrigger />)
    rerender(<Wrapper open withTrigger={false} />)

    rerender(<Wrapper open={false} withTrigger={false} />)

    expect(screen.getByRole('heading', { name: 'heading' })).toHaveFocus()
  })

  it('survives StrictMode, which mounts, destroys and mounts again', () => {
    const onEscape = vi.fn()
    const trigger = document.createElement('button')
    document.body.append(trigger)
    trigger.focus()

    const { unmount } = render(<StrictMode><Dialog name="dialog" onEscape={onEscape} /></StrictMode>)
    press('Escape')
    expect(onEscape).toHaveBeenCalledTimes(1)

    unmount()
    expect(trigger).toHaveFocus()
    trigger.remove()
  })

  it('does not reach for its opener when a layer below the top one closes', () => {
    function Stack({ lower, upper }: { lower: boolean; upper: boolean }) {
      return (
        <div>
          <button type="button">trigger</button>
          {lower && <Dialog name="lower" />}
          {upper && <Dialog name="upper" />}
        </div>
      )
    }
    const { rerender, unmount } = render(<Stack lower={false} upper={false} />)
    const trigger = screen.getByRole('button', { name: 'trigger' })
    trigger.focus()
    rerender(<Stack lower upper={false} />)
    rerender(<Stack lower upper />)
    // The opener's own focus(), not the resulting activeElement: React DOM restores the selection
    // it recorded before each commit, so a misplaced restore is undone before anything can read it.
    const stolen = vi.spyOn(trigger, 'focus')

    rerender(<Stack lower={false} upper />)

    expect(stolen).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'upper one' })).toHaveFocus()
    stolen.mockRestore()
    unmount()
  })

  it('pushes no layer at all while the container ref holds nothing', () => {
    const lower = vi.fn()
    function Detached() {
      const box = useRef<HTMLDivElement>(null)
      useLayer({ active: true, ref: box })
      return <p>no container</p>
    }
    const { unmount } = render(<div><Dialog name="lower" onEscape={lower} /><Detached /></div>)

    press('Escape')

    expect(lower).toHaveBeenCalledTimes(1)
    unmount()
  })

  it('leaves the stack empty once the layer goes', () => {
    const { unmount } = render(<Dialog name="dialog" />)
    expect(hasOpenLayer()).toBe(true)

    unmount()

    expect(hasOpenLayer()).toBe(false)
  })
})
