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
})
