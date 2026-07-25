import { describe, it, expect, vi } from 'vitest'
import { render } from '@testing-library/react'
import LoadMoreSentinel from './LoadMoreSentinel'

// The DOM lib type has no `instances` static — that's the test double's addition, not the
// real IntersectionObserver's, so it's read through this cast instead of augmenting the lib.
const FakeIntersectionObserver = IntersectionObserver as unknown as {
  instances: { trigger: (isIntersecting?: boolean) => void }[]
}

describe('LoadMoreSentinel', () => {
  it('calls back when it comes into view', () => {
    const onReach = vi.fn()
    render(<LoadMoreSentinel onReach={onReach} />)

    FakeIntersectionObserver.instances[0].trigger(true)

    expect(onReach).toHaveBeenCalledTimes(1)
  })

  it('stays quiet while it is out of view', () => {
    const onReach = vi.fn()
    render(<LoadMoreSentinel onReach={onReach} />)

    FakeIntersectionObserver.instances[0].trigger(false)

    expect(onReach).not.toHaveBeenCalled()
  })

  // A re-render hands it a fresh closure. Rebuilding the observer each time would make it
  // fire again on the same intersection, so the callback is read through a ref instead.
  it('keeps one observer across re-renders and calls the latest callback', () => {
    const first = vi.fn()
    const second = vi.fn()
    const { rerender } = render(<LoadMoreSentinel onReach={first} />)
    const observer = FakeIntersectionObserver.instances[0]

    rerender(<LoadMoreSentinel onReach={second} />)
    expect(FakeIntersectionObserver.instances).toHaveLength(1)
    expect(FakeIntersectionObserver.instances[0]).toBe(observer)

    FakeIntersectionObserver.instances[0].trigger(true)
    expect(first).not.toHaveBeenCalled()
    expect(second).toHaveBeenCalledTimes(1)
  })

  it('disconnects when it unmounts', () => {
    const { unmount } = render(<LoadMoreSentinel onReach={vi.fn()} />)

    unmount()

    expect(FakeIntersectionObserver.instances).toHaveLength(0)
  })
})
