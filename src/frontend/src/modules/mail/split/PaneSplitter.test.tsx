import { describe, it, expect, vi } from 'vitest'
import { act, fireEvent, render, screen } from '@testing-library/react'
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

/**
 * jsdom carries no ResizeObserver at all. This fake stands in for it exactly the way
 * `installPointerEvents` stands in for a missing PointerEvent — it records the callback the
 * component passed in, so a test can fire it by hand to simulate the browser noticing a resize.
 */
let observedCallback: (() => void) | undefined

class FakeResizeObserver {
  constructor(callback: () => void) { observedCallback = callback }
  observe() {}
  unobserve() {}
  disconnect() {}
}

function fireResize() {
  act(() => observedCallback?.())
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

  it('never nudges past the parent span minus the reserve', () => {
    const { onResize, separator } = renderSplitter({ size: 670 })
    Object.defineProperty(separator.parentElement!, 'clientWidth', { value: 1000 })

    fireEvent.keyDown(separator, { key: 'ArrowRight' })

    expect(onResize).toHaveBeenLastCalledWith(680) // 1000 − reserve(320)
  })

  it('stays at the ceiling rather than crushing the other pane', () => {
    const { onResize, separator } = renderSplitter({ size: 680 })
    Object.defineProperty(separator.parentElement!, 'clientWidth', { value: 1000 })

    fireEvent.keyDown(separator, { key: 'ArrowRight' })

    expect(onResize).toHaveBeenLastCalledWith(680) // never 696
  })

  // ceilingOf reads clientHeight, not clientWidth, on this axis — the vertical cases above
  // cannot exercise that branch.
  it('never nudges past the ceiling horizontally either', () => {
    const { onResize, separator } = renderSplitter({ orientation: 'horizontal', size: 670 })
    Object.defineProperty(separator.parentElement!, 'clientHeight', { value: 1000 })

    fireEvent.keyDown(separator, { key: 'ArrowDown' })

    expect(onResize).toHaveBeenLastCalledWith(680) // 1000 − reserve(320)
  })

  it('resets to the default on double-click', () => {
    const { onResize, separator } = renderSplitter({ size: 500 })

    fireEvent.doubleClick(separator)

    expect(onResize).toHaveBeenCalledWith(380)
  })

  it('reports its current position and minimum to assistive tech', () => {
    const { separator } = renderSplitter({ size: 420, min: 240 })

    expect(separator).toHaveAttribute('aria-valuenow', '420')
    expect(separator).toHaveAttribute('aria-valuemin', '240')
  })

  it('omits aria-valuemax when the ceiling is unknown — no layout yet', () => {
    const { separator } = renderSplitter()

    expect(separator).not.toHaveAttribute('aria-valuemax')
  })

  // A ResizeObserver on the parent, not a value read during render: a window resize or a
  // sibling pane changing size does not re-render PaneSplitter for any other reason, so nothing
  // else would ever notice the ceiling moved.
  it('reports the ceiling once the parent has a real span, and picks up a later resize', () => {
    vi.stubGlobal('ResizeObserver', FakeResizeObserver)
    const { separator } = renderSplitter({ size: 380 })
    Object.defineProperty(separator.parentElement!, 'clientWidth', { value: 800, configurable: true })

    fireResize()

    expect(separator).toHaveAttribute('aria-valuemax', '480') // 800 − reserve(320)

    Object.defineProperty(separator.parentElement!, 'clientWidth', { value: 1000, configurable: true })
    fireResize()

    expect(separator).toHaveAttribute('aria-valuemax', '680') // 1000 − reserve(320)
    vi.unstubAllGlobals()
  })

  it('omits aria-valuemax again if a later resize takes the parent back to no layout', () => {
    vi.stubGlobal('ResizeObserver', FakeResizeObserver)
    const { separator } = renderSplitter({ size: 380 })
    Object.defineProperty(separator.parentElement!, 'clientWidth', { value: 800, configurable: true })
    fireResize()
    expect(separator).toHaveAttribute('aria-valuemax', '480')

    Object.defineProperty(separator.parentElement!, 'clientWidth', { value: 0, configurable: true })
    fireResize()

    expect(separator).not.toHaveAttribute('aria-valuemax')
    vi.unstubAllGlobals()
  })
})
