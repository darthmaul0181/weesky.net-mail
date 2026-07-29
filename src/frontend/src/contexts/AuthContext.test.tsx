import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, waitFor, fireEvent, act } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthProvider, useAuth } from './AuthContext'

import { useAccountId } from '../hooks/useAccountId'
import { useWebAppManifest } from '../hooks/useWebAppManifest'

const mocks = vi.hoisted(() => ({
  getAccount: vi.fn(),
  logout: vi.fn(),
  getAppSettings: vi.fn(),
  getConnectedAccounts: vi.fn(),
  hasSession: vi.fn(),
  clearSession: vi.fn(),
  setUnauthorizedHandler: vi.fn(),
  setIsAdmin: vi.fn(),
}))

vi.mock('../api.js', () => ({
  api: {
    getAccount: mocks.getAccount, logout: mocks.logout, getAppSettings: mocks.getAppSettings,
    getConnectedAccounts: mocks.getConnectedAccounts,
  },
  hasSession: mocks.hasSession,
  clearSession: mocks.clearSession,
  setUnauthorizedHandler: mocks.setUnauthorizedHandler,
  setIsAdmin: mocks.setIsAdmin,
}))

function Probe() {
  const {
    isLoggedIn, isAdmin, identity, accountLoaded, accounts, activeAccount, accountsLoading,
    logout, switchAccount,
  } = useAuth()
  return (
    <div>
      <span data-testid="logged">{String(isLoggedIn)}</span>
      <span data-testid="admin">{String(isAdmin)}</span>
      <span data-testid="loaded">{String(accountLoaded)}</span>
      <span data-testid="email">{identity?.email ?? ''}</span>
      <span data-testid="active">{activeAccount?.id ?? ''}</span>
      <span data-testid="scope">{useAccountId()}</span>
      <span data-testid="accounts-loading">{String(accountsLoading)}</span>
      <span data-testid="accounts">
        {accounts.map(a => `${a.id}:${a.email}:${a.displayName}`).join('|')}
      </span>
      <button onClick={() => logout()}>out</button>
      <button onClick={() => switchAccount('acct-1')}>go-1</button>
      <button onClick={() => switchAccount('acct-2')}>go-2</button>
      <button onClick={() => switchAccount('ghost')}>go-ghost</button>
      <button onClick={() => switchAccount('primary')}>go-primary</button>
    </div>
  )
}

const account = {
  userName: 'mick', mailbox: 'WSY', fullName: 'Mick', isAdmin: true,
  domains: [{ id: 'WSY', name: 'weesky.be' }],
}

const connected = [
  {
    id: 'acct-1', email: 'work@corp.example', displayName: 'Work', domainId: 'D1',
    domainName: 'corp.example', sieveSupported: true, credentialsValid: true,
    creationDate: '2026-01-01T00:00:00Z',
  },
  {
    id: 'acct-2', email: 'old@corp.example', displayName: '', domainId: 'D1',
    domainName: 'corp.example', sieveSupported: false, credentialsValid: false,
    creationDate: '2026-01-02T00:00:00Z',
  },
]

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
    localStorage.clear()
    mocks.getAccount.mockResolvedValue(account)
    mocks.logout.mockResolvedValue(null)
    mocks.getAppSettings.mockResolvedValue(appSettings)
    mocks.getConnectedAccounts.mockResolvedValue(connected)
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
  it('forgets every account\'s new-mail claim on logout', async () => {
    mocks.hasSession.mockReturnValue(true)
    renderProbe()
    await waitFor(() => expect(screen.getByTestId('loaded')).toHaveTextContent('true'))
    const claim = JSON.stringify({ uidValidity: 1, uidNext: 9 })
    localStorage.setItem('mail.lastNotifiedUidNext', claim)
    localStorage.setItem('mail.lastNotifiedUidNext.primary', claim)
    localStorage.setItem('mail.lastNotifiedUidNext.acct-1', claim)

    fireEvent.click(screen.getByText('out'))

    await waitFor(() => expect(localStorage.getItem('mail.lastNotifiedUidNext')).toBeNull())
    expect(localStorage.getItem('mail.lastNotifiedUidNext.primary')).toBeNull()
    expect(localStorage.getItem('mail.lastNotifiedUidNext.acct-1')).toBeNull()
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

  describe('linked accounts', () => {
    async function renderLoaded() {
      mocks.hasSession.mockReturnValue(true)
      const result = renderProbe()
      await waitFor(() => expect(screen.getByTestId('accounts')).toHaveTextContent('acct-1'))
      return result
    }

    it('lists the primary account ahead of the connected ones', async () => {
      await renderLoaded()

      expect(screen.getByTestId('accounts')).toHaveTextContent(
        'primary:mick@weesky.be:Mick|acct-1:work@corp.example:Work'
        + '|acct-2:old@corp.example:old@corp.example',
      )
      expect(screen.getByTestId('active')).toHaveTextContent('primary')
    })

    it('switches, persists the choice and re-reads it on the next mount', async () => {
      const { unmount } = await renderLoaded()

      fireEvent.click(screen.getByText('go-1'))

      await waitFor(() => expect(screen.getByTestId('active')).toHaveTextContent('acct-1'))
      expect(localStorage.getItem('mail.activeAccount')).toBe('acct-1')

      unmount()
      renderProbe()

      await waitFor(() => expect(screen.getByTestId('active')).toHaveTextContent('acct-1'))
    })

    it('drops the previous account\'s mail queries on a switch', async () => {
      await renderLoaded()
      client.setQueryData(['mail', 'primary', 'folders'], [{ path: 'INBOX' }])
      const removeQueries = vi.spyOn(client, 'removeQueries')

      fireEvent.click(screen.getByText('go-1'))

      await waitFor(() => expect(screen.getByTestId('active')).toHaveTextContent('acct-1'))
      expect(removeQueries).toHaveBeenCalledWith({ queryKey: ['mail', 'primary'] })
      expect(client.getQueryData(['mail', 'primary', 'folders'])).toBeUndefined()
    })

    // A stored password that no longer decrypts has to be re-entered in settings; landing in the
    // mailbox would only show a wall of errors.
    it('refuses a target whose credentials no longer work', async () => {
      await renderLoaded()

      fireEvent.click(screen.getByText('go-2'))

      await waitFor(() => expect(screen.getByTestId('active')).toHaveTextContent('primary'))
      expect(localStorage.getItem('mail.activeAccount')).toBeNull()
    })

    it.each([['go-ghost'], ['go-primary']])('ignores a %s switch', async (label) => {
      await renderLoaded()
      const removeQueries = vi.spyOn(client, 'removeQueries')

      fireEvent.click(screen.getByText(label))

      await waitFor(() => expect(screen.getByTestId('active')).toHaveTextContent('primary'))
      expect(removeQueries).not.toHaveBeenCalled()
      expect(localStorage.getItem('mail.activeAccount')).toBeNull()
    })

    // The fallback runs on a loaded list only. Run while the query is in flight, a reload on a
    // connected account would clear the stored id and flash the primary mailbox.
    it('keeps a persisted account through the load instead of flashing the primary', async () => {
      localStorage.setItem('mail.activeAccount', 'acct-1')
      mocks.hasSession.mockReturnValue(true)
      let deliver: (rows: unknown[]) => void = () => {}
      mocks.getConnectedAccounts.mockReturnValue(new Promise(resolve => { deliver = resolve }))

      renderProbe()
      await waitFor(() => expect(screen.getByTestId('loaded')).toHaveTextContent('true'))
      expect(localStorage.getItem('mail.activeAccount')).toBe('acct-1')

      await act(async () => { deliver(connected) })

      await waitFor(() => expect(screen.getByTestId('active')).toHaveTextContent('acct-1'))
      expect(localStorage.getItem('mail.activeAccount')).toBe('acct-1')
    })

    // The id is known synchronously from storage; only its metadata needs the fetch. Scoping the
    // query keys on the resolved account instead would send real requests to the primary mailbox
    // for the whole load window.
    it('scopes the queries on the persisted account from the first render', async () => {
      localStorage.setItem('mail.activeAccount', 'acct-1')
      mocks.hasSession.mockReturnValue(true)
      mocks.getConnectedAccounts.mockReturnValue(new Promise(() => {}))

      renderProbe()

      expect(screen.getByTestId('scope')).toHaveTextContent('acct-1')
      expect(screen.getByTestId('accounts-loading')).toHaveTextContent('true')
    })

    // The metadata is genuinely unknown until the list lands, and claiming it is the primary's
    // marks the primary row as the active one. Task 13 makes that row clickable, where the mark
    // would move the user off their own mailbox on a click meant to change nothing.
    it('leaves the active account unresolved while the list loads', async () => {
      localStorage.setItem('mail.activeAccount', 'acct-1')
      mocks.hasSession.mockReturnValue(true)
      mocks.getConnectedAccounts.mockReturnValue(new Promise(() => {}))

      renderProbe()
      await waitFor(() => expect(screen.getByTestId('loaded')).toHaveTextContent('true'))

      expect(screen.getByTestId('active')).toBeEmptyDOMElement()
      expect(screen.getByTestId('scope')).toHaveTextContent('acct-1')
    })

    it('reports the accounts as loading until the list lands', async () => {
      mocks.hasSession.mockReturnValue(true)
      let deliver: (rows: unknown[]) => void = () => {}
      mocks.getConnectedAccounts.mockReturnValue(new Promise(resolve => { deliver = resolve }))

      renderProbe()
      expect(screen.getByTestId('accounts-loading')).toHaveTextContent('true')

      await act(async () => { deliver(connected) })

      await waitFor(() =>
        expect(screen.getByTestId('accounts-loading')).toHaveTextContent('false'))
    })

    it('falls back to the primary once a loaded list no longer holds the stored id', async () => {
      localStorage.setItem('mail.activeAccount', 'gone')

      await renderLoaded()

      await waitFor(() => expect(localStorage.getItem('mail.activeAccount')).toBeNull())
      expect(screen.getByTestId('active')).toHaveTextContent('primary')
    })

    it('falls back once a settled list turns out to be empty', async () => {
      localStorage.setItem('mail.activeAccount', 'acct-1')
      mocks.hasSession.mockReturnValue(true)
      mocks.getConnectedAccounts.mockResolvedValue([])

      renderProbe()

      await waitFor(() => expect(localStorage.getItem('mail.activeAccount')).toBeNull())
      expect(screen.getByTestId('scope')).toHaveTextContent('primary')
    })

    // The click path is guarded by switchAccount; the reload path reaches the same broken mailbox
    // unless the fallback checks the credentials too — the row is still there, it just no longer
    // works, and every folder and message request would come back failing.
    it('falls back when the stored account\'s credentials went invalid', async () => {
      localStorage.setItem('mail.activeAccount', 'acct-2')

      await renderLoaded()

      await waitFor(() => expect(localStorage.getItem('mail.activeAccount')).toBeNull())
      expect(screen.getByTestId('scope')).toHaveTextContent('primary')
      expect(screen.getByTestId('active')).toHaveTextContent('primary')
    })

    it('clears the persisted account when the session ends', async () => {
      await renderLoaded()
      fireEvent.click(screen.getByText('go-1'))
      await waitFor(() => expect(screen.getByTestId('active')).toHaveTextContent('acct-1'))

      fireEvent.click(screen.getByText('out'))

      await waitFor(() => expect(screen.getByTestId('logged')).toHaveTextContent('false'))
      expect(localStorage.getItem('mail.activeAccount')).toBeNull()
      expect(screen.getByTestId('active')).toBeEmptyDOMElement()
    })
  })
})
