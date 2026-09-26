import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useToday } from './useToday'

beforeEach(() => { vi.useFakeTimers() })
afterEach(() => {
  vi.useRealTimers()
  vi.restoreAllMocks()
})

describe('useToday', () => {
  // Tokyo's midnight is 15:00 UTC, whatever zone the machine running this is in.
  it("moves on at the calendar zone's midnight, not the machine's", () => {
    vi.setSystemTime(new Date('2026-09-14T14:59:30Z'))
    const { result } = renderHook(() => useToday('Asia/Tokyo'))
    expect(result.current).toBe('2026-09-14')

    act(() => { vi.advanceTimersByTime(29_000) })
    expect(result.current).toBe('2026-09-14')

    act(() => { vi.advanceTimersByTime(1_000) })
    expect(result.current).toBe('2026-09-15')
  })

  it('re-arms for the midnight after', () => {
    vi.setSystemTime(new Date('2026-09-14T14:59:30Z'))
    const { result } = renderHook(() => useToday('Asia/Tokyo'))

    act(() => { vi.advanceTimersByTime(30_000 + 86_400_000) })
    expect(result.current).toBe('2026-09-16')
  })

  // A machine asleep stops the timers' clock but not the wall clock: the day is caught within a
  // minute of waking, not when a timer set for last night's midnight finally runs out.
  it('catches up within a minute of waking from sleep', () => {
    vi.setSystemTime(new Date('2026-09-14T09:00:00Z'))  // 18:00 in Tokyo
    const { result } = renderHook(() => useToday('Asia/Tokyo'))

    vi.setSystemTime(new Date('2026-09-14T23:00:00Z'))  // 08:00 the next morning
    act(() => { vi.advanceTimersByTime(60_000) })
    expect(result.current).toBe('2026-09-15')
  })

  // Santiago skips from 23:59:59 on 5 September to 01:00 on the 6th: its midnight is already past
  // while the day has not turned, and that hour must not be spent polling every second.
  it('crosses a midnight the zone skips without polling it', () => {
    vi.setSystemTime(new Date('2026-09-06T03:10:00Z'))  // 23:10, 5 September
    const timeout = vi.spyOn(globalThis, 'setTimeout')
    const { result } = renderHook(() => useToday('America/Santiago'))
    expect(result.current).toBe('2026-09-05')

    act(() => { vi.advanceTimersByTime(51 * 60_000) })
    expect(result.current).toBe('2026-09-06')
    expect(timeout.mock.calls.length).toBeLessThan(60)
  })

  it('leaves no timer and no listener behind once unmounted', () => {
    vi.setSystemTime(new Date('2026-09-14T14:00:00Z'))
    const removed = vi.spyOn(document, 'removeEventListener')
    const { unmount } = renderHook(() => useToday('Asia/Tokyo'))

    unmount()

    expect(vi.getTimerCount()).toBe(0)
    expect(removed).toHaveBeenCalledWith('visibilitychange', expect.any(Function))
  })

  // A hidden tab's timers are throttled: coming back is when the day is read again.
  it('catches up when the tab comes back', () => {
    vi.setSystemTime(new Date('2026-09-14T14:00:00Z'))
    const { result } = renderHook(() => useToday('Asia/Tokyo'))

    vi.setSystemTime(new Date('2026-09-15T02:00:00Z'))
    act(() => { document.dispatchEvent(new Event('visibilitychange')) })
    expect(result.current).toBe('2026-09-15')
  })
})
