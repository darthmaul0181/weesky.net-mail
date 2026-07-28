import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { act, renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { useAppSettings } from './useAppSettings'
import { useWebAppManifest } from './useWebAppManifest'

const mocks = vi.hoisted(() => ({ getAppSettings: vi.fn(), setAppSetting: vi.fn() }))
vi.mock('../api.js', () => ({ api: mocks }))

// Hoisted like the sibling useAppSettings.test.tsx: the change tests have to reach the very client
// the hook reads, to drive a settings change through setQueryData.
let client: QueryClient
function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

const enabled = {
  'app.installable': 'true',
  'app.name': 'Snoopy mail',
  'app.shortName': 'Snoopy',
}

function manifestLink() {
  return document.head.querySelector('link[rel="manifest"]')
}

// The two "posts nothing" tests need a handle on the query itself. Waiting on the api call is not
// enough: it is issued synchronously at mount, so the assertion would run before the settings had
// answered and would hold just as well against a hook that posts for a disabled app — measured.
// The probe shares the hook's key and client, so it reads that same query rather than firing a
// second one.
function renderWithSettings() {
  return renderHook(() => {
    useWebAppManifest()
    return useAppSettings()
  }, { wrapper })
}

describe('useWebAppManifest', () => {
  // jsdom implements neither createObjectURL nor revokeObjectURL: they are installed here and
  // removed afterwards, rather than replaced globally — mailtoSeed uses the real URL.
  // Each call answers a distinct url, so a test can tell a withdrawn manifest from its replacement
  // instead of reading one constant that stands for both.
  beforeEach(() => {
    vi.clearAllMocks()
    client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    let issued = 0
    URL.createObjectURL = vi.fn(() => `blob:mock-${++issued}`)
    URL.revokeObjectURL = vi.fn()
  })
  afterEach(() => {
    manifestLink()?.remove()
    delete (URL as Partial<typeof URL>).createObjectURL
    delete (URL as Partial<typeof URL>).revokeObjectURL
  })

  it('posts a manifest link when the app is enabled', async () => {
    mocks.getAppSettings.mockResolvedValue(enabled)

    renderHook(() => useWebAppManifest(), { wrapper })

    await waitFor(() => expect(manifestLink()).not.toBeNull())
    expect(manifestLink()!.getAttribute('href')).toBe('blob:mock-1')
  })

  // The switch left off must post nothing at all: that is what keeps the install icon from
  // appearing and then vanishing on load.
  it('posts nothing when the app is disabled', async () => {
    mocks.getAppSettings.mockResolvedValue({ ...enabled, 'app.installable': 'false' })

    const { result } = renderWithSettings()

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(manifestLink()).toBeNull()
  })

  it('posts nothing when the settings cannot be read', async () => {
    mocks.getAppSettings.mockRejectedValue(new Error('offline'))

    const { result } = renderWithSettings()

    await waitFor(() => expect(result.current.isError).toBe(true))
    expect(manifestLink()).toBeNull()
  })

  // The admin switching the app off mid-session is the "icon vanishes" path, and the one the
  // change branch of the cleanup exists for: the withdrawal has to take the old link and its blob
  // with it, not merely stop posting new ones.
  it('removes the link and revokes its url when the app is switched off', async () => {
    mocks.getAppSettings.mockResolvedValue(enabled)

    renderHook(() => useWebAppManifest(), { wrapper })
    await waitFor(() => expect(manifestLink()).not.toBeNull())

    act(() => {
      client.setQueryData(['appSettings'], { ...enabled, 'app.installable': 'false' })
    })

    await waitFor(() => expect(manifestLink()).toBeNull())
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:mock-1')
  })

  // A rename replaces the manifest instead of withdrawing it, and the blob behind the old one has
  // to go the same way: an un-revoked Blob lives for the lifetime of the document.
  it('revokes the previous url when the settings change', async () => {
    mocks.getAppSettings.mockResolvedValue(enabled)

    renderHook(() => useWebAppManifest(), { wrapper })
    await waitFor(() => expect(manifestLink()).not.toBeNull())

    act(() => {
      client.setQueryData(['appSettings'], { ...enabled, 'app.name': 'Woodstock mail' })
    })

    await waitFor(() => expect(manifestLink()!.getAttribute('href')).toBe('blob:mock-2'))
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:mock-1')
  })

  // Without revocation, every pass would leave a Blob alive for the lifetime of the document.
  it('removes the link and revokes its url on unmount', async () => {
    mocks.getAppSettings.mockResolvedValue(enabled)

    const { unmount } = renderHook(() => useWebAppManifest(), { wrapper })
    await waitFor(() => expect(manifestLink()).not.toBeNull())

    unmount()

    expect(manifestLink()).toBeNull()
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:mock-1')
  })
})
