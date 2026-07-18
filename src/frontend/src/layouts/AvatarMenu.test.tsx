// src/layouts/AvatarMenu.test.tsx
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { AuthProvider } from '../contexts/AuthContext'
import AvatarMenu from './AvatarMenu'

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
    <MemoryRouter>
      <AuthProvider><AvatarMenu /></AuthProvider>
    </MemoryRouter>
  )
}

describe('AvatarMenu', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.hasSession.mockReturnValue(true)
    mocks.logout.mockResolvedValue(null)
    mocks.getAccount.mockResolvedValue({
      userName: 'mick', mailbox: 'WSY', fullName: 'Mick', isAdmin: false,
      domains: [{ id: 'WSY', name: 'weesky.be' }],
    })
  })

  it('shows the user initials on the trigger', async () => {
    renderMenu()
    expect(await screen.findByRole('button', { name: /account menu/i })).toHaveTextContent('MW')
  })

  it('opens on click, showing identity, accounts and actions', async () => {
    renderMenu()
    fireEvent.click(await screen.findByRole('button', { name: /account menu/i }))
    expect(screen.getAllByText('mick@weesky.be')).toHaveLength(2)
    expect(screen.getAllByRole('menuitem').length).toBeGreaterThanOrEqual(1)
    expect(screen.getByText('Settings')).toBeInTheDocument()
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
})
