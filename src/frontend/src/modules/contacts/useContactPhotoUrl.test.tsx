import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { renderHook } from '@testing-library/react'
import { useLayoutEffect, type ReactNode } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { contactKeys } from './queries'
import { useContactPhotoUrl } from './useContactPhotoUrl'

vi.mock('../../hooks/useAccountId', () => ({ useAccountId: () => 'acc' }))

beforeEach(() => {
  // jsdom does not implement the object URL API.
  URL.createObjectURL = vi.fn(() => 'blob:photo')
  URL.revokeObjectURL = vi.fn()
})

function withCache(seed: (client: QueryClient) => void) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, enabled: false } } })
  seed(client)
  function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={client}>{children}</QueryClientProvider>
  }
  return Wrapper
}

describe('useContactPhotoUrl', () => {
  it('serves the blob cached under the card it was read at', () => {
    const wrapper = withCache(c =>
      c.setQueryData(contactKeys.photo('acc', 'c1', 'h1'), new Blob(['x'])))

    const { result } = renderHook(() => useContactPhotoUrl('c1', true, 'h1'), { wrapper })

    expect(result.current).toBe('blob:photo')
  })

  // Removal and replacement are the same fact: the card changed, so the key did too, so there is
  // nothing stale left to serve.
  it('does not serve it under the next card hash', () => {
    const wrapper = withCache(c =>
      c.setQueryData(contactKeys.photo('acc', 'c1', 'h1'), new Blob(['x'])))

    const { result } = renderHook(() => useContactPhotoUrl('c1', false, 'h2'), { wrapper })

    expect(result.current).toBeNull()
  })

  it('never commits a picture once its blob is gone', () => {
    let made = 0
    URL.createObjectURL = vi.fn(() => `blob:photo-${++made}`)
    const wrapper = withCache(c => {
      c.setQueryData(contactKeys.photo('acc', 'c1', 'h1'), new Blob(['x']))
      c.setQueryData(contactKeys.photo('acc', 'c1', 'h2'), new Blob(['y']))
    })
    const committed: (string | null)[] = []
    const { rerender } = renderHook(({ hash, has }) => {
      const url = useContactPhotoUrl('c1', has, hash)
      useLayoutEffect(() => { committed.push(url) })
      return url
    }, { wrapper, initialProps: { hash: 'h1', has: true } })
    expect(committed[committed.length - 1]).toBe('blob:photo-1')

    committed.length = 0
    rerender({ hash: 'h2', has: true })
    expect(committed[committed.length - 1]).toBe('blob:photo-2')

    committed.length = 0
    rerender({ hash: 'h3', has: false })
    expect(committed).toEqual([null])

    // Its URL was revoked with it: the next blob must not bring it back for a frame.
    committed.length = 0
    rerender({ hash: 'h1', has: true })
    expect(committed).not.toContain('blob:photo-2')
    expect(committed[committed.length - 1]).toBe('blob:photo-3')
  })

  it('never commits the previous contact\'s face on the next card', () => {
    let made = 0
    URL.createObjectURL = vi.fn(() => `blob:photo-${++made}`)
    const wrapper = withCache(c => {
      c.setQueryData(contactKeys.photo('acc', 'c1', 'h1'), new Blob(['x']))
      c.setQueryData(contactKeys.photo('acc', 'c2', 'h2'), new Blob(['y']))
    })
    const committed: (string | null)[] = []
    const { rerender } = renderHook(({ id, hash }) => {
      const url = useContactPhotoUrl(id, true, hash)
      useLayoutEffect(() => { committed.push(url) })
      return url
    }, { wrapper, initialProps: { id: 'c1', hash: 'h1' } })
    expect(committed[committed.length - 1]).toBe('blob:photo-1')

    committed.length = 0
    rerender({ id: 'c2', hash: 'h2' })
    expect(committed).not.toContain('blob:photo-1')
    expect(committed[committed.length - 1]).toBe('blob:photo-2')
  })
})
