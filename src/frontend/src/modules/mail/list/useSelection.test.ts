import { describe, it, expect } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import { useSelection } from './useSelection'

const LOADED = [10, 20, 30, 40, 50]

describe('useSelection', () => {
  it('toggles a uid on and off', () => {
    const { result } = renderHook(() => useSelection('INBOX::0'))
    act(() => result.current.toggle(20, 1))
    expect(result.current.has(20)).toBe(true)
    act(() => result.current.toggle(20, 1))
    expect(result.current.has(20)).toBe(false)
  })

  it('selects the inclusive range from the last toggled anchor', () => {
    const { result } = renderHook(() => useSelection('INBOX::0'))
    act(() => result.current.toggle(20, 1))          // anchor at index 1
    act(() => result.current.toggleRange(LOADED, 3)) // 1..3 → 20,30,40
    expect([...result.current.selected].sort((a, b) => a - b)).toEqual([20, 30, 40])
  })

  it('ranges upward too (anchor after the target)', () => {
    const { result } = renderHook(() => useSelection('INBOX::0'))
    act(() => result.current.toggle(40, 3))
    act(() => result.current.toggleRange(LOADED, 1)) // 3..1 → 20,30,40
    expect([...result.current.selected].sort((a, b) => a - b)).toEqual([20, 30, 40])
  })

  it('selectAll takes every loaded uid; clear empties it', () => {
    const { result } = renderHook(() => useSelection('INBOX::0'))
    act(() => result.current.selectAll(LOADED))
    expect(result.current.selected.size).toBe(5)
    act(() => result.current.clear())
    expect(result.current.selected.size).toBe(0)
  })

  it('clears when the resetKey changes (folder or page)', () => {
    let key = 'INBOX::0'
    const { result, rerender } = renderHook(() => useSelection(key))
    act(() => result.current.selectAll(LOADED))
    expect(result.current.selected.size).toBe(5)
    key = 'INBOX::1'
    rerender()
    expect(result.current.selected.size).toBe(0)
  })
})
