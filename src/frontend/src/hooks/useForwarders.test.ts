import { describe, expect, it, vi } from 'vitest'
import { renderHook } from '@testing-library/react'
import { useForwarders } from './useForwarders'

describe('useForwarders', () => {
  it('answers one object, and one function per key, however often the handlers are rebuilt', () => {
    const { result, rerender } = renderHook(() => useForwarders({ open: () => {} }))
    const first = result.current

    rerender()

    expect(result.current).toBe(first)
    expect(result.current.open).toBe(first.open)
  })

  /* The whole reason the idiom exists: the forwarder is built once, so it has to read the
     handlers of the render it is called in rather than the closures it was born with. */
  it('calls the latest handlers, never the ones captured at mount', () => {
    const atMount = vi.fn()
    const later = vi.fn()
    const { result, rerender } = renderHook(
      ({ open }: { open: () => void }) => useForwarders({ open }),
      { initialProps: { open: atMount } },
    )

    rerender({ open: later })
    result.current.open()

    expect(atMount).not.toHaveBeenCalled()
    expect(later).toHaveBeenCalledTimes(1)
  })

  it('passes the arguments through and hands the answer back', () => {
    const { result } = renderHook(
      () => useForwarders({ join: (left: string, right: number) => `${left}${right}` }))

    expect(result.current.join('x', 2)).toBe('x2')
  })

  it('forwards every key and nothing else', () => {
    const { result } = renderHook(() => useForwarders({ open: () => {}, close: () => {} }))

    expect(Object.keys(result.current)).toEqual(['open', 'close'])
  })
})
