import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthProvider } from '../../contexts/AuthContext'
import { LocaleProvider } from '../../contexts/LocaleContext'
import { ThemeProvider } from '../../contexts/ThemeContext'
import { routes } from '../../routes'
import { mockViewport, resetViewport, settle } from '../../test-utils'

afterEach(resetViewport)

const mocks = vi.hoisted(() => ({
  getAccount: vi.fn(),
  getCapabilities: vi.fn(),
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
    getCapabilities: mocks.getCapabilities,
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
    // Omits every flag — a capabilities response with nothing gated, or a platform that hasn't
    // restricted anything. The gate is `!== false`, so an absent key must leave every surface up.
    mocks.getCapabilities.mockResolvedValue({})
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

  describe('capability gating', () => {
    it('hides Aliases when the platform does not wire it up', async () => {
      mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: true })
      mocks.getCapabilities.mockResolvedValue({ aliases: false })
      renderAt('/settings/account')
      const nav = within(await screen.findByRole('navigation', { name: 'Settings' }))
      expect(nav.getByText('Account')).toBeInTheDocument()
      await waitFor(() => expect(nav.queryByText('Aliases')).not.toBeInTheDocument())
      // Unrelated surfaces stay up — one flag going false must not take the others with it.
      expect(nav.getByText('Administration')).toBeInTheDocument()
    })

    it('hides Administration when the platform does not wire it up, even for an admin', async () => {
      mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: true })
      mocks.getCapabilities.mockResolvedValue({ admin: false })
      renderAt('/settings/account')
      const nav = within(await screen.findByRole('navigation', { name: 'Settings' }))
      await waitFor(() => expect(mocks.setIsAdmin).toHaveBeenCalledWith(true))
      await waitFor(() => expect(nav.queryByText('Administration')).not.toBeInTheDocument())
    })

    it('hides Rules on the primary account when the platform does not wire it up', async () => {
      mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: false })
      mocks.getCapabilities.mockResolvedValue({ rules: false })
      renderAt('/settings/account')
      const nav = within(await screen.findByRole('navigation', { name: 'Settings' }))
      expect(nav.getByText('Account')).toBeInTheDocument()
      await waitFor(() => expect(nav.queryByText('Rules')).not.toBeInTheDocument())
    })

    // The connected account's Rules gate is sieveSupported, not the platform's capabilities — the
    // two must not be conflated, or a generic-platform connected mailbox loses Rules for no reason.
    it('leaves a connected account\'s Sieve-based Rules gating untouched by capabilities.rules', async () => {
      localStorage.setItem('mail.activeAccount', 'g1')
      mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: false })
      mocks.getConnectedAccounts.mockResolvedValue([connectedRow({ sieveSupported: true })])
      mocks.getCapabilities.mockResolvedValue({ rules: false })
      renderAt('/settings/general')
      const nav = within(await screen.findByRole('navigation', { name: 'Settings' }))
      await waitFor(() => expect(nav.queryByText('Account')).not.toBeInTheDocument())
      expect(nav.getByText('Rules')).toBeInTheDocument()
    })

    // The nav already hides these three rows on the flag; a deep link (bookmark, typed URL) is a
    // second door to the same screen and has to be guarded independently — that guard is what
    // these six cases exercise, on the model of the Sieve deep-link test above.
    describe('deep-link guarding', () => {
      it('deep-links to /settings/admin with capabilities.admin=false and redirects to Account', async () => {
        mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: true })
        mocks.getCapabilities.mockResolvedValue({ admin: false })
        const router = renderAt('/settings/admin')
        await waitFor(() => expect(router.state.location.pathname).toBe('/settings/account'))
      })

      it('deep-links to /settings/admin as an admin with no capabilities fixture and stays', async () => {
        mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: true })
        mocks.getCapabilities.mockResolvedValue({})
        const router = renderAt('/settings/admin')
        expect(await screen.findByRole('button', { name: 'Accounts' })).toBeInTheDocument()
        expect(router.state.location.pathname).toBe('/settings/admin')
      })

      it('deep-links to /settings/rules on the primary account with capabilities.rules=false and redirects to General', async () => {
        mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: false })
        mocks.getCapabilities.mockResolvedValue({ rules: false })
        const router = renderAt('/settings/rules')
        await waitFor(() => expect(router.state.location.pathname).toBe('/settings/general'))
      })

      it('deep-links to /settings/rules on the primary account with no capabilities fixture and stays', async () => {
        mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: false })
        mocks.getCapabilities.mockResolvedValue({})
        const router = renderAt('/settings/rules')
        await screen.findByRole('navigation', { name: 'Settings' })
        expect(router.state.location.pathname).toBe('/settings/rules')
      })

      it('deep-links to /settings/aliases with capabilities.aliases=false and redirects to General', async () => {
        mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: false })
        mocks.getCapabilities.mockResolvedValue({ aliases: false })
        const router = renderAt('/settings/aliases')
        await waitFor(() => expect(router.state.location.pathname).toBe('/settings/general'))
      })

      it('deep-links to /settings/aliases with no capabilities fixture and stays', async () => {
        mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: false })
        mocks.getCapabilities.mockResolvedValue({})
        const router = renderAt('/settings/aliases')
        await screen.findByRole('navigation', { name: 'Settings' })
        expect(router.state.location.pathname).toBe('/settings/aliases')
      })

      // The race the redirect used to lose: activeAccount is null for the width of the connected
      // accounts fetch, and the `isPrimary` fallback that reads null as "primary" is right for a
      // nav row (it just re-renders once the list lands) but was wrong for a redirect — a
      // connected, Sieve-capable account deep-linking in while capabilities.rules resolves false
      // first got bounced off a page it was actually allowed to see.
      it('does not redirect a deep-linked /settings/rules while capabilities.rules=false resolves before the still-loading account list, then stays once the connected account\'s sieveSupported lands true', async () => {
        localStorage.setItem('mail.activeAccount', 'g1')
        mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: false })
        mocks.getCapabilities.mockResolvedValue({ rules: false })
        let resolveAccounts: (rows: unknown[]) => void = () => {}
        mocks.getConnectedAccounts.mockReturnValue(new Promise(resolve => { resolveAccounts = resolve }))
        const router = renderAt('/settings/rules')

        await waitFor(() => expect(mocks.getCapabilities).toHaveBeenCalled())
        await settle()
        expect(router.state.location.pathname).toBe('/settings/rules')

        resolveAccounts([connectedRow({ sieveSupported: true })])
        await screen.findByRole('navigation', { name: 'Settings' })
        await settle()
        expect(router.state.location.pathname).toBe('/settings/rules')
      })
    })
  })
})

describe('SettingsLayout below 1024px', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    mocks.hasSession.mockReturnValue(true)
    mocks.getAccount.mockResolvedValue({ ...baseAccount, isAdmin: false })
    mocks.getCapabilities.mockResolvedValue({})
    mocks.getQuota.mockResolvedValue(null)
    mocks.adminGetUsers.mockResolvedValue([])
    mocks.adminGetDomains.mockResolvedValue([])
    mocks.getConnectedAccounts.mockResolvedValue([])
    mocks.getMailFolders.mockResolvedValue([])
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '30' })
  })

  // Both narrow tiers, not just the tablet: the drawer's boundary is 1024px, and a rule keyed on
  // the phone width alone would leave a tablet with no way to reach the navigation at all.
  it.each(['tablet', 'phone'] as const)('puts its navigation in a drawer behind a toggle (%s)', async tier => {
    mockViewport(tier)
    renderAt('/settings/appearance')
    await settle()
    expect(document.querySelector('.context-drawer .context-pane')).toBeTruthy()
    expect(document.querySelector('.drawer-toggle')).toBeTruthy()
    // The pane is rendered through a ternary, never twice: a second copy would sit behind the
    // drawer, out of reach and duplicating every NavLink in the accessibility tree.
    expect(document.querySelectorAll('.context-pane')).toHaveLength(1)
  })

  it('leaves the navigation inline on a desktop', async () => {
    mockViewport('desktop')
    renderAt('/settings/appearance')
    await settle()
    expect(document.querySelector('.context-drawer')).toBeNull()
    expect(document.querySelector('.drawer-toggle')).toBeNull()
    expect(document.querySelector('.settings-mobile-bar')).toBeNull()
  })

  // The bar names the page, and it takes that name from the nav's own row rather than from a
  // second copy of the labels — the drift guard is that both come out of one array.
  it('names the section in its bar, not the module', async () => {
    mockViewport('phone')
    renderAt('/settings/appearance')
    await settle()
    const bar = document.querySelector('.settings-mobile-title')
    expect(bar?.textContent).toBe('Appearance')
    const nav = within(await screen.findByRole('navigation', { name: 'Settings' }))
    expect(nav.getByText('Appearance')).toBeInTheDocument()
  })

  it('opens the drawer from the hamburger', async () => {
    mockViewport('tablet')
    renderAt('/settings/general')
    await settle()
    expect(document.querySelector('.context-drawer.is-open')).toBeNull()
    await userEvent.click(screen.getByRole('button', { name: 'Open navigation' }))
    expect(document.querySelector('.context-drawer.is-open')).toBeTruthy()
  })

  // Picking a row closes the drawer and retitles the bar. Note what this does NOT prove:
  // ContextDrawer holds `onClose` in a ref and its route effect depends on [pathname, search]
  // alone, so an inline arrow would pass this too — nothing here or anywhere else enforces the
  // by-reference contract, which is a convention rather than a guarantee.
  it('closes the drawer on a pick and follows the section name', async () => {
    mockViewport('tablet')
    renderAt('/settings/general')
    await settle()
    await userEvent.click(screen.getByRole('button', { name: 'Open navigation' }))
    await userEvent.click(screen.getByRole('link', { name: 'Appearance' }))
    await waitFor(() => expect(document.querySelector('.context-drawer.is-open')).toBeNull())
    expect(document.querySelector('.settings-mobile-title')?.textContent).toBe('Appearance')
  })
})
