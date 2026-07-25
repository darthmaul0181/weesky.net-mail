import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
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
  adminGetUsers: vi.fn(),
  adminGetDomains: vi.fn(),
  getMailFolders: vi.fn(),
  getPreferences: vi.fn(),
}))

vi.mock('../../api.js', () => ({
  api: {
    getAccount: mocks.getAccount,
    getQuota: mocks.getQuota,
    logout: mocks.logout,
    adminGetUsers: mocks.adminGetUsers,
    adminGetDomains: mocks.adminGetDomains,
    getMailFolders: mocks.getMailFolders,
    getPreferences: mocks.getPreferences,
  },
  hasSession: mocks.hasSession,
  clearSession: mocks.clearSession,
  setUnauthorizedHandler: mocks.setUnauthorizedHandler,
  setIsAdmin: mocks.setIsAdmin,
}))

function renderAt(path: string) {
  const router = createMemoryRouter(routes, { initialEntries: [path] })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <ThemeProvider>
        <AuthProvider><RouterProvider router={router} /></AuthProvider>
      </ThemeProvider>
    </QueryClientProvider>
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
    mocks.adminGetUsers.mockResolvedValue([])
    mocks.adminGetDomains.mockResolvedValue([])
    // The shell now watches the inbox app-wide, so every route mounts these two.
    mocks.getMailFolders.mockResolvedValue([])
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '30' })
  })

  it('/settings redirects to /settings/account', async () => {
    mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: false })
    const router = renderAt('/settings')
    await waitFor(() => expect(router.state.location.pathname).toBe('/settings/account'))
  })

  // The old URL was linked from the mail column and may sit in a bookmark.
  it('redirects the old system-folders URL to the folders page', async () => {
    const router = renderAt('/settings/system-folders')
    await waitFor(() => expect(router.state.location.pathname).toBe('/settings/folders'))
  })

  it('shows the nav without Administration for non-admins', async () => {
    mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: false })
    renderAt('/settings/account')
    const nav = within(await screen.findByRole('navigation', { name: 'Settings' }))
    expect(nav.getByText('Account')).toBeInTheDocument()
    expect(nav.getByText('General')).toBeInTheDocument()
    expect(nav.getByText('Linked accounts')).toBeInTheDocument()
    expect(nav.getByText('Appearance')).toBeInTheDocument()
    expect(nav.getByText('Folders list')).toBeInTheDocument()
    expect(nav.getByText('Aliases')).toBeInTheDocument()
    expect(nav.getByText('Identities')).toBeInTheDocument()
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

  it('renders AdminPage for admins at /settings/admin (RequireAdmin happy path)', async () => {
    mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: true })
    const router = renderAt('/settings/admin')
    expect(await screen.findByRole('button', { name: 'Accounts' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Domains' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Virtual domains' })).toBeInTheDocument()
    expect(await screen.findByText('Accounts (0)')).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/settings/admin')
  })
})
