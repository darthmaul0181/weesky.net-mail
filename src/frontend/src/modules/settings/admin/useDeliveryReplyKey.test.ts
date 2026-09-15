import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { renderHook, waitFor } from '@testing-library/react'
import { createElement, type ReactNode } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { api } from '../../../api.js'
import { useDeliveryReplyKey, useGenerateDeliveryKey, useSetDeliveryReplies } from './useDeliveryReplyKey'

vi.mock('../../../api.js', () => ({ api: {
  adminGetDeliveryReplyKey: vi.fn(), adminGenerateDeliveryReplyKey: vi.fn(),
  adminSetDeliveryReplies: vi.fn(), adminDeleteDeliveryReplyKey: vi.fn(),
}, ApiError: class extends Error {} }))

function wrapper() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return { client, Wrapper: ({ children }: { children: ReactNode }) => createElement(QueryClientProvider, { client }, children) }
}

describe('useDeliveryReplyKey', () => {
  // The first test asserts `toHaveBeenCalledTimes(2)`, which is only true starting from a clean
  // mock call history — order-dependent otherwise if a case runs before it.
  beforeEach(() => { vi.clearAllMocks() })

  it('reads the state and refreshes it after a write, even a refused one', async () => {
    vi.mocked(api.adminGetDeliveryReplyKey).mockResolvedValue({ configured: false, enabled: false })
    vi.mocked(api.adminSetDeliveryReplies).mockRejectedValue(new Error('409'))
    const { Wrapper } = wrapper()
    const { result } = renderHook(() => ({ q: useDeliveryReplyKey(), set: useSetDeliveryReplies() }), { wrapper: Wrapper })
    await waitFor(() => expect(result.current.q.data).toEqual({ configured: false, enabled: false }))

    await expect(result.current.set.mutateAsync(true)).rejects.toThrow()
    await waitFor(() => expect(api.adminGetDeliveryReplyKey).toHaveBeenCalledTimes(2))
    expect(api.adminSetDeliveryReplies).toHaveBeenCalledWith({ enabled: true })
  })

  it('hands the generated key back and never caches it', async () => {
    vi.mocked(api.adminGetDeliveryReplyKey).mockResolvedValue({ configured: true, enabled: false })
    vi.mocked(api.adminGenerateDeliveryReplyKey).mockResolvedValue({ key: 'k' })
    const { Wrapper, client } = wrapper()
    const { result, unmount } = renderHook(() => useGenerateDeliveryKey(), { wrapper: Wrapper })
    await expect(result.current.mutateAsync()).resolves.toEqual({ key: 'k' })
    // gcTime: 0 only frees a mutation once nothing observes it — the hook itself is an observer
    // while mounted, exactly the precedent SchedulingAccountSection's own test waits on (the
    // dialog closing) rather than on the mutation settling. Removal itself runs off a 0ms timer.
    unmount()
    await waitFor(() => expect(client.getMutationCache().getAll()).toHaveLength(0))
  })
})
