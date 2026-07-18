import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent, act } from '@testing-library/react'
import { AuthProvider, useAuth } from './AuthContext'

const mocks = vi.hoisted(() => ({
  getAccount: vi.fn(),
  logout: vi.fn(),
  hasSession: vi.fn(),
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

function Probe() {
  const { isLoggedIn, isAdmin, identity, accountLoaded, logout } = useAuth()
  return (
    <div>
      <span data-testid="logged">{String(isLoggedIn)}</span>
      <span data-testid="admin">{String(isAdmin)}</span>
      <span data-testid="loaded">{String(accountLoaded)}</span>
      <span data-testid="email">{identity?.email ?? ''}</span>
      <button onClick={() => logout()}>out</button>
    </div>
  )
}

const account = {
  userName: 'mick', mailbox: 'WSY', fullName: 'Mick', isAdmin: true,
  domains: [{ id: 'WSY', name: 'weesky.be' }],
}

describe('AuthContext', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getAccount.mockResolvedValue(account)
    mocks.logout.mockResolvedValue(null)
  })

  it('loads the account when a session exists', async () => {
    mocks.hasSession.mockReturnValue(true)
    render(<AuthProvider><Probe /></AuthProvider>)
    expect(screen.getByTestId('logged')).toHaveTextContent('true')
    await waitFor(() => expect(screen.getByTestId('loaded')).toHaveTextContent('true'))
    expect(screen.getByTestId('admin')).toHaveTextContent('true')
    expect(screen.getByTestId('email')).toHaveTextContent('mick@weesky.be')
    expect(mocks.setIsAdmin).toHaveBeenCalledWith(true)
  })

  it('does not load the account without a session', () => {
    mocks.hasSession.mockReturnValue(false)
    render(<AuthProvider><Probe /></AuthProvider>)
    expect(screen.getByTestId('logged')).toHaveTextContent('false')
    expect(mocks.getAccount).not.toHaveBeenCalled()
  })

  it('registers an unauthorized handler that logs out the UI', async () => {
    mocks.hasSession.mockReturnValue(true)
    render(<AuthProvider><Probe /></AuthProvider>)
    await waitFor(() => expect(screen.getByTestId('loaded')).toHaveTextContent('true'))
    const handler = mocks.setUnauthorizedHandler.mock.calls[0][0]
    expect(typeof handler).toBe('function')

    act(() => { handler() })

    await waitFor(() => expect(screen.getByTestId('logged')).toHaveTextContent('false'))
    expect(screen.getByTestId('loaded')).toHaveTextContent('false')
  })

  it('logout calls the API, clears the session, resets state', async () => {
    mocks.hasSession.mockReturnValue(true)
    render(<AuthProvider><Probe /></AuthProvider>)
    await waitFor(() => expect(screen.getByTestId('loaded')).toHaveTextContent('true'))
    fireEvent.click(screen.getByText('out'))
    await waitFor(() => expect(screen.getByTestId('logged')).toHaveTextContent('false'))
    expect(mocks.logout).toHaveBeenCalled()
    expect(mocks.clearSession).toHaveBeenCalled()
  })
})
