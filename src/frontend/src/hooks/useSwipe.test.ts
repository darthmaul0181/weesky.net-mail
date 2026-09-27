import { describe, expect, it, vi } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { useSwipe, type SwipeSide } from './useSwipe'

const at = (x: number, y = 0, over: Partial<React.PointerEvent> = {}) =>
  ({ clientX: x, clientY: y, pointerType: 'touch', isPrimary: true, button: 0, pointerId: 1, ...over }
  ) as React.PointerEvent

function setup(canSwipe: (side: SwipeSide) => boolean = () => true, outcome: 'leave' | 'return' = 'return') {
  const onCommit = vi.fn(() => outcome)
  const hook = renderHook(() => useSwipe<HTMLDivElement>(canSwipe, onCommit))
  const el = document.createElement('div')
  Object.defineProperty(el, 'clientWidth', { value: 300 })
  hook.result.current.ref.current = el
  const drag = (...points: [number, number?][]) => act(() => {
    hook.result.current.handlers.onPointerDown(at(100, 0))
    for (const [x, y] of points) hook.result.current.handlers.onPointerMove(at(x, y ?? 0))
  })
  const lift = () => act(() => { hook.result.current.handlers.onPointerUp() })
  return { hook, el, onCommit, drag, lift }
}

describe('useSwipe', () => {
  it('follows a horizontal finger without arming short of the threshold', () => {
    const { hook, el, drag } = setup()
    drag([140])
    expect(el.style.transform).toBe('translateX(40px)')
    expect(hook.result.current.state).toEqual({ side: 'right', armed: false })
  })

  it('arms past 35% of the width', () => {
    const { hook, drag } = setup()
    drag([206])
    expect(hook.result.current.state).toEqual({ side: 'right', armed: true })
  })

  it('commits an armed release and slides out on leave', () => {
    const { el, onCommit, drag, lift } = setup(() => true, 'leave')
    drag([50], [-20])
    lift()
    expect(onCommit).toHaveBeenCalledWith('left')
    expect(el.style.transform).toBe('translateX(-300px)')
  })

  it('snaps back on return', () => {
    const { hook, el, drag, lift } = setup(() => true, 'return')
    drag([210])
    lift()
    expect(el.style.transform).toBe('')
    expect(hook.result.current.state).toEqual({ side: null, armed: false })
  })

  it('does nothing on a release short of the threshold', () => {
    const { el, onCommit, drag, lift } = setup()
    drag([150])
    lift()
    expect(onCommit).not.toHaveBeenCalled()
    expect(el.style.transform).toBe('')
  })

  it('a scroll that drifts sideways stays a scroll', () => {
    const { hook, el, onCommit, drag, lift } = setup()
    drag([100, 30], [220, 40])
    lift()
    expect(el.style.transform).toBe('')
    expect(hook.result.current.state.side).toBeNull()
    expect(onCommit).not.toHaveBeenCalled()
  })

  it('never moves for a mouse', () => {
    const { hook, el } = setup()
    act(() => {
      hook.result.current.handlers.onPointerDown(at(100, 0, { pointerType: 'mouse' }))
      hook.result.current.handlers.onPointerMove(at(250, 0, { pointerType: 'mouse' }))
    })
    expect(el.style.transform).toBe('')
  })

  it('does not start toward a side that is off, and stops at rest crossing into one', () => {
    const { hook, el, drag } = setup(side => side === 'right')
    drag([40])
    expect(el.style.transform).toBe('')
    drag([150], [60])
    expect(el.style.transform).toBe('')
    expect(hook.result.current.state.side).toBeNull()
  })

  it('snaps back on pointercancel', () => {
    const { hook, el, onCommit, drag } = setup()
    drag([220])
    act(() => { hook.result.current.handlers.onPointerCancel() })
    expect(el.style.transform).toBe('')
    expect(onCommit).not.toHaveBeenCalled()
  })

  it('swallows the click that ends a drag, and only that one', () => {
    const { hook, drag, lift } = setup()
    drag([140])
    lift()
    expect(hook.result.current.swallowClick()).toBe(true)
    expect(hook.result.current.swallowClick()).toBe(false)
    act(() => { hook.result.current.handlers.onPointerDown(at(100)); hook.result.current.handlers.onPointerUp() })
    expect(hook.result.current.swallowClick()).toBe(false)
  })

  it('a second finger mid-drag puts the row back at rest', () => {
    const { hook, el, onCommit, drag, lift } = setup()
    drag([220])
    act(() => { hook.result.current.handlers.onPointerDown(at(50, 0, { isPrimary: false, pointerId: 2 })) })
    lift()
    expect(el.style.transform).toBe('')
    expect(hook.result.current.state).toEqual({ side: null, armed: false })
    expect(onCommit).not.toHaveBeenCalled()
  })
})
