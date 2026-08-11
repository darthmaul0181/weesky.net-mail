import { describe, it, expect, beforeEach } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { usePaneCollapsed, usePaneSize } from './usePaneSize'

describe('usePaneSize', () => {
  beforeEach(() => localStorage.clear())

  it('starts at the default when nothing is stored', () => {
    const { result } = renderHook(() => usePaneSize('mail.split.test', 380, 240))

    expect(result.current[0]).toBe(380)
  })

  it('reads a stored size back', () => {
    localStorage.setItem('mail.split.test', '412')

    const { result } = renderHook(() => usePaneSize('mail.split.test', 380, 240))

    expect(result.current[0]).toBe(412)
  })

  // localStorage outlives the code that wrote it: garbage, or a size below the floor,
  // must answer the default rather than a crushed pane.
  it.each(['garbage', '100', '-5', ''])('falls back to the default for stored %s', stored => {
    localStorage.setItem('mail.split.test', stored)

    const { result } = renderHook(() => usePaneSize('mail.split.test', 380, 240))

    expect(result.current[0]).toBe(380)
  })

  it('persists what the setter is given, floored at the minimum', () => {
    const { result } = renderHook(() => usePaneSize('mail.split.test', 380, 240))

    act(() => result.current[1](500.4))
    expect(result.current[0]).toBe(500)
    expect(localStorage.getItem('mail.split.test')).toBe('500')

    act(() => result.current[1](50))
    expect(result.current[0]).toBe(240)
    expect(localStorage.getItem('mail.split.test')).toBe('240')
  })
})

describe('usePaneCollapsed', () => {
  beforeEach(() => localStorage.clear())

  // Only the exact string folds. A pane hidden by a typo, or by a value some later build writes
  // for another purpose, is a column the user cannot find and did not ask to lose.
  it.each([undefined, 'garbage', 'TRUE', '1', ''])('stays open for stored %s', stored => {
    if (stored !== undefined) localStorage.setItem('mail.folders.test', stored)

    const { result } = renderHook(() => usePaneCollapsed('mail.folders.test'))

    expect(result.current[0]).toBe(false)
  })

  it('reads a folded pane back', () => {
    localStorage.setItem('mail.folders.test', 'true')

    const { result } = renderHook(() => usePaneCollapsed('mail.folders.test'))

    expect(result.current[0]).toBe(true)
  })

  it('persists both directions', () => {
    const { result } = renderHook(() => usePaneCollapsed('mail.folders.test'))

    act(() => result.current[1](true))
    expect(result.current[0]).toBe(true)
    expect(localStorage.getItem('mail.folders.test')).toBe('true')

    act(() => result.current[1](false))
    expect(result.current[0]).toBe(false)
    expect(localStorage.getItem('mail.folders.test')).toBe('false')
  })
})
