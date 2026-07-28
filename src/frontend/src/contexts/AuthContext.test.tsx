import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, waitFor, fireEvent, act } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthProvider, useAuth } from './AuthContext'

import { useWebAppManifest } from '../hooks/useWebAppManifest'

const mocks = vi.hoisted(() => ({
  getAccount: vi.fn(),
  logout: vi.fn(),
  getAppSettings: vi.fn(),
  hasSession: vi.fn(),
  clearSession: vi.fn(),
  setUnauthorizedHandler: vi.fn(),
  setIsAdmin: vi.fn(),
}))

vi.mock('../api.js', () => ({
  api: {
    getAccount: mocks.getAccount, logout: mocks.logout, getAppSettings: mocks.getAppSettings,
  },
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

const appSettings = {
  'app.installable': 'true', 'app.name': 'Snoopy mail', 'app.shortName': 'Snoopy',
}

/** App.tsx's InstallManifest: mounted beside the provider, above the router, never unmounted. */
function ManifestProbe() {
  useWebAppManifest()
  return null
}

function manifestLink() {
  return document.head.querySelector('link[rel="manifest"]')
}

describe('AuthContext', () => {
  let client: QueryClient

  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getAccount.mockResolvedValue(account)
    mocks.logout.mockResolvedValue(null)
    mocks.getAppSettings.mockResolvedValue(appSettings)
    client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    // jsdom implements neither; installed here and removed afterwards rather than globally.
    URL.createObjectURL = vi.fn(() => 'blob:mock')
    URL.revokeObjectURL = vi.fn()
  })

  afterEach(() => {
    manifestLink()?.remove()
    delete (URL as Partial<typeof URL>).createObjectURL
    delete (URL as Partial<typeof URL>).revokeObjectURL
  })

  function renderProbe() {
    return render(
      <QueryClientProvider client={client}>
        <AuthProvider><Probe /></AuthProvider>
      </QueryClientProvider>,
    )
  }

  it('loads the account when a session exists', async () => {
    mocks.hasSession.mockReturnValue(true)
    renderProbe()
    expect(screen.getByTestId('logged')).toHaveTextContent('true')
    await waitFor(() => expect(screen.getByTestId('loaded')).toHaveTextContent('true'))
    expect(screen.getByTestId('admin')).toHaveTextContent('true')
    expect(screen.getByTestId('email')).toHaveTextContent('mick@weesky.be')
    expect(mocks.setIsAdmin).toHaveBeenCalledWith(true)
  })

  it('does not load the account without a session', () => {
    mocks.hasSession.mockReturnValue(false)
    renderProbe()
    expect(screen.getByTestId('logged')).toHaveTextContent('false')
    expect(mocks.getAccount).not.toHaveBeenCalled()
  })

  it('registers an unauthorized handler that logs out the UI', async () => {
    mocks.hasSession.mockReturnValue(true)
    renderProbe()
    await waitFor(() => expect(screen.getByTestId('loaded')).toHaveTextContent('true'))
    const handler = mocks.setUnauthorizedHandler.mock.calls[0][0]
    expect(typeof handler).toBe('function')

    act(() => { handler() })

    await waitFor(() => expect(screen.getByTestId('logged')).toHaveTextContent('false'))
    expect(screen.getByTestId('loaded')).toHaveTextContent('false')
  })

  it('logout calls the API, clears the session, resets state', async () => {
    mocks.hasSession.mockReturnValue(true)
    renderProbe()
    await waitFor(() => expect(screen.getByTestId('loaded')).toHaveTextContent('true'))
    fireEvent.click(screen.getByText('out'))
    await waitFor(() => expect(screen.getByTestId('logged')).toHaveTextContent('false'))
    expect(mocks.logout).toHaveBeenCalled()
    expect(mocks.clearSession).toHaveBeenCalled()
  })

  // The keys are account-scoped in shape only — the id is the constant 'primary' until linked
  // accounts ship — so anything left behind is served to whoever signs in next.
  it('empties the query cache on logout', async () => {
    mocks.hasSession.mockReturnValue(true)
    renderProbe()
    await waitFor(() => expect(screen.getByTestId('loaded')).toHaveTextContent('true'))
    client.setQueryData(['mail', 'primary', 'folders'], [{ path: 'INBOX' }])
    client.setQueryData(['contacts', 'primary'], [{ id: 'c1' }])
    client.setQueryData(['preferences'], { 'mail.pageSize': '50' })

    fireEvent.click(screen.getByText('out'))

    await waitFor(() => expect(screen.getByTestId('logged')).toHaveTextContent('false'))
    expect(client.getQueryData(['mail', 'primary', 'folders'])).toBeUndefined()
    expect(client.getQueryData(['contacts', 'primary'])).toBeUndefined()
    expect(client.getQueryData(['preferences'])).toBeUndefined()
  })

  // localStorage outlives the tab, so this one is not covered by clearing the query cache.
  it('forgets the new-mail claim on logout', async () => {
    mocks.hasSession.mockReturnValue(true)
    renderProbe()
    await waitFor(() => expect(screen.getByTestId('loaded')).toHaveTextContent('true'))
    localStorage.setItem('mail.lastNotifiedUidNext', JSON.stringify({ uidValidity: 1, uidNext: 9 }))

    fireEvent.click(screen.getByText('out'))

    await waitFor(() => expect(localStorage.getItem('mail.lastNotifiedUidNext')).toBeNull())
  })

  // Flushing is for a session *ending*. On a logged-out first mount nothing is cached to flush,
  // and clearing regardless destroyed the in-flight queries that siblings mounted above the router
  // had already started — which is how /login lost the install manifest entirely.
  it('leaves the query cache alone on a logged-out first mount', async () => {
    mocks.hasSession.mockReturnValue(false)
    client.setQueryData(['appSettings'], { 'app.installable': 'true' })

    renderProbe()

    await waitFor(() => expect(screen.getByTestId('logged')).toHaveTextContent('false'))
    expect(client.getQueryData(['appSettings'])).toEqual({ 'app.installable': 'true' })
  })

  // The install manifest is the one reader mounted above the router, so it is still observing
  // ['appSettings'] when a session ends. Removing that query from the cache — which is what
  // clear() does — detaches the observer silently: the link stays posted and no later change can
  // withdraw or replace it until the page is reloaded.
  it('leaves the install manifest tracking the settings after a session ends', async () => {
    mocks.hasSession.mockReturnValue(true)
    render(
      <QueryClientProvider client={client}>
        <ManifestProbe />
        <AuthProvider><Probe /></AuthProvider>
      </QueryClientProvider>,
    )
    await waitFor(() => expect(manifestLink()).not.toBeNull())

    fireEvent.click(screen.getByText('out'))
    await waitFor(() => expect(screen.getByTestId('logged')).toHaveTextContent('false'))
    // The settings are instance-wide and read anonymously, so the still-mounted reader refetches
    // them and the link comes straight back.
    await waitFor(() => expect(mocks.getAppSettings).toHaveBeenCalledTimes(2))
    expect(manifestLink()).not.toBeNull()

    act(() => {
      client.setQueryData(['appSettings'], { ...appSettings, 'app.installable': 'false' })
    })

    await waitFor(() => expect(manifestLink()).toBeNull())
  })

  it('empties the query cache when a 401 ends the session', async () => {
    mocks.hasSession.mockReturnValue(true)
    renderProbe()
    await waitFor(() => expect(screen.getByTestId('loaded')).toHaveTextContent('true'))
    client.setQueryData(['mail', 'primary', 'folders'], [{ path: 'INBOX' }])
    const handler = mocks.setUnauthorizedHandler.mock.calls[0][0]

    act(() => { handler() })

    await waitFor(() => expect(client.getQueryData(['mail', 'primary', 'folders'])).toBeUndefined())
  })
})
