import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { StrictMode } from 'react'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import App from './App'
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
  getMailFolders: vi.fn(),
  getAppSettings: vi.fn(),
}))

vi.mock('./api.js', () => ({
  api: {
    getAccount: mocks.getAccount,
    logout: mocks.logout,
    login: mocks.login,
    getMailFolders: mocks.getMailFolders,
    getAppSettings: mocks.getAppSettings,
  },
  hasSession: mocks.hasSession,
  clearSession: mocks.clearSession,
  markLoggedIn: mocks.markLoggedIn,
  setUnauthorizedHandler: mocks.setUnauthorizedHandler,
  setIsAdmin: mocks.setIsAdmin,
}))

function renderAt(path: string) {
  const router = createMemoryRouter(routes, { initialEntries: [path] })
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={queryClient}>
      <ThemeProvider>
        <AuthProvider>
          <RouterProvider router={router} />
        </AuthProvider>
      </ThemeProvider>
    </QueryClientProvider>
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
    mocks.getMailFolders.mockResolvedValue([])
    mocks.getAppSettings.mockResolvedValue({})
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
    // Replaces the old "coming soon" assertion: /mail now renders the mail module.
    expect(await screen.findByRole('navigation', { name: 'Folders' })).toBeInTheDocument()
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

// Mounts the real <App/> rather than a hand-rebuilt provider tree. Every other test in this file
// rebuilds the composition, which is precisely how a defect in the composition itself stayed
// invisible: InstallManifest starts the app-settings fetch, and AuthProvider's mount effect used
// to answer a logged-out visitor by clearing the whole query cache out from under it, so the
// manifest was never posted on the one page it exists to cover.
describe('App composition', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getAccount.mockResolvedValue(account)
    mocks.getMailFolders.mockResolvedValue([])
    mocks.getAppSettings.mockResolvedValue({
      'app.installable': 'true',
      'app.name': 'Snoopy mail',
      'app.shortName': 'Snoopy',
    })
    URL.createObjectURL = vi.fn(() => 'blob:mock')
    URL.revokeObjectURL = vi.fn()
  })
  afterEach(() => {
    document.head.querySelector('link[rel="manifest"]')?.remove()
    delete (URL as Partial<typeof URL>).createObjectURL
    delete (URL as Partial<typeof URL>).revokeObjectURL
  })

  // Logged out on purpose: /login is the first page a new user sees, so it is where installation
  // has to be offered. Waiting for the login form first proves the visitor really is on it.
  // Wrapped in StrictMode as main.tsx wraps it, so the double-invoked mount effects are part of
  // what is under test — the guard added to AuthProvider is spent on a transition rather than on a
  // first run precisely so the second pass cannot clear the cache for real.
  it('posts the manifest link for a logged-out visitor', async () => {
    mocks.hasSession.mockReturnValue(false)

    render(<StrictMode><App /></StrictMode>)

    expect(await screen.findByPlaceholderText('Email address')).toBeInTheDocument()
    await waitFor(() =>
      expect(document.head.querySelector('link[rel="manifest"]')).not.toBeNull())
  })
})
