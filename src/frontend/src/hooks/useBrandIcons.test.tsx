import { beforeEach, describe, expect, it, vi } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { useBrandIcons } from './useBrandIcons'
import { currentLogo } from '../lib/appLogo'
import { createTestQueryClient, withQueryClient } from '../test-utils'

const mocks = vi.hoisted(() => ({ getAppSettings: vi.fn() }))
vi.mock('../api.js', () => ({ api: mocks }))
const favicon = vi.hoisted(() => ({ setFaviconBase: vi.fn() }))
vi.mock('../lib/favicon', () => favicon)

beforeEach(() => {
  localStorage.clear()
  document.head.innerHTML = '<link rel="apple-touch-icon" href="/scotty-192.png">'
})

describe('useBrandIcons', () => {
  it('puts the custom logo on the tab, the iOS icon and the notifications', async () => {
    mocks.getAppSettings.mockResolvedValue({ 'app.logo': 'v1' })
    renderHook(() => useBrandIcons(), { wrapper: withQueryClient(createTestQueryClient()) })

    await waitFor(() => expect(favicon.setFaviconBase).toHaveBeenLastCalledWith(expect.stringMatching(/logo\/32\?v=v1$/)))
    expect(document.querySelector('link[rel="apple-touch-icon"]')!.getAttribute('href')).toMatch(/logo\/192\?v=v1$/)
    expect(currentLogo()[192]).toMatch(/logo\/192\?v=v1$/)
  })
})
