import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { useToday } from './useToday'

/* Local wall-clock components on purpose: every claim here is about the local calendar day, so
   22:00 to midnight crosses one on any host, whatever its zone. `advanceTimersByTime` moves the
   faked clock along with the timers, so it is the only thing that drives time here. */
const HOUR = 60 * 60 * 1000

beforeEach(() => {
  vi.useFakeTimers()
  vi.setSystemTime(new Date(2026, 6, 18, 22, 0))
})
afterEach(() => { vi.useRealTimers() })

describe('useToday', () => {
  it('answers the local calendar day', () => {
    expect(renderHook(() => useToday()).result.current).toBe('2026-07-18')
  })

  it('moves on at the next local midnight', () => {
    const { result } = renderHook(() => useToday())

    act(() => { vi.advanceTimersByTime(2 * HOUR) })

    expect(result.current).toBe('2026-07-19')
  })

  it('stays put before midnight', () => {
    const { result } = renderHook(() => useToday())

    act(() => { vi.advanceTimersByTime(119 * 60 * 1000) })

    expect(result.current).toBe('2026-07-18')
  })

  // It rearms rather than firing once: a mailbox is left open for days, not for one night.
  it('keeps following the day across a second midnight', () => {
    const { result } = renderHook(() => useToday())

    act(() => { vi.advanceTimersByTime(2 * HOUR) })
    act(() => { vi.advanceTimersByTime(24 * HOUR) })

    expect(result.current).toBe('2026-07-20')
    expect(vi.getTimerCount()).toBe(1)
  })

  it('drops its timer on unmount', () => {
    const { unmount } = renderHook(() => useToday())

    unmount()

    expect(vi.getTimerCount()).toBe(0)
  })
})
