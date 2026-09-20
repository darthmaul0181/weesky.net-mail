import { act, render, renderHook, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useToasts } from './useToasts'

/** A page that raises one toast on mount, the shape every consumer of the hook has. */
function Page({ type }) {
  const { toasts, addToast } = useToasts()
  return (
    <div>
      <button type="button" onClick={() => addToast('saved', type)}>raise</button>
      {toasts.map(t => <span key={t.id}>{t.message}</span>)}
    </div>
  )
}

describe('useToasts', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('dismisses a toast on its own after three seconds', async () => {
    render(<Page />)
    act(() => screen.getByRole('button').click())
    expect(screen.getByText('saved')).toBeInTheDocument()

    act(() => vi.advanceTimersByTime(3000))

    expect(screen.queryByText('saved')).not.toBeInTheDocument()
  })

  // The timer outlives the component that armed it: in a test it fires into a torn-down jsdom and
  // React reaches for a window that is gone, which is how it reddened a settings suite on CI.
  it('leaves no timer behind when the page unmounts', () => {
    const clear = vi.spyOn(globalThis, 'clearTimeout')
    const { unmount } = render(<Page />)
    act(() => screen.getByRole('button').click())

    unmount()

    expect(clear).toHaveBeenCalled()
    expect(vi.getTimerCount()).toBe(0)
  })

  it('leaves no timer behind when the toast is dismissed by hand', () => {
    function Dismissable() {
      const { toasts, addToast, removeToast } = useToasts()
      return (
        <div>
          <button type="button" onClick={() => addToast('saved')}>raise</button>
          {toasts.map(t => (
            <button key={t.id} type="button" onClick={() => removeToast(t.id)}>close {t.message}</button>
          ))}
        </div>
      )
    }

    render(<Dismissable />)
    act(() => screen.getByRole('button', { name: 'raise' }).click())
    act(() => screen.getByRole('button', { name: 'close saved' }).click())

    expect(vi.getTimerCount()).toBe(0)
  })

  // An error toast is never dismissed on its own, so it must not arm a timer at all.
  it('arms no timer for an error', () => {
    render(<Page type="error" />)
    act(() => screen.getByRole('button').click())

    expect(vi.getTimerCount()).toBe(0)
  })

  it('pauses the dismiss timer and resumes it with the remaining time (WCAG 2.2.1)', () => {
    const { result } = renderHook(() => useToasts())

    act(() => result.current.addToast('saved'))
    act(() => vi.advanceTimersByTime(2000))
    act(() => result.current.pauseToast(result.current.toasts[0].id))
    act(() => vi.advanceTimersByTime(5000)) // well past the original 3s budget
    expect(result.current.toasts).toHaveLength(1) // still here — paused

    act(() => result.current.resumeToast(result.current.toasts[0].id))
    act(() => vi.advanceTimersByTime(999))
    expect(result.current.toasts).toHaveLength(1) // 1s of the original 3s left, not yet
    act(() => vi.advanceTimersByTime(1))
    expect(result.current.toasts).toHaveLength(0)
  })

  it('pausing an id with no timer (an error toast, or an unknown id) is a no-op', () => {
    const { result } = renderHook(() => useToasts())

    act(() => result.current.addToast('failed', 'error'))
    const id = result.current.toasts[0].id

    expect(() => act(() => result.current.pauseToast(id))).not.toThrow()
    expect(() => act(() => result.current.resumeToast(id))).not.toThrow()
    expect(vi.getTimerCount()).toBe(0)
  })

  it('resuming a toast that was never paused is a no-op, not a second timer', () => {
    const { result } = renderHook(() => useToasts())

    act(() => result.current.addToast('saved'))
    act(() => result.current.resumeToast(result.current.toasts[0].id))

    expect(vi.getTimerCount()).toBe(1)
  })
})
