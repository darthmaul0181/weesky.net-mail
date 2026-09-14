import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useRef, type RefObject } from 'react'
import { useDialogFocusTrap } from './useDialogFocusTrap'

function Fixture({ withInitialFocus = false, active = true }: { withInitialFocus?: boolean; active?: boolean }) {
  const containerRef = useRef<HTMLDivElement>(null)
  const secondRef = useRef<HTMLButtonElement>(null)
  useDialogFocusTrap(containerRef, { active, initialFocusRef: withInitialFocus ? secondRef : undefined })
  return (
    <div>
      <button type="button">outside</button>
      <div ref={containerRef}>
        <button type="button">first</button>
        <button type="button" ref={secondRef}>second</button>
        <button type="button">third</button>
      </div>
    </div>
  )
}

describe('useDialogFocusTrap', () => {
  it('focuses the first focusable element in the container on mount', () => {
    render(<Fixture />)
    expect(screen.getByRole('button', { name: 'first' })).toHaveFocus()
  })

  it('focuses the named element instead when one is given', () => {
    render(<Fixture withInitialFocus />)
    expect(screen.getByRole('button', { name: 'second' })).toHaveFocus()
  })

  it('does nothing while inactive', async () => {
    render(<Fixture active={false} />)
    expect(screen.getByRole('button', { name: 'first' })).not.toHaveFocus()

    screen.getByRole('button', { name: 'third' }).focus()
    await userEvent.tab()

    expect(screen.getByRole('button', { name: 'first' })).not.toHaveFocus()
  })

  it('wraps Shift+Tab from the first item to the last', async () => {
    render(<Fixture />)
    screen.getByRole('button', { name: 'first' }).focus()

    await userEvent.tab({ shift: true })

    expect(screen.getByRole('button', { name: 'third' })).toHaveFocus()
  })

  it('wraps Tab from the last item to the first', async () => {
    render(<Fixture />)
    screen.getByRole('button', { name: 'third' }).focus()

    await userEvent.tab()

    expect(screen.getByRole('button', { name: 'first' })).toHaveFocus()
  })

  it('never lets Tab reach an element outside the container', async () => {
    render(<Fixture />)
    screen.getByRole('button', { name: 'third' }).focus()

    await userEvent.tab()
    await userEvent.tab()

    expect(screen.getByRole('button', { name: 'outside' })).not.toHaveFocus()
  })

  it('brings a focus that fell outside the container back to its first item on Tab', async () => {
    render(<Fixture />)
    ;(document.activeElement as HTMLElement).blur()

    await userEvent.tab()

    expect(screen.getByRole('button', { name: 'first' })).toHaveFocus()
  })

  it('returns focus to whatever was focused before mount, once unmounted', () => {
    function Wrapper({ show }: { show: boolean }) {
      return (
        <div>
          <button type="button">trigger</button>
          {show && <Fixture />}
        </div>
      )
    }
    const { rerender } = render(<Wrapper show={false} />)
    const trigger = screen.getByRole('button', { name: 'trigger' })
    trigger.focus()
    expect(trigger).toHaveFocus()

    rerender(<Wrapper show />)
    expect(trigger).not.toHaveFocus()

    rerender(<Wrapper show={false} />)
    expect(trigger).toHaveFocus()
  })

  it('returns focus to the fallback when the element that opened it is gone', () => {
    function Wrapper({ show, withTrigger }: { show: boolean; withTrigger: boolean }) {
      const fallbackRef = useRef<HTMLHeadingElement>(null)
      const containerRef = useRef<HTMLDivElement>(null)
      return (
        <div>
          <h2 tabIndex={-1} ref={fallbackRef}>heading</h2>
          {withTrigger && <button type="button">trigger</button>}
          {show && <Trap containerRef={containerRef} fallbackRef={fallbackRef} />}
        </div>
      )
    }
    function Trap({ containerRef, fallbackRef }: {
      containerRef: RefObject<HTMLDivElement>; fallbackRef: RefObject<HTMLHeadingElement>
    }) {
      useDialogFocusTrap(containerRef, { fallbackFocusRef: fallbackRef })
      return <div ref={containerRef}><button type="button">inside</button></div>
    }
    const { rerender } = render(<Wrapper show={false} withTrigger />)
    screen.getByRole('button', { name: 'trigger' }).focus()
    rerender(<Wrapper show withTrigger />)
    rerender(<Wrapper show withTrigger={false} />)

    rerender(<Wrapper show={false} withTrigger={false} />)

    expect(screen.getByRole('heading', { name: 'heading' })).toHaveFocus()
  })
})
