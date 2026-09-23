import { describe, it, expect } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import { useLayoutEffect } from 'react'
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

  // Contacts are GUIDs: the hook can no longer be tied to the mail's uids.
  it('holds string keys as readily as numeric ones', () => {
    const { result } = renderHook(() => useSelection<string>('all'))

    act(() => result.current.toggle('a4f1-c2', 0))

    expect(result.current.has('a4f1-c2')).toBe(true)
    expect(result.current.has('other')).toBe(false)
  })

  it('range-selects over string keys', () => {
    const keys = ['a', 'b', 'c', 'd']
    const { result } = renderHook(() => useSelection<string>('all'))

    act(() => result.current.toggle('a', 0))
    act(() => result.current.toggleRange(keys, 2))

    expect([...result.current.selected].sort()).toEqual(['a', 'b', 'c'])
  })

  // The thread row's checkbox: every member on or off in one call, whatever their mix was.
  it('setMany adds or removes a batch without touching the rest', () => {
    const { result } = renderHook(() => useSelection('INBOX::0'))
    act(() => result.current.toggle(50, 4))
    act(() => result.current.setMany([10, 20], true))
    expect([...result.current.selected].sort((a, b) => a - b)).toEqual([10, 20, 50])
    act(() => result.current.setMany([10, 20], false))
    expect([...result.current.selected]).toEqual([50])
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

  it('never commits the previous resetKey\'s ticks', () => {
    let key = 'INBOX::0'
    const committed: number[] = []
    const { result, rerender } = renderHook(() => {
      const selection = useSelection(key)
      useLayoutEffect(() => { committed.push(selection.selected.size) })
      return selection
    })
    act(() => result.current.toggle(20, 1))
    committed.length = 0
    key = 'Archive::0'
    rerender()
    expect(committed).toEqual([0])
  })

  it('forgets the range anchor when the resetKey changes', () => {
    let key = 'INBOX::0'
    const { result, rerender } = renderHook(() => useSelection(key))
    act(() => result.current.toggle(40, 3))
    key = 'INBOX::1'
    rerender()
    act(() => result.current.toggleRange(LOADED, 1))
    expect([...result.current.selected]).toEqual([20])
  })

  it('forgets the range anchor across a round trip back to the same resetKey', () => {
    let key = 'INBOX::0'
    const { result, rerender } = renderHook(() => useSelection(key))
    act(() => result.current.toggle(40, 3))
    key = 'Archive::0'
    rerender()
    key = 'INBOX::0'
    rerender()
    act(() => result.current.toggleRange(LOADED, 1))
    expect([...result.current.selected]).toEqual([20])
  })
})
