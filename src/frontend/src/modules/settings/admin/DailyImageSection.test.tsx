import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { QueryClientProvider } from '@tanstack/react-query'
import { createTestQueryClient, setupUser } from '../../../test-utils'
import DailyImageSection from './DailyImageSection'

const mocks = vi.hoisted(() => ({ getAppSettings: vi.fn(), setAppSetting: vi.fn() }))
vi.mock('../../../api.js', () => ({ api: mocks }))

beforeEach(() => {
  vi.clearAllMocks()
  mocks.getAppSettings.mockResolvedValue({ 'app.dailyImage': 'false' })
  mocks.setAppSetting.mockResolvedValue(null)
})

describe('DailyImageSection (Administration)', () => {
  it('switches the feature on and says so', async () => {
    const user = setupUser()
    const addToast = vi.fn()
    render(<QueryClientProvider client={createTestQueryClient()}><DailyImageSection addToast={addToast} /></QueryClientProvider>)

    const toggle = await screen.findByRole('checkbox', { name: 'Image of the day (Bing)' })
    expect(toggle).not.toBeChecked()
    await user.click(toggle)

    expect(mocks.setAppSetting).toHaveBeenCalledWith('app.dailyImage', 'true')
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('The image of the day is on'))
  })

  it('reports a refused change', async () => {
    const user = setupUser()
    const addToast = vi.fn()
    mocks.setAppSetting.mockRejectedValue(new Error('boom'))
    render(<QueryClientProvider client={createTestQueryClient()}><DailyImageSection addToast={addToast} /></QueryClientProvider>)

    await user.click(await screen.findByRole('checkbox', { name: 'Image of the day (Bing)' }))

    await waitFor(() => expect(addToast).toHaveBeenCalledWith(expect.any(String), 'error'))
  })
})
