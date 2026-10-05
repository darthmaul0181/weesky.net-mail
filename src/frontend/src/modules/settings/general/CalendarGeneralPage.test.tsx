import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import CalendarGeneralPage from './CalendarGeneralPage'
import { createTestQueryClient, pickOption, setupUser } from '../../../test-utils'

const mocks = vi.hoisted(() => ({
  getPreferences: vi.fn(),
  setPreference: vi.fn(),
  setBirthdays: vi.fn(),
}))
vi.mock('../../../api.js', () => ({ api: mocks }))
vi.mock('../../../contexts/AuthContext', () => import('../../../test-auth'))

function wrapper({ children }: { children: ReactNode }) {
  const client = createTestQueryClient()
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

function renderPage(preferences: Record<string, string> = { 'mail.pageSize': '30' }) {
  mocks.getPreferences.mockResolvedValue(preferences)
  mocks.setPreference.mockResolvedValue(undefined)
  mocks.setBirthdays.mockResolvedValue(null)
  return render(<CalendarGeneralPage />, { wrapper })
}

let user: ReturnType<typeof setupUser>
beforeEach(() => { user = setupUser() })

describe('CalendarGeneralPage', () => {
  beforeEach(() => vi.clearAllMocks())

  it('names its module above the title', async () => {
    renderPage()
    expect(await screen.findByRole('heading', { level: 1 })).toHaveTextContent('General')
    expect(screen.getByText('Calendar')).toBeInTheDocument()
  })

  it('groups the settings under section headings', async () => {
    renderPage()
    await screen.findByLabelText('First day of the week')

    expect(screen.getAllByRole('heading', { level: 2 }).map(heading => heading.textContent))
      .toEqual(['Calendars', 'Week'])
  })
})

describe('the first day of the week', () => {
  beforeEach(() => vi.clearAllMocks())

  it('shows Monday when nothing is stored', async () => {
    renderPage()
    expect(await screen.findByLabelText('First day of the week')).toHaveTextContent('Monday')
  })

  it('saves Sunday', async () => {
    renderPage()
    await pickOption(await screen.findByLabelText('First day of the week'), 'Sunday')
    await waitFor(() => expect(mocks.setPreference).toHaveBeenCalledWith('calendar.firstDayOfWeek', 'sunday'))
    expect(await screen.findByText('Weeks now start on Sunday')).toBeInTheDocument()
  })
})

describe('the birthdays calendar switch', () => {
  beforeEach(() => vi.clearAllMocks())

  it('shows it unchecked when the preference is off', async () => {
    renderPage({ 'mail.pageSize': '30', 'calendar.birthdays': 'off' })

    expect(await screen.findByRole('checkbox', { name: 'Birthdays calendar' })).not.toBeChecked()
  })

  it('shows it checked by default, and switches it through its own route', async () => {
    renderPage()
    const toggle = await screen.findByRole('checkbox', { name: 'Birthdays calendar' })
    expect(toggle).toBeChecked()

    await user.click(toggle)

    expect(mocks.setBirthdays).toHaveBeenCalledWith(false, expect.any(String), 'en')
    expect(mocks.setPreference).not.toHaveBeenCalledWith('calendar.birthdays', expect.anything())
    expect(await screen.findByText('Birthdays calendar turned off')).toBeInTheDocument()
  })

  it('surfaces a failure to save instead of pretending', async () => {
    renderPage()
    mocks.setBirthdays.mockRejectedValue(new Error('Refused by the server'))
    const toggle = await screen.findByRole('checkbox', { name: 'Birthdays calendar' })

    await user.click(toggle)

    expect(await screen.findByText('Could not save the setting')).toBeInTheDocument()
  })
})
