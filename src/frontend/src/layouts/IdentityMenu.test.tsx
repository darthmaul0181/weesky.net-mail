// Replaces AvatarMenu.test.tsx: the account block moved from the topbar to the foot of the
// folder column. Same behaviours, minus the Settings entry — the rail's gear owns that route.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthProvider } from '../contexts/AuthContext'
import { registerLeaveGuard } from '../lib/leaveGuard'
import IdentityMenu from './IdentityMenu'

const mocks = vi.hoisted(() => ({
  getAccount: vi.fn(),
  logout: vi.fn(),
  getConnectedAccounts: vi.fn(),
  hasSession: vi.fn(() => true),
  clearSession: vi.fn(),
  setUnauthorizedHandler: vi.fn(),
  setIsAdmin: vi.fn(),
}))

vi.mock('../api.js', () => ({
  api: {
    getAccount: mocks.getAccount, logout: mocks.logout,
    getConnectedAccounts: mocks.getConnectedAccounts,
  },
  hasSession: mocks.hasSession,
  clearSession: mocks.clearSession,
  setUnauthorizedHandler: mocks.setUnauthorizedHandler,
  setIsAdmin: mocks.setIsAdmin,
}))

function Path() {
  return <span data-testid="path">{useLocation().pathname}</span>
}

function renderMenu() {
  return render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter>
        <AuthProvider><IdentityMenu /><Path /></AuthProvider>
      </MemoryRouter>
    </QueryClientProvider>
  )
}

const toggle = () => screen.getByRole('button', { name: /account menu/i })
const path = () => screen.getByTestId('path').textContent

function connected(over: Record<string, unknown> = {}) {
  return {
    id: 'g1', email: 'support@acme.com', displayName: 'Support',
    domainId: 'd1', domainName: 'acme.com',
    sieveSupported: true, credentialsValid: true, creationDate: '2026-07-01',
    ...over,
  }
}

async function openMenu() {
  fireEvent.click(await screen.findByRole('button', { name: /account menu/i }))
}

describe('IdentityMenu', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    registerLeaveGuard(null)
    mocks.hasSession.mockReturnValue(true)
    mocks.logout.mockResolvedValue(null)
    mocks.getConnectedAccounts.mockResolvedValue([])
    mocks.getAccount.mockResolvedValue({
      userName: 'mick', mailbox: 'WSY', fullName: 'Mick', isAdmin: false,
      domains: [{ id: 'WSY', name: 'weesky.be' }],
    })
  })

  // The identity is on the band itself, not behind the toggle: that is the point of the move.
  it('shows the identity without opening anything', async () => {
    renderMenu()

    expect(await screen.findByText('mick@weesky.be')).toBeInTheDocument()
    expect(screen.getByText('M')).toBeInTheDocument()
    expect(screen.queryByText('Sign out')).not.toBeInTheDocument()
  })

  it('opens on the chevron, listing the accounts and Sign out', async () => {
    renderMenu()
    await openMenu()

    // Once on the band, once in the menu's account list.
    expect(screen.getAllByText('mick@weesky.be')).toHaveLength(2)
    expect(screen.getAllByRole('menuitem').length).toBeGreaterThanOrEqual(1)
    expect(screen.getByText('Connected accounts…')).toBeInTheDocument()
    expect(screen.getByText('Sign out')).toBeInTheDocument()
  })

  it('signs out via the auth context', async () => {
    renderMenu()
    await openMenu()

    fireEvent.click(screen.getByText('Sign out'))

    await waitFor(() => expect(mocks.logout).toHaveBeenCalled())
    expect(mocks.clearSession).toHaveBeenCalled()
  })

  it('closes on outside mousedown', async () => {
    renderMenu()
    await openMenu()
    expect(screen.getByText('Sign out')).toBeInTheDocument()

    fireEvent.mouseDown(document.body)

    expect(screen.queryByText('Sign out')).not.toBeInTheDocument()
  })

  it('closes on Escape', async () => {
    renderMenu()
    await openMenu()

    fireEvent.keyDown(document, { key: 'Escape' })

    expect(screen.queryByText('Sign out')).not.toBeInTheDocument()
  })

  it('marks the toggle expanded only while the menu is open', async () => {
    renderMenu()
    await screen.findByText('mick@weesky.be')
    expect(toggle()).toHaveAttribute('aria-expanded', 'false')

    fireEvent.click(toggle())

    expect(toggle()).toHaveAttribute('aria-expanded', 'true')
  })

  // The rows are buttons, not decorated divs: a switch has to be reachable from the keyboard.
  it('lists every account as a menu item button, the active one marked', async () => {
    mocks.getConnectedAccounts.mockResolvedValue([connected()])
    renderMenu()
    await openMenu()

    const rows = await screen.findAllByRole('menuitem', { name: /@/ })
    expect(rows).toHaveLength(2)
    rows.forEach(row => expect(row.tagName).toBe('BUTTON'))
    expect(rows[0].className).toContain('is-active')
    expect(rows[1].className).not.toContain('is-active')
  })

  // The mail list's own unread marker, in the place it sits there. The inactive rows keep the
  // gutter so the labels stay in one column instead of stepping in under the active one.
  it('marks the active account with a dot and keeps the other rows aligned', async () => {
    mocks.getConnectedAccounts.mockResolvedValue([connected()])
    renderMenu()
    await openMenu()

    const rows = await screen.findAllByRole('menuitem', { name: /@/ })
    expect(rows[0].querySelector('.identity-active-dot')).toBeInTheDocument()
    expect(rows[0]).toHaveAttribute('aria-current', 'true')
    expect(rows[1].querySelector('.identity-active-dot')).toBeNull()
    expect(rows[1].querySelector('.identity-dot-slot')).toBeInTheDocument()
    expect(rows[1]).not.toHaveAttribute('aria-current')
  })

  it('switches to the account clicked and closes the menu', async () => {
    mocks.getConnectedAccounts.mockResolvedValue([connected()])
    renderMenu()
    await openMenu()

    fireEvent.click(await screen.findByRole('menuitem', { name: /support@acme\.com/ }))

    expect(screen.queryByText('Sign out')).not.toBeInTheDocument()
    // The band follows the switch: name, initials and the connected account's second line.
    // Awaited because the menu asks the leave guard first, which settles a microtask later.
    await waitFor(() => expect(screen.getByText('Support')).toBeInTheDocument())
    expect(screen.getByText('support@acme.com · acme.com')).toBeInTheDocument()
    expect(screen.getByText('S')).toBeInTheDocument()
    expect(localStorage.getItem('mail.activeAccount')).toBe('g1')
  })

  // Switching mailbox is a state change, not a navigation, so the composer's router blocker
  // never sees it: an open draft would be left behind silently. The guard is what the menu asks
  // before it changes anything.
  it('leaves the account alone when the leave guard refuses', async () => {
    mocks.getConnectedAccounts.mockResolvedValue([connected()])
    registerLeaveGuard(() => Promise.resolve(false))
    renderMenu()
    await openMenu()

    fireEvent.click(await screen.findByRole('menuitem', { name: /support@acme\.com/ }))

    await waitFor(() => expect(screen.queryByText('Sign out')).not.toBeInTheDocument())
    expect(screen.queryByText('support@acme.com · acme.com')).not.toBeInTheDocument()
    expect(localStorage.getItem('mail.activeAccount')).toBeNull()
  })

  it('switches once the leave guard allows it', async () => {
    mocks.getConnectedAccounts.mockResolvedValue([connected()])
    registerLeaveGuard(() => Promise.resolve(true))
    renderMenu()
    await openMenu()

    fireEvent.click(await screen.findByRole('menuitem', { name: /support@acme\.com/ }))

    await waitFor(() => expect(localStorage.getItem('mail.activeAccount')).toBe('g1'))
  })

  // A shared mailbox carries no external domain; the band still has to say where it lives.
  it('falls back to Weesky on the band when the account has no domain', async () => {
    mocks.getConnectedAccounts.mockResolvedValue([connected({ domainName: null })])
    renderMenu()
    await openMenu()

    fireEvent.click(await screen.findByRole('menuitem', { name: /support@acme\.com/ }))

    await waitFor(() => expect(screen.getByText('support@acme.com · Weesky')).toBeInTheDocument())
  })

  // switchAccount refuses such a target, so a row that looked like a switch would do nothing.
  it('sends a row with broken credentials to the linked accounts page instead of switching', async () => {
    mocks.getConnectedAccounts.mockResolvedValue([connected({ credentialsValid: false })])
    renderMenu()
    await openMenu()

    const row = await screen.findByRole('menuitem', { name: /support@acme\.com/ })
    expect(row).toHaveTextContent('Password needed')

    fireEvent.click(row)

    expect(path()).toBe('/settings/accounts')
    expect(localStorage.getItem('mail.activeAccount')).toBeNull()
    expect(screen.getByText('mick@weesky.be')).toBeInTheDocument()
  })

  it('navigates to the linked accounts page from Connected accounts…', async () => {
    renderMenu()
    await openMenu()

    fireEvent.click(screen.getByText('Connected accounts…'))

    expect(path()).toBe('/settings/accounts')
    expect(screen.queryByText('Sign out')).not.toBeInTheDocument()
  })

  // activeAccount is null until the list resolves; painting the primary as active would offer a
  // switch onto the mailbox the user is not in.
  it('paints no active row while the account list is still loading', async () => {
    localStorage.setItem('mail.activeAccount', 'g1')
    mocks.getConnectedAccounts.mockReturnValue(new Promise(() => {}))
    renderMenu()
    await openMenu()

    const rows = screen.getAllByRole('menuitem', { name: /@/ })
    expect(rows).toHaveLength(1)
    expect(rows[0].className).not.toContain('is-active')
  })
})
