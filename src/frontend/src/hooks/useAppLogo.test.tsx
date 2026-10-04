import { beforeEach, describe, expect, it, vi } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { useAppLogo } from './useAppLogo'
import { createTestQueryClient, withQueryClient } from '../test-utils'

const mocks = vi.hoisted(() => ({ getAppSettings: vi.fn() }))
vi.mock('../api.js', () => ({ api: mocks }))

beforeEach(() => { localStorage.clear(); vi.clearAllMocks() })

const render = () => renderHook(() => useAppLogo(), { wrapper: withQueryClient(createTestQueryClient()) })

describe('useAppLogo', () => {
  it('uses the API logo once the settings name a version, and remembers it', async () => {
    mocks.getAppSettings.mockResolvedValue({ 'app.logo': 'v1' })
    const { result } = render()

    await waitFor(() => expect(result.current[192]).toMatch(/v=v1$/))
    expect(localStorage.getItem('app.logoVersion')).toBe('v1')
  })

  // No Scotty-then-organisation flash on every page load.
  it('starts from the remembered version while the settings are on their way', () => {
    localStorage.setItem('app.logoVersion', 'v1')
    mocks.getAppSettings.mockReturnValue(new Promise(() => {}))

    expect(render().result.current[192]).toMatch(/v=v1$/)
  })

  it('goes back to Scotty, and forgets, when the admin restored the default', async () => {
    localStorage.setItem('app.logoVersion', 'v1')
    mocks.getAppSettings.mockResolvedValue({ 'app.logo': '' })
    const { result } = render()

    await waitFor(() => expect(result.current[192]).toMatch(/logo-192/))
    expect(localStorage.getItem('app.logoVersion')).toBe('')
  })
})
