// Replaces AvatarMenu.test.tsx: the account block moved from the topbar to the foot of the
// folder column. Same behaviours, minus the Settings entry — the rail's gear owns that route.
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthProvider } from '../contexts/AuthContext'
import IdentityMenu from './IdentityMenu'

const mocks = vi.hoisted(() => ({
  getAccount: vi.fn(),
  logout: vi.fn(),
  hasSession: vi.fn(() => true),
  clearSession: vi.fn(),
  setUnauthorizedHandler: vi.fn(),
  setIsAdmin: vi.fn(),
}))

vi.mock('../api.js', () => ({
  api: { getAccount: mocks.getAccount, logout: mocks.logout },
  hasSession: mocks.hasSession,
  clearSession: mocks.clearSession,
  setUnauthorizedHandler: mocks.setUnauthorizedHandler,
  setIsAdmin: mocks.setIsAdmin,
}))

function renderMenu() {
  return render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter>
        <AuthProvider><IdentityMenu /></AuthProvider>
      </MemoryRouter>
    </QueryClientProvider>
  )
}

const toggle = () => screen.getByRole('button', { name: /account menu/i })

describe('IdentityMenu', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.hasSession.mockReturnValue(true)
    mocks.logout.mockResolvedValue(null)
    mocks.getAccount.mockResolvedValue({
      userName: 'mick', mailbox: 'WSY', fullName: 'Mick', isAdmin: false,
      domains: [{ id: 'WSY', name: 'weesky.be' }],
    })
  })

  // The identity is on the band itself, not behind the toggle: that is the point of the move.
  it('shows the identity without opening anything', async () => {
    renderMenu()

    expect(await screen.findByText('mick@weesky.be')).toBeInTheDocument()
    expect(screen.getByText('MW')).toBeInTheDocument()
    expect(screen.queryByText('Sign out')).not.toBeInTheDocument()
  })

  it('opens on the chevron, listing the accounts and Sign out', async () => {
    renderMenu()
    fireEvent.click(await screen.findByRole('button', { name: /account menu/i }))

    // Once on the band, once in the menu's account list.
    expect(screen.getAllByText('mick@weesky.be')).toHaveLength(2)
    expect(screen.getAllByRole('menuitem').length).toBeGreaterThanOrEqual(1)
    expect(screen.getByText('Sign out')).toBeInTheDocument()
  })

  it('signs out via the auth context', async () => {
    renderMenu()
    fireEvent.click(await screen.findByRole('button', { name: /account menu/i }))

    fireEvent.click(screen.getByText('Sign out'))

    await waitFor(() => expect(mocks.logout).toHaveBeenCalled())
    expect(mocks.clearSession).toHaveBeenCalled()
  })

  it('closes on outside mousedown', async () => {
    renderMenu()
    fireEvent.click(await screen.findByRole('button', { name: /account menu/i }))
    expect(screen.getByText('Sign out')).toBeInTheDocument()

    fireEvent.mouseDown(document.body)

    expect(screen.queryByText('Sign out')).not.toBeInTheDocument()
  })

  it('closes on Escape', async () => {
    renderMenu()
    fireEvent.click(await screen.findByRole('button', { name: /account menu/i }))

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
})
