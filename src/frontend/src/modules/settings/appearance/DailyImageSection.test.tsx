import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { QueryClientProvider } from '@tanstack/react-query'
import { createTestQueryClient, setupUser } from '../../../test-utils'
import DailyImageSection from './DailyImageSection'

const mocks = vi.hoisted(() => ({ getPreferences: vi.fn(), setPreference: vi.fn(), getAppSettings: vi.fn() }))
vi.mock('../../../api.js', () => ({ api: mocks }))

function renderSection(enabled: boolean, style = 'none') {
  mocks.getAppSettings.mockResolvedValue({ 'app.dailyImage': String(enabled) })
  mocks.getPreferences.mockResolvedValue({ 'ui.dailyImage': style })
  mocks.setPreference.mockResolvedValue(null)
  return render(<QueryClientProvider client={createTestQueryClient()}><DailyImageSection /></QueryClientProvider>)
}

beforeEach(() => vi.clearAllMocks())

describe('DailyImageSection (Appearance)', () => {
  it('offers the four choices, the stored one checked', async () => {
    renderSection(true, 'postcard')

    await waitFor(() => expect(screen.getByRole('radio', { name: 'Postcard' })).toBeChecked())
    expect(screen.getAllByRole('radio').map(r => r.getAttribute('value')))
      .toEqual(['none', 'fullBleed', 'postcard', 'watermark'])
    expect(screen.getByText('Shown in the reading pane when no message is open.')).toBeInTheDocument()
  })

  it('stores the choice on the account', async () => {
    const user = setupUser()
    renderSection(true)

    await user.click(await screen.findByRole('radio', { name: 'Watermark' }))

    expect(mocks.setPreference).toHaveBeenCalledWith('ui.dailyImage', 'watermark')
  })

  it('hides the section while the admin left the feature off, keeping the stored choice', async () => {
    renderSection(false, 'watermark')

    await waitFor(() => expect(mocks.getAppSettings).toHaveBeenCalled())
    expect(screen.queryByRole('radiogroup')).toBeNull()
    expect(mocks.setPreference).not.toHaveBeenCalled()
  })
})
