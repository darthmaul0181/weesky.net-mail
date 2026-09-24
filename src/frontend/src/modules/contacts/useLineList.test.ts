import { describe, expect, it } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { useLineList } from './useLineList'

interface Line { value: string; kind: string }

const blank = (): Line => ({ value: '', kind: '' })

function setup(initial: Line[], keepOne = false, max = 3) {
  return renderHook(() => useLineList(() => initial, { blank, keepOne, max }))
}

describe('useLineList', () => {
  it('patches the one line it is told to, and leaves the others as they were', () => {
    const { result } = setup([{ value: 'a', kind: 'home' }, { value: 'b', kind: 'work' }])
    const second = result.current.lines[1]

    act(() => result.current.update(0, { value: 'z' }))

    expect(result.current.lines).toEqual([{ value: 'z', kind: 'home' }, { value: 'b', kind: 'work' }])
    expect(result.current.lines[1]).toBe(second)
  })

  it('reaches zero lines when it need not keep one', () => {
    const { result } = setup([{ value: 'a', kind: '' }])

    act(() => result.current.remove(0))

    expect(result.current.lines).toEqual([])
  })

  it('leaves one blank line in place of the last one removed when it must keep one', () => {
    const { result } = setup([{ value: 'a', kind: '' }, { value: 'b', kind: '' }], true)

    act(() => result.current.remove(0))
    expect(result.current.lines).toEqual([{ value: 'b', kind: '' }])

    act(() => result.current.remove(0))
    expect(result.current.lines).toEqual([blank()])
  })

  it('adds a blank line, and stops offering one at the cap', () => {
    const { result } = setup([{ value: 'a', kind: '' }], false, 2)
    expect(result.current.canAdd).toBe(true)

    act(() => result.current.add())

    expect(result.current.lines).toEqual([{ value: 'a', kind: '' }, blank()])
    expect(result.current.canAdd).toBe(false)

    act(() => result.current.add())
    expect(result.current.lines).toHaveLength(2)
  })

  it('replaces the whole list', () => {
    const { result } = setup([{ value: 'a', kind: '' }])

    act(() => result.current.replace([{ value: 'x', kind: 'cell' }, { value: 'y', kind: '' }]))

    expect(result.current.lines).toEqual([{ value: 'x', kind: 'cell' }, { value: 'y', kind: '' }])
  })

  it('reads its initial lines once', () => {
    let calls = 0
    const { rerender } = renderHook(() =>
      useLineList(() => { calls += 1; return [blank()] }, { blank, keepOne: false, max: 3 }))

    rerender()

    expect(calls).toBe(1)
  })
})
