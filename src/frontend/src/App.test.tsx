import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { AuthProvider } from './contexts/AuthContext'
import { ThemeProvider } from './contexts/ThemeContext'
import { routes } from './routes'

const mocks = vi.hoisted(() => ({
  getAccount: vi.fn(),
  logout: vi.fn(),
  hasSession: vi.fn(),
  clearSession: vi.fn(),
  setUnauthorizedHandler: vi.fn(),
  setIsAdmin: vi.fn(),
  login: vi.fn(),
  markLoggedIn: vi.fn(),
}))

vi.mock('./api.js', () => ({
  api: { getAccount: mocks.getAccount, logout: mocks.logout, login: mocks.login },
  hasSession: mocks.hasSession,
  clearSession: mocks.clearSession,
  markLoggedIn: mocks.markLoggedIn,
  setUnauthorizedHandler: mocks.setUnauthorizedHandler,
  setIsAdmin: mocks.setIsAdmin,
}))

function renderAt(path: string) {
  const router = createMemoryRouter(routes, { initialEntries: [path] })
  render(
    <ThemeProvider>
      <AuthProvider>
        <RouterProvider router={router} />
      </AuthProvider>
    </ThemeProvider>
  )
  return router
}

const account = {
  userName: 'mick', mailbox: 'WSY', fullName: 'Mick', isAdmin: false,
  domains: [{ id: 'WSY', name: 'weesky.be' }],
}

describe('routing', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getAccount.mockResolvedValue(account)
  })

  it('redirects unauthenticated users to /login', async () => {
    mocks.hasSession.mockReturnValue(false)
    const router = renderAt('/mail')
    await waitFor(() => expect(router.state.location.pathname).toBe('/login'))
  })

  it('redirects / to /mail when authenticated', async () => {
    mocks.hasSession.mockReturnValue(true)
    const router = renderAt('/')
    await waitFor(() => expect(router.state.location.pathname).toBe('/mail'))
    expect(await screen.findByText(/coming soon/i)).toBeInTheDocument()
  })

  it('renders the rail with the four module links', async () => {
    mocks.hasSession.mockReturnValue(true)
    renderAt('/mail')
    expect(await screen.findByLabelText('Mail')).toBeInTheDocument()
    expect(screen.getByLabelText('Calendar')).toBeInTheDocument()
    expect(screen.getByLabelText('Contacts')).toBeInTheDocument()
    expect(screen.getByLabelText('Settings')).toBeInTheDocument()
  })

  it('unknown paths fall back to /mail', async () => {
    mocks.hasSession.mockReturnValue(true)
    const router = renderAt('/nope')
    await waitFor(() => expect(router.state.location.pathname).toBe('/mail'))
  })

  it('redirects authenticated users away from /login', async () => {
    mocks.hasSession.mockReturnValue(true)
    const router = renderAt('/login')
    await waitFor(() => expect(router.state.location.pathname).toBe('/mail'))
  })

  it('logs in through the form and navigates to /mail', async () => {
    mocks.hasSession.mockReturnValue(false)
    mocks.login.mockResolvedValue(undefined)
    mocks.markLoggedIn.mockImplementation(() => {
      mocks.hasSession.mockReturnValue(true)
    })
    const user = userEvent.setup()
    const router = renderAt('/login')

    await user.type(await screen.findByPlaceholderText('Email address'), 'mick@weesky.be')
    await user.type(screen.getByPlaceholderText('Password'), 'hunter2')
    await user.click(screen.getByRole('button', { name: /sign in/i }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/mail'))
  })
})
