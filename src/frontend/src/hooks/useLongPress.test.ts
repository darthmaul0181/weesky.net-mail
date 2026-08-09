import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { useLongPress } from './useLongPress'

beforeEach(() => vi.useFakeTimers())
afterEach(() => vi.useRealTimers())

const at = (x: number, y: number) => ({ clientX: x, clientY: y }) as React.PointerEvent

describe('useLongPress', () => {
  it('fires once the press outlasts the delay', () => {
    const fired = vi.fn()
    const { result } = renderHook(() => useLongPress(fired, 500))
    act(() => { result.current.onPointerDown(at(0, 0)) })
    act(() => { vi.advanceTimersByTime(500) })
    expect(fired).toHaveBeenCalledTimes(1)
  })

  it('does not fire on a tap', () => {
    const fired = vi.fn()
    const { result } = renderHook(() => useLongPress(fired, 500))
    act(() => { result.current.onPointerDown(at(0, 0)) })
    act(() => { result.current.onPointerUp() })
    act(() => { vi.advanceTimersByTime(500) })
    expect(fired).not.toHaveBeenCalled()
  })

  it('does not fire when the finger travels — that is a scroll', () => {
    const fired = vi.fn()
    const { result } = renderHook(() => useLongPress(fired, 500))
    act(() => { result.current.onPointerDown(at(0, 0)) })
    act(() => { result.current.onPointerMove(at(0, 30)) })
    act(() => { vi.advanceTimersByTime(500) })
    expect(fired).not.toHaveBeenCalled()
  })

  it('tolerates the jitter of a still finger', () => {
    const fired = vi.fn()
    const { result } = renderHook(() => useLongPress(fired, 500))
    act(() => { result.current.onPointerDown(at(0, 0)) })
    act(() => { result.current.onPointerMove(at(3, 4)) })
    act(() => { vi.advanceTimersByTime(500) })
    expect(fired).toHaveBeenCalledTimes(1)
  })
})
