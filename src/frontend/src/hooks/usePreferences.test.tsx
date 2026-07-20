import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import {
  PREFERENCE_KEYS, pageSizeOf, showPreviewOf, usePreferences, useSetPreference,
} from './usePreferences'

const mocks = vi.hoisted(() => ({ getPreferences: vi.fn(), setPreference: vi.fn() }))
vi.mock('../api.js', () => ({ api: mocks }))

let client: QueryClient
function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

describe('usePreferences', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  })

  it('reads the map the backend answers', async () => {
    mocks.getPreferences.mockResolvedValue({ [PREFERENCE_KEYS.pageSize]: '50' })

    const { result } = renderHook(() => usePreferences(), { wrapper })

    await waitFor(() => expect(result.current.data).toBeDefined())
    expect(pageSizeOf(result.current.data!)).toBe(50)
  })

  // Every cached message page was computed under the old size, so it is not merely stale — it
  // is the wrong shape. Both caches have to go.
  it('drops the cached message pages when a preference changes', async () => {
    mocks.setPreference.mockResolvedValue(undefined)
    const invalidate = vi.spyOn(client, 'invalidateQueries')

    const { result } = renderHook(() => useSetPreference(), { wrapper })
    result.current.mutate({ key: PREFERENCE_KEYS.pageSize, value: '10' })

    await waitFor(() => expect(mocks.setPreference).toHaveBeenCalledWith(PREFERENCE_KEYS.pageSize, '10'))
    await waitFor(() =>
      expect(invalidate).toHaveBeenCalledWith({ queryKey: ['mail'] }))
  })

  it('leaves the caches alone when the write fails', async () => {
    mocks.setPreference.mockRejectedValue(new Error('nope'))
    const invalidate = vi.spyOn(client, 'invalidateQueries')

    const { result } = renderHook(() => useSetPreference(), { wrapper })
    result.current.mutate({ key: PREFERENCE_KEYS.pageSize, value: '10' })

    await waitFor(() => expect(result.current.isError).toBe(true))
    expect(invalidate).not.toHaveBeenCalled()
  })
})

describe('the accessors', () => {
  it('reads the page size as a number', () => {
    expect(pageSizeOf({ [PREFERENCE_KEYS.pageSize]: '100' })).toBe(100)
  })

  // Anything but the explicit "false" means shown: a key the backend has not sent yet must not
  // silently hide the preview.
  it.each([
    ['true', true],
    ['false', false],
    [undefined, true],
  ])('reads showPreview %s as %s', (stored, expected) => {
    const preferences: Record<string, string> =
      stored === undefined ? {} : { [PREFERENCE_KEYS.showPreview]: stored }

    expect(showPreviewOf(preferences)).toBe(expected)
  })
})
