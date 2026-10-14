import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { createTestQueryClient, setupUser, withQueryClient } from '../../../test-utils'
import DailyImageSection from './DailyImageSection'

const mocks = vi.hoisted(() => ({
  getPreferences: vi.fn(), setPreference: vi.fn(), getAppSettings: vi.fn(), getWhatsNew: vi.fn(),
}))
vi.mock('../../../api.js', () => ({ api: mocks }))

function renderSection(enabled: boolean, style = 'none') {
  mocks.getAppSettings.mockResolvedValue({ 'app.dailyImage': String(enabled) })
  mocks.getPreferences.mockResolvedValue({ 'ui.dailyImage': style })
  mocks.setPreference.mockResolvedValue(null)
  mocks.getWhatsNew.mockResolvedValue({ baseline: '0.0.0', items: [] })
  // A router: the section is a what's-new target, which reads the location Show me arrives with.
  return render(<MemoryRouter><DailyImageSection /></MemoryRouter>, { wrapper: withQueryClient(createTestQueryClient()) })
}

beforeEach(() => vi.clearAllMocks())

describe('DailyImageSection (Appearance)', () => {
  it('offers the three choices, the stored one checked', async () => {
    renderSection(true, 'postcard')

    await waitFor(() => expect(screen.getByRole('radio', { name: 'Postcard' })).toBeChecked())
    expect(screen.getAllByRole('radio').map(r => r.getAttribute('value')))
      .toEqual(['none', 'fullBleed', 'postcard'])
    expect(screen.getByText('Shown in the reading pane when no message is open.')).toBeInTheDocument()
  })

  it('stores the choice on the account', async () => {
    const user = setupUser()
    renderSection(true)

    await user.click(await screen.findByRole('radio', { name: 'Full bleed' }))

    expect(mocks.setPreference).toHaveBeenCalledWith('ui.dailyImage', 'fullBleed')
  })

  it('hides the section while the admin left the feature off, keeping the stored choice', async () => {
    renderSection(false, 'postcard')

    await waitFor(() => expect(mocks.getAppSettings).toHaveBeenCalled())
    expect(screen.queryByRole('radiogroup')).toBeNull()
    expect(mocks.setPreference).not.toHaveBeenCalled()
  })
})
