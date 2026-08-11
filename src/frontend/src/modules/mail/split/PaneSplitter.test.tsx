import { describe, it, expect, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import PaneSplitter from './PaneSplitter'

function renderSplitter(overrides: Partial<Parameters<typeof PaneSplitter>[0]> = {}) {
  const onResize = vi.fn()
  render(
    <div>
      <PaneSplitter
        orientation="vertical" size={380} defaultSize={380} min={240} reserve={320}
        onResize={onResize} {...overrides}
      />
    </div>,
  )
  return { onResize, separator: screen.getByRole('separator') }
}

describe('PaneSplitter', () => {
  it('is an accessible separator carrying its orientation', () => {
    const { separator } = renderSplitter({ orientation: 'horizontal' })

    expect(separator).toHaveAttribute('aria-orientation', 'horizontal')
    expect(separator).toHaveAttribute('tabindex', '0')
  })

  it('drags along its axis', () => {
    const { onResize, separator } = renderSplitter()

    fireEvent.pointerDown(separator, { clientX: 400, clientY: 10 })
    fireEvent.pointerMove(window, { clientX: 460, clientY: 10 })
    expect(onResize).toHaveBeenLastCalledWith(440)

    fireEvent.pointerUp(window)
    fireEvent.pointerMove(window, { clientX: 500, clientY: 10 })
    expect(onResize).toHaveBeenCalledTimes(1)  // released — later moves are not drags
  })

  it('never drags below the minimum', () => {
    const { onResize, separator } = renderSplitter()

    fireEvent.pointerDown(separator, { clientX: 400 })
    fireEvent.pointerMove(window, { clientX: 100 })

    expect(onResize).toHaveBeenLastCalledWith(240)
    fireEvent.pointerUp(window)
  })

  it('never drags past the parent span minus the reserve', () => {
    const { onResize, separator } = renderSplitter()
    Object.defineProperty(separator.parentElement!, 'clientWidth', { value: 800 })

    fireEvent.pointerDown(separator, { clientX: 400 })
    fireEvent.pointerMove(window, { clientX: 2000 })

    expect(onResize).toHaveBeenLastCalledWith(480) // 800 − reserve(320)
    fireEvent.pointerUp(window)
  })

  it('stops the drag on pointercancel, same as pointerup', () => {
    const { onResize, separator } = renderSplitter()

    fireEvent.pointerDown(separator, { clientX: 400 })
    fireEvent.pointerMove(window, { clientX: 420 })
    expect(onResize).toHaveBeenCalledTimes(1)

    fireEvent(window, new Event('pointercancel'))
    fireEvent.pointerMove(window, { clientX: 500 })

    expect(onResize).toHaveBeenCalledTimes(1) // cancelled — the later move is not a drag
  })

  it('nudges with the arrow keys, clamped at the minimum', () => {
    const { onResize, separator } = renderSplitter({ size: 250 })

    fireEvent.keyDown(separator, { key: 'ArrowRight' })
    expect(onResize).toHaveBeenLastCalledWith(266)

    fireEvent.keyDown(separator, { key: 'ArrowLeft' })
    expect(onResize).toHaveBeenLastCalledWith(240)  // 250 − 16 floors at min
  })

  it('nudges vertically when horizontal', () => {
    const { onResize } = renderSplitter({ orientation: 'horizontal', size: 280, min: 120 })

    fireEvent.keyDown(screen.getByRole('separator'), { key: 'ArrowDown' })
    expect(onResize).toHaveBeenLastCalledWith(296)
  })

  it('resets to the default on double-click', () => {
    const { onResize, separator } = renderSplitter({ size: 500 })

    fireEvent.doubleClick(separator)

    expect(onResize).toHaveBeenCalledWith(380)
  })

  describe('the collapse chevron', () => {
    // Both splits between the list and the reader hand in neither prop: a seam with a control that
    // folds nothing is worse than a bare one.
    it('is absent unless the caller offers something to fold', () => {
      renderSplitter()

      expect(screen.queryByRole('button')).not.toBeInTheDocument()
    })

    it('names the action to come and reports the pane state', () => {
      const onToggleCollapse = vi.fn()
      renderSplitter({ collapsed: false, onToggleCollapse })

      const chevron = screen.getByRole('button', { name: /hide the folder column/i })
      expect(chevron).toHaveAttribute('aria-expanded', 'true')

      fireEvent.click(chevron)
      expect(onToggleCollapse).toHaveBeenCalledOnce()
    })

    it('turns round once the pane is folded', () => {
      renderSplitter({ collapsed: true, onToggleCollapse: vi.fn() })

      expect(screen.getByRole('button', { name: /show the folder column/i }))
        .toHaveAttribute('aria-expanded', 'false')
      expect(screen.getByRole('separator').className).toContain('is-collapsed')
    })

    // It sits INSIDE the separator, so both gestures reach the bar unless they are stopped: a
    // click would start a drag, and a double-click would reset the width it is folding away.
    it('neither drags nor resets the pane it folds', () => {
      const { onResize } = renderSplitter({ collapsed: false, onToggleCollapse: vi.fn() })
      const chevron = screen.getByRole('button')

      fireEvent.pointerDown(chevron, { clientX: 400, clientY: 10 })
      fireEvent.pointerMove(window, { clientX: 460, clientY: 10 })
      fireEvent.pointerUp(window)
      fireEvent.doubleClick(chevron)

      expect(onResize).not.toHaveBeenCalled()
    })
  })
})
