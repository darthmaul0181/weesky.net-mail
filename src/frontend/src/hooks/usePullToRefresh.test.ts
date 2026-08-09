import { describe, expect, it, vi } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { createRef } from 'react'
import { usePullToRefresh } from './usePullToRefresh'

function scroller(scrollTop: number) {
  const element = document.createElement('div')
  Object.defineProperty(element, 'scrollTop', { value: scrollTop, writable: true })
  document.body.appendChild(element)
  return element
}

// jsdom has no TouchEvent constructor; a plain Event carrying a `touches` array is what the
// hook actually reads, and it dispatches through the same listeners.
function fire(element: HTMLElement, type: string, y: number) {
  const event = new Event(type, { bubbles: true, cancelable: true })
  Object.defineProperty(event, 'touches', { value: [{ clientY: y }] })
  element.dispatchEvent(event)
}

describe('usePullToRefresh', () => {
  it('refreshes once the pull passes the threshold', () => {
    const element = scroller(0)
    const ref = createRef<HTMLElement>()
    // @ts-expect-error assigning a ref in a test
    ref.current = element
    const onRefresh = vi.fn()
    renderHook(() => usePullToRefresh(ref, onRefresh))
    act(() => {
      fire(element, 'touchstart', 0)
      fire(element, 'touchmove', 100)
      fire(element, 'touchend', 100)
    })
    expect(onRefresh).toHaveBeenCalledTimes(1)
  })

  it('ignores a short pull', () => {
    const element = scroller(0)
    const ref = createRef<HTMLElement>()
    // @ts-expect-error assigning a ref in a test
    ref.current = element
    const onRefresh = vi.fn()
    renderHook(() => usePullToRefresh(ref, onRefresh))
    act(() => {
      fire(element, 'touchstart', 0)
      fire(element, 'touchmove', 20)
      fire(element, 'touchend', 20)
    })
    expect(onRefresh).not.toHaveBeenCalled()
  })

  it('ignores a pull that starts part-way down the list', () => {
    const element = scroller(400)
    const ref = createRef<HTMLElement>()
    // @ts-expect-error assigning a ref in a test
    ref.current = element
    const onRefresh = vi.fn()
    renderHook(() => usePullToRefresh(ref, onRefresh))
    act(() => {
      fire(element, 'touchstart', 0)
      fire(element, 'touchmove', 100)
      fire(element, 'touchend', 100)
    })
    // Pulling down inside a scrolled list is scrolling, not refreshing.
    expect(onRefresh).not.toHaveBeenCalled()
  })

  it('ignores a scroll up followed by a pull back down', () => {
    const element = scroller(0)
    const ref = createRef<HTMLElement>()
    // @ts-expect-error assigning a ref in a test
    ref.current = element
    const onRefresh = vi.fn()
    renderHook(() => usePullToRefresh(ref, onRefresh))
    act(() => {
      fire(element, 'touchstart', 0)
      fire(element, 'touchmove', -50) // the list scrolls up — this was never a pull
      fire(element, 'touchmove', 100) // back down past the start, but the gesture already ended
      fire(element, 'touchend', 100)
    })
    // A scroll up, then back down, is not "released past the threshold": the list was never
    // held at rest the way a real pull starts.
    expect(onRefresh).not.toHaveBeenCalled()
  })

  // The only shape that catches a listener rebind mid-gesture: MessageList's real call site is
  // `usePullToRefresh(scrollRef, () => onRefresh?.())`, a fresh arrow every render, and `setPull`
  // inside `move()` re-renders this hook's own host on every touch frame. If the listener effect
  // depends on that callback's identity, the rebind between frames drops `origin`/`travelled` and
  // the gesture goes dead mid-drag — exactly what the three tests above cannot see, since they
  // fire every event inside one `act()` with a callback whose identity never changes.
  it('keeps tracking a pull across the re-render its own setPull causes', async () => {
    const element = scroller(0)
    const ref = createRef<HTMLElement>()
    // @ts-expect-error assigning a ref in a test
    ref.current = element
    const onRefresh = vi.fn()
    renderHook(() => usePullToRefresh(ref, () => onRefresh()))
    await act(async () => { fire(element, 'touchstart', 0) })
    await act(async () => { fire(element, 'touchmove', 30) }) // re-renders via setPull
    await act(async () => { fire(element, 'touchmove', 90) })
    await act(async () => { fire(element, 'touchend', 90) })
    expect(onRefresh).toHaveBeenCalledTimes(1)
  })
})
