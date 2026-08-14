import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { ROW_EXIT_MS, useRowExit } from './useRowExit'

/** test-setup's stub answers `matches: false` to everything; this flips the one query that counts. */
function reduceMotion(on: boolean) {
  vi.mocked(window.matchMedia).mockImplementation((query: string) => ({
    matches: on && query === '(prefers-reduced-motion: reduce)',
    media: query,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
  }) as unknown as MediaQueryList)
}

describe('useRowExit', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    reduceMotion(false)
  })
  afterEach(() => vi.useRealTimers())

  it('holds the mutation back while the rows leave', () => {
    const fire = vi.fn()
    const { result } = renderHook(() => useRowExit())

    act(() => result.current.depart([7, 8], fire))

    expect(result.current.departing).toEqual(new Set([7, 8]))
    expect(fire).not.toHaveBeenCalled()

    act(() => { vi.advanceTimersByTime(ROW_EXIT_MS - 1) })
    expect(fire).not.toHaveBeenCalled()
  })

  it('fires once and releases the rows when the exit has played', () => {
    const fire = vi.fn()
    const { result } = renderHook(() => useRowExit())

    act(() => result.current.depart([7], fire))
    act(() => { vi.advanceTimersByTime(ROW_EXIT_MS) })

    expect(fire).toHaveBeenCalledTimes(1)
    expect(result.current.departing.size).toBe(0)
  })

  it('keeps two batches apart', () => {
    const first = vi.fn()
    const second = vi.fn()
    const { result } = renderHook(() => useRowExit())

    act(() => result.current.depart([7], first))
    act(() => { vi.advanceTimersByTime(120) })
    act(() => result.current.depart([9], second))

    expect(result.current.departing).toEqual(new Set([7, 9]))

    act(() => { vi.advanceTimersByTime(ROW_EXIT_MS - 120) })
    expect(first).toHaveBeenCalledTimes(1)
    expect(second).not.toHaveBeenCalled()
    expect(result.current.departing).toEqual(new Set([9]))

    act(() => { vi.advanceTimersByTime(120) })
    expect(second).toHaveBeenCalledTimes(1)
  })

  // A double click on the trash would otherwise arm the same delete twice.
  it('ignores a uid that is already leaving', () => {
    const first = vi.fn()
    const second = vi.fn()
    const { result } = renderHook(() => useRowExit())

    act(() => result.current.depart([7], first))
    act(() => result.current.depart([7], second))
    act(() => { vi.advanceTimersByTime(ROW_EXIT_MS) })

    expect(first).toHaveBeenCalledTimes(1)
    expect(second).not.toHaveBeenCalled()
  })

  // Only the uids nobody is already taking care of are the new batch's own.
  it('departs the rows of an overlapping batch that are not already leaving', () => {
    const first = vi.fn()
    const second = vi.fn()
    const { result } = renderHook(() => useRowExit())

    act(() => result.current.depart([7, 8], first))
    act(() => result.current.depart([8, 9], second))

    expect(result.current.departing).toEqual(new Set([7, 8, 9]))

    act(() => { vi.advanceTimersByTime(ROW_EXIT_MS) })
    expect(first).toHaveBeenCalledTimes(1)
    expect(second).toHaveBeenCalledTimes(1)
    expect(result.current.departing.size).toBe(0)
  })

  // The user asked for the deletion; leaving the module must not swallow it.
  it('fires pending departures on unmount instead of dropping them', () => {
    const fire = vi.fn()
    const { result, unmount } = renderHook(() => useRowExit())

    act(() => result.current.depart([7], fire))
    unmount()

    expect(fire).toHaveBeenCalledTimes(1)

    act(() => { vi.advanceTimersByTime(ROW_EXIT_MS) })
    expect(fire).toHaveBeenCalledTimes(1)  // The timer was cleared, not left to fire a second time.
  })

  it('deletes straight away under prefers-reduced-motion', () => {
    reduceMotion(true)
    const fire = vi.fn()
    const { result } = renderHook(() => useRowExit())

    act(() => result.current.depart([7], fire))

    expect(fire).toHaveBeenCalledTimes(1)
    expect(result.current.departing.size).toBe(0)
  })
})
