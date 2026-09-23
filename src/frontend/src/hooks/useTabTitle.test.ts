import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createElement, type ReactNode } from 'react'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { APP_NAME_STORAGE_KEY, tabTitle, useTabTitle } from './useTabTitle'
import { api } from '../api.js'
import { settle } from '../test-utils'

const auth = vi.hoisted(() => ({ activeAccount: null as { email: string } | null }))

vi.mock('../contexts/AuthContext', () => ({ useAuth: () => auth }))
vi.mock('../api.js', async importOriginal => ({
  ...await importOriginal<typeof import('../api.js')>(),
  api: { getAppSettings: vi.fn() },
}))

function mount() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const wrapper = ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client }, children)
  return renderHook(() => useTabTitle(), { wrapper })
}

beforeEach(() => {
  auth.activeAccount = null
  localStorage.clear()
  document.title = ''
  vi.mocked(api.getAppSettings).mockResolvedValue({ 'app.name': 'Scotty webmail' })
})

describe('tabTitle', () => {
  it('puts the mailbox in front of the application name', () => {
    expect(tabTitle('mick@weesky.be', 'Scotty webmail')).toBe('mick@weesky.be · Scotty webmail')
  })

  // The account list has not landed yet; naming the signed-in address here would announce the
  // primary mailbox on a tab that is about to open a connected one.
  it('leaves the application name alone while no account is resolved', () => {
    expect(tabTitle(null, 'Scotty webmail')).toBe('Scotty webmail')
  })
})

describe('useTabTitle', () => {
  it('titles the tab with the application name the administrator set', async () => {
    auth.activeAccount = { email: 'mick@weesky.be' }
    mount()

    await waitFor(() => expect(document.title).toBe('mick@weesky.be · Scotty webmail'))
  })

  // index.html paints the remembered name before React runs, so the next load shows no hostname.
  it('remembers the name for the next load', async () => {
    mount()

    await waitFor(() => expect(localStorage.getItem(APP_NAME_STORAGE_KEY)).toBe('Scotty webmail'))
  })

  it('keeps the remembered name while the settings are unreachable', async () => {
    localStorage.setItem(APP_NAME_STORAGE_KEY, 'Weesky Mail')
    vi.mocked(api.getAppSettings).mockRejectedValue(new Error('down'))
    mount()
    await settle()

    expect(document.title).toBe('Weesky Mail')
  })

  it('falls back to the hostname with neither a setting nor a remembered name', async () => {
    vi.mocked(api.getAppSettings).mockRejectedValue(new Error('down'))
    mount()
    await settle()

    expect(document.title).toBe(window.location.hostname)
  })
})
