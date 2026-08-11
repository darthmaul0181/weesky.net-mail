import { describe, it, expect, beforeEach } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { usePaneSize } from './usePaneSize'

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
