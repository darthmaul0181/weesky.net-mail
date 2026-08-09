import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthProvider } from '../../contexts/AuthContext'
import { LocaleProvider } from '../../contexts/LocaleContext'
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
  getConnectedAccounts: vi.fn(),
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
    getConnectedAccounts: mocks.getConnectedAccounts,
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
        <AuthProvider><LocaleProvider><RouterProvider router={router} /></LocaleProvider></AuthProvider>
      </ThemeProvider>
    </QueryClientProvider>
  )
  return router
}

const baseAccount = {
  userName: 'mick', mailbox: 'WSY', fullName: 'Mick',
  domains: [{ id: 'WSY', name: 'weesky.be' }],
}

const connectedRow = (over: Record<string, unknown> = {}) => ({
  id: 'g1', email: 'support@acme.com', displayName: 'Support', domainId: 'd1', domainName: 'acme.com',
  sieveSupported: true, credentialsValid: true, creationDate: '2026-07-01',
  ...over,
})

describe('settings section', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    mocks.hasSession.mockReturnValue(true)
    mocks.getQuota.mockResolvedValue(null)
    mocks.adminGetUsers.mockResolvedValue([])
    mocks.adminGetDomains.mockResolvedValue([])
    mocks.getConnectedAccounts.mockResolvedValue([])
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
    expect(nav.getByText('Connected accounts')).toBeInTheDocument()
    expect(nav.getByText('Appearance')).toBeInTheDocument()
    expect(nav.getByText('Folders')).toBeInTheDocument()
    expect(nav.getByText('Aliases')).toBeInTheDocument()
    expect(nav.getByText('Identities')).toBeInTheDocument()
    expect(nav.getByText('Rules')).toBeInTheDocument()
    await waitFor(() => expect(mocks.setIsAdmin).toHaveBeenCalledWith(false))
    expect(nav.queryByText('Administration')).not.toBeInTheDocument()
  })

  // website-design.md, § Page layout: a settings page's title pairs a leading icon with the text.
  // Walked route by route rather than asserted per page, because the five pages that had drifted
  // each looked perfectly fine on their own — only the set showed the rule was not being kept.
  it.each([
    ['/settings/account', 'Account'],
    ['/settings/general', 'General'],
    ['/settings/accounts', 'Connected accounts'],
    ['/settings/appearance', 'Appearance'],
    ['/settings/folders', 'Folders'],
    ['/settings/aliases', 'Aliases'],
    ['/settings/identities', 'Identities'],
    ['/settings/rules', 'Rules'],
    ['/settings/admin', 'Administration'],
  ])('%s pairs an icon with its <h1> title', async (path, title) => {
    mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: true })
    renderAt(path)

    const heading = await screen.findByRole('heading', { level: 1 })
    expect(heading.textContent).toContain(title)
    expect(heading.querySelector('svg')).toBeInTheDocument()
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

  it('hides Account, Aliases and Administration for a connected account, keeps Identities and Rules', async () => {
    localStorage.setItem('mail.activeAccount', 'g1')
    mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: true })
    mocks.getConnectedAccounts.mockResolvedValue([connectedRow()])
    renderAt('/settings/general')
    const nav = within(await screen.findByRole('navigation', { name: 'Settings' }))
    await waitFor(() => expect(nav.queryByText('Account')).not.toBeInTheDocument())
    expect(nav.queryByText('Aliases')).not.toBeInTheDocument()
    expect(nav.queryByText('Administration')).not.toBeInTheDocument()
    expect(nav.getByText('Identities')).toBeInTheDocument()
    expect(nav.getByText('Rules')).toBeInTheDocument()
  })

  it('hides Rules when the connected account does not support Sieve', async () => {
    localStorage.setItem('mail.activeAccount', 'g1')
    mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: false })
    mocks.getConnectedAccounts.mockResolvedValue([connectedRow({ sieveSupported: false })])
    renderAt('/settings/general')
    const nav = within(await screen.findByRole('navigation', { name: 'Settings' }))
    await waitFor(() => expect(nav.queryByText('Account')).not.toBeInTheDocument())
    expect(nav.queryByText('Rules')).not.toBeInTheDocument()
  })

  // The loading pin: activeAccount is null until the connected-accounts query resolves, and the
  // gate must read that as "show everything", not as "hide everything until proven primary".
  it('shows the full primary nav while the account list is still loading', async () => {
    mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: true })
    mocks.getConnectedAccounts.mockReturnValue(new Promise(() => {}))
    renderAt('/settings/general')
    const nav = within(await screen.findByRole('navigation', { name: 'Settings' }))
    await waitFor(() => expect(mocks.setIsAdmin).toHaveBeenCalledWith(true))
    expect(nav.getByText('Account')).toBeInTheDocument()
    expect(nav.getByText('Aliases')).toBeInTheDocument()
    expect(nav.getByText('Rules')).toBeInTheDocument()
    expect(nav.getByText('Administration')).toBeInTheDocument()
  })

  it('deep-links to /settings/account under a connected account and redirects to General', async () => {
    localStorage.setItem('mail.activeAccount', 'g1')
    mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: false })
    mocks.getConnectedAccounts.mockResolvedValue([connectedRow()])
    const router = renderAt('/settings/account')
    await waitFor(() => expect(router.state.location.pathname).toBe('/settings/general'))
  })

  it('deep-links to /settings/rules under a non-Sieve connected account and redirects to General', async () => {
    localStorage.setItem('mail.activeAccount', 'g1')
    mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: false })
    mocks.getConnectedAccounts.mockResolvedValue([connectedRow({ sieveSupported: false })])
    const router = renderAt('/settings/rules')
    await waitFor(() => expect(router.state.location.pathname).toBe('/settings/general'))
  })
})
