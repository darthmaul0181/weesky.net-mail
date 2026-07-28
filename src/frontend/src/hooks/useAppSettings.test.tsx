import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { installableOf, useAppSettings, useSetAppSetting } from './useAppSettings'

const mocks = vi.hoisted(() => ({ getAppSettings: vi.fn(), setAppSetting: vi.fn() }))
vi.mock('../api.js', () => ({ api: mocks }))

let client: QueryClient
function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

describe('useAppSettings', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  })

  it('answers the map the backend sent', async () => {
    mocks.getAppSettings.mockResolvedValue({ 'app.installable': 'true' })

    const { result } = renderHook(() => useAppSettings(), { wrapper })

    await waitFor(() => expect(result.current.data).toEqual({ 'app.installable': 'true' }))
  })

  it('refetches the settings once a write succeeds', async () => {
    mocks.setAppSetting.mockResolvedValue(undefined)
    const invalidate = vi.spyOn(client, 'invalidateQueries')

    const { result } = renderHook(() => useSetAppSetting(), { wrapper })
    result.current.mutate({ key: 'app.name', value: 'Snoopy mail' })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['appSettings'] })
  })

  // The case that actually distinguishes onSettled from onSuccess: a refused write must still
  // resync the screen to the server's copy rather than leaving an optimistic lie on it.
  it('still refetches the settings when the write is refused', async () => {
    mocks.setAppSetting.mockRejectedValue(new Error('nope'))
    const invalidate = vi.spyOn(client, 'invalidateQueries')

    const { result } = renderHook(() => useSetAppSetting(), { wrapper })
    result.current.mutate({ key: 'app.name', value: 'Snoopy mail' })

    await waitFor(() => expect(result.current.isError).toBe(true))
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['appSettings'] })
  })
})

describe('installableOf', () => {
  // Exactly 'true': an absent or malformed value must leave the app discreet, never announce it
  // by accident.
  it('is true only for the exact string', () => {
    expect(installableOf({ 'app.installable': 'true' })).toBe(true)
    expect(installableOf({ 'app.installable': 'True' })).toBe(false)
    expect(installableOf({})).toBe(false)
  })
})
