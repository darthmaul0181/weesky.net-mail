import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { AuthProvider } from '../../contexts/AuthContext'
import { ThemeProvider } from '../../contexts/ThemeContext'
import { routes } from '../../routes'

const mocks = vi.hoisted(() => ({
  getAccount: vi.fn(),
  getQuota: vi.fn(),
  logout: vi.fn(),
  hasSession: vi.fn(() => true),
  clearSession: vi.fn(),
  setUnauthorizedHandler: vi.fn(),
  setIsAdmin: vi.fn(),
}))

vi.mock('../../api.js', () => ({
  api: { getAccount: mocks.getAccount, getQuota: mocks.getQuota, logout: mocks.logout },
  hasSession: mocks.hasSession,
  clearSession: mocks.clearSession,
  setUnauthorizedHandler: mocks.setUnauthorizedHandler,
  setIsAdmin: mocks.setIsAdmin,
}))

function renderAt(path: string) {
  const router = createMemoryRouter(routes, { initialEntries: [path] })
  render(
    <ThemeProvider>
      <AuthProvider><RouterProvider router={router} /></AuthProvider>
    </ThemeProvider>
  )
  return router
}

const baseAccount = {
  userName: 'mick', mailbox: 'WSY', fullName: 'Mick',
  domains: [{ id: 'WSY', name: 'weesky.be' }],
}

describe('settings section', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.hasSession.mockReturnValue(true)
    mocks.getQuota.mockResolvedValue(null)
  })

  it('/settings redirects to /settings/account', async () => {
    mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: false })
    const router = renderAt('/settings')
    await waitFor(() => expect(router.state.location.pathname).toBe('/settings/account'))
  })

  it('shows the nav without Administration for non-admins', async () => {
    mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: false })
    renderAt('/settings/account')
    const nav = within(await screen.findByRole('navigation', { name: 'Settings' }))
    expect(nav.getByText('Account')).toBeInTheDocument()
    expect(nav.getByText('Linked accounts')).toBeInTheDocument()
    expect(nav.getByText('Appearance')).toBeInTheDocument()
    expect(nav.getByText('Aliases')).toBeInTheDocument()
    expect(nav.getByText('Rules')).toBeInTheDocument()
    await waitFor(() => expect(mocks.setIsAdmin).toHaveBeenCalledWith(false))
    expect(nav.queryByText('Administration')).not.toBeInTheDocument()
  })

  it('shows Administration for admins', async () => {
    mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: true })
    renderAt('/settings/account')
    const nav = within(await screen.findByRole('navigation', { name: 'Settings' }))
    expect(await nav.findByText('Administration')).toBeInTheDocument()
  })

  it('blocks /settings/admin for non-admins', async () => {
    mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: false })
    const router = renderAt('/settings/admin')
    await waitFor(() => expect(router.state.location.pathname).toBe('/settings/account'))
  })
})
