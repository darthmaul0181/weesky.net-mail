import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { useLongPress } from './useLongPress'

beforeEach(() => vi.useFakeTimers())
afterEach(() => vi.useRealTimers())

// A primary finger unless a case says otherwise: that is the only press the hook answers to,
// so it is the only sensible default for a test that is about something else.
const at = (x: number, y: number, over: Partial<React.PointerEvent> = {}) =>
  ({ clientX: x, clientY: y, pointerType: 'touch', isPrimary: true, button: 0, ...over }
  ) as React.PointerEvent

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

  // The two tests above only bracket the threshold to (5, 30]. These pin it: 10px is jitter,
  // anything past it is a scroll, so the constant cannot drift without a test saying so.
  it('holds the travel threshold at 10px exactly', () => {
    const fired = vi.fn()
    const { result } = renderHook(() => useLongPress(fired, 500))
    act(() => { result.current.onPointerDown(at(0, 0)) })
    act(() => { result.current.onPointerMove(at(6, 8)) })  // hypot 10 — still a held finger
    act(() => { vi.advanceTimersByTime(500) })
    expect(fired).toHaveBeenCalledTimes(1)
  })

  it('cancels one pixel past the threshold', () => {
    const fired = vi.fn()
    const { result } = renderHook(() => useLongPress(fired, 500))
    act(() => { result.current.onPointerDown(at(0, 0)) })
    act(() => { result.current.onPointerMove(at(0, 11)) })
    act(() => { vi.advanceTimersByTime(500) })
    expect(fired).not.toHaveBeenCalled()
  })

  // The browser taking the gesture over — the list starts scrolling, or a system UI opens. On a
  // phone this is the cancel that fires most, and it had no test at all.
  it('does not fire once the browser takes the gesture', () => {
    const fired = vi.fn()
    const { result } = renderHook(() => useLongPress(fired, 500))
    act(() => { result.current.onPointerDown(at(0, 0)) })
    act(() => { result.current.onPointerCancel() })
    act(() => { vi.advanceTimersByTime(500) })
    expect(fired).not.toHaveBeenCalled()
  })

  // Without the cleanup effect a row unmounted mid-press — a folder change, a bulk move — still
  // calls back into a component that is gone. Every other test passes with the effect deleted.
  it('does not fire after the row unmounts', () => {
    const fired = vi.fn()
    const { result, unmount } = renderHook(() => useLongPress(fired, 500))
    act(() => { result.current.onPointerDown(at(0, 0)) })
    unmount()
    act(() => { vi.advanceTimersByTime(500) })
    expect(fired).not.toHaveBeenCalled()
  })

  // A second press restarts the clock from its own origin rather than inheriting the first's
  // remaining time — otherwise a quick tap followed by a hold fires early, at the wrong place.
  it('a second press restarts the delay', () => {
    const fired = vi.fn()
    const { result } = renderHook(() => useLongPress(fired, 500))
    act(() => { result.current.onPointerDown(at(0, 0)) })
    act(() => { vi.advanceTimersByTime(300) })
    act(() => { result.current.onPointerDown(at(100, 100)) })
    act(() => { vi.advanceTimersByTime(300) })
    expect(fired).not.toHaveBeenCalled()
    act(() => { vi.advanceTimersByTime(200) })
    expect(fired).toHaveBeenCalledTimes(1)
  })

  describe('answers to a finger, not to a mouse', () => {
    // A mouse press held still is nobody's gesture: the desktop row already opens on click and
    // selects by checkbox, and firing here both stole that click and, mid-drag, silently widened
    // what the drag carried from one row to the whole selection.
    const ignored: [string, Partial<React.PointerEvent>][] = [
      ['a mouse press', { pointerType: 'mouse' }],
      ['a secondary finger', { isPrimary: false }],
      ['a non-primary button', { button: 2 }],
    ]
    it.each(ignored)('ignores %s', (_name, over) => {
      const fired = vi.fn()
      const { result } = renderHook(() => useLongPress(fired, 500))
      act(() => { result.current.onPointerDown(at(0, 0, over)) })
      act(() => { vi.advanceTimersByTime(500) })
      expect(fired).not.toHaveBeenCalled()
    })

    it('answers a pen', () => {
      const fired = vi.fn()
      const { result } = renderHook(() => useLongPress(fired, 500))
      act(() => { result.current.onPointerDown(at(0, 0, { pointerType: 'pen' })) })
      act(() => { vi.advanceTimersByTime(500) })
      expect(fired).toHaveBeenCalledTimes(1)
    })

    // The guard returns early, so it must cancel before it does: a mouse press landing during a
    // live touch timer would otherwise leave that timer to fire under the mouse.
    it('a mouse press ends a finger press already running', () => {
      const fired = vi.fn()
      const { result } = renderHook(() => useLongPress(fired, 500))
      act(() => { result.current.onPointerDown(at(0, 0)) })
      act(() => { result.current.onPointerDown(at(0, 0, { pointerType: 'mouse' })) })
      act(() => { vi.advanceTimersByTime(500) })
      expect(fired).not.toHaveBeenCalled()
    })
  })
})
