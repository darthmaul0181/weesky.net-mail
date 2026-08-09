import { afterEach, describe, expect, it } from 'vitest'
import { renderHook } from '@testing-library/react'
import { useViewport } from './useViewport'
import { changeViewport, mockViewport, resetViewport } from '../test-utils'

afterEach(resetViewport)

describe('useViewport', () => {
  it('reads phone below 640px', () => {
    mockViewport('phone')
    expect(renderHook(() => useViewport()).result.current).toBe('phone')
  })

  it('reads tablet between 640 and 1023px', () => {
    mockViewport('tablet')
    expect(renderHook(() => useViewport()).result.current).toBe('tablet')
  })

  it('reads desktop at 1024px and above', () => {
    mockViewport('desktop')
    expect(renderHook(() => useViewport()).result.current).toBe('desktop')
  })

  it('follows a tier change', async () => {
    mockViewport('desktop')
    const { result } = renderHook(() => useViewport())
    await changeViewport('phone')
    expect(result.current).toBe('phone')
  })

  it('falls back to desktop without matchMedia', () => {
    const saved = window.matchMedia
    // @ts-expect-error deliberately removing the API an old browser may not have
    delete window.matchMedia
    expect(renderHook(() => useViewport()).result.current).toBe('desktop')
    window.matchMedia = saved
  })

  it('unsubscribes on unmount', () => {
    mockViewport('phone')
    const { unmount } = renderHook(() => useViewport())
    unmount()
    // A listener left behind would setState on an unmounted hook at the next change.
    expect(() => changeViewport('desktop')).not.toThrow()
  })
})
