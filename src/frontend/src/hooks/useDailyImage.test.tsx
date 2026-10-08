import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { act, renderHook, waitFor } from '@testing-library/react'
import { QueryClientProvider, type QueryClient } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import i18next from 'i18next'
import { createTestQueryClient } from '../test-utils'
import { useDailyImage } from './useDailyImage'

const mocks = vi.hoisted(() => ({ getAppSettings: vi.fn(), getDailyImage: vi.fn() }))
vi.mock('../api.js', () => ({ api: mocks }))

// jsdom never loads an image: this one loads unless its address says "broken".
class LoadingImage {
  onload: (() => void) | null = null
  onerror: (() => void) | null = null
  set src(value: string) {
    queueMicrotask(() => (value.includes('broken') ? this.onerror?.() : this.onload?.()))
  }
}

// One client per test: the wrapper runs again on every rerender, and a fresh client would drop the cache.
let client: QueryClient
function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

beforeEach(() => {
  client = createTestQueryClient()
  vi.stubGlobal('Image', LoadingImage)
  mocks.getAppSettings.mockResolvedValue({ 'app.dailyImage': 'true' })
  mocks.getDailyImage.mockResolvedValue({ version: 'v1', title: 'Poulpe fiction', copyright: '© G. Barathieu' })
})
afterEach(async () => {
  vi.unstubAllGlobals()
  vi.clearAllMocks()
  await i18next.changeLanguage('en')
})

describe('useDailyImage', () => {
  it('answers the photo and its credit once the photo has loaded', async () => {
    const { result } = renderHook(() => useDailyImage(), { wrapper })

    await waitFor(() => expect(result.current).not.toBeNull())
    expect(result.current!.src).toMatch(/\/api\/AppSettings\/daily-image\/v1\?lang=en$/)
    expect(result.current!.title).toBe('Poulpe fiction')
    expect(result.current!.copyright).toBe('© G. Barathieu')
  })

  it('never asks when the admin left the feature off', async () => {
    mocks.getAppSettings.mockResolvedValue({ 'app.dailyImage': 'false' })
    const { result } = renderHook(() => useDailyImage(), { wrapper })

    await waitFor(() => expect(mocks.getAppSettings).toHaveBeenCalled())
    expect(mocks.getDailyImage).not.toHaveBeenCalled()
    expect(result.current).toBeNull()
  })

  it('never asks when the caller does not want it', async () => {
    const { result } = renderHook(() => useDailyImage(false), { wrapper })

    await waitFor(() => expect(mocks.getAppSettings).toHaveBeenCalled())
    expect(mocks.getDailyImage).not.toHaveBeenCalled()
    expect(result.current).toBeNull()
  })

  it('stays null when the photo fails to load', async () => {
    mocks.getDailyImage.mockResolvedValue({ version: 'broken', title: 't', copyright: 'c' })
    const { result } = renderHook(() => useDailyImage(), { wrapper })

    await waitFor(() => expect(mocks.getDailyImage).toHaveBeenCalled())
    await act(async () => { await Promise.resolve() })
    expect(result.current).toBeNull()
  })

  it('follows the interface language', async () => {
    const { result } = renderHook(() => useDailyImage(), { wrapper })
    await waitFor(() => expect(result.current).not.toBeNull())

    await act(async () => { await i18next.changeLanguage('fr') })

    await waitFor(() => expect(mocks.getDailyImage).toHaveBeenCalledWith('fr', expect.anything()))
    await waitFor(() => expect(result.current!.src).toMatch(/lang=fr$/))
  })
})
