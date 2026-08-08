import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, useLocation } from 'react-router-dom'
import ConnectedAccountsPage from './ConnectedAccountsPage'
import { leaveTo } from './useConnectedAccounts'

const mocks = vi.hoisted(() => ({
  getConnectedAccounts: vi.fn(),
  getConnectableDomains: vi.fn(),
  connectAccount: vi.fn(),
  updateConnectedAccountPassword: vi.fn(),
  deleteConnectedAccount: vi.fn(),
  startOAuthConnect: vi.fn(),
  completeOAuthConnect: vi.fn(),
}))
vi.mock('../../../api.js', () => ({ api: mocks }))

// Only the navigation seam is stubbed: jsdom refuses a real top-level assignment, and every hook
// in the module is the real one.
vi.mock('./useConnectedAccounts', async importOriginal => ({
  ...await importOriginal<typeof import('./useConnectedAccounts')>(),
  leaveTo: vi.fn(),
}))

const auth = vi.hoisted(() => ({ activeAccountId: 'primary' }))
vi.mock('../../../contexts/AuthContext', () => ({ useAuth: () => auth }))

/** An ApiError as `request()` throws it: the status is what carries the real cause. */
function apiError(message: string, status: number) {
  return Object.assign(new Error(message), { status })
}

const WORK = {
  id: 'a1', email: 'work@acme.com', displayName: 'Work', domainId: 'd1', domainName: 'Acme',
  sieveSupported: true, credentialsValid: true, creationDate: '2026-03-04T10:00:00Z',
  authMode: 'Password',
}
const SHARED = {
  id: 'b2', email: 'shared@weesky.net', displayName: '', domainId: null, domainName: null,
  sieveSupported: true, credentialsValid: false, creationDate: '2026-05-20T10:00:00Z',
  authMode: 'Password',
}
const OAUTH_ACCOUNT = {
  id: 'c3', email: 'me@outlook.com', displayName: 'Outlook', domainId: 'd2', domainName: 'Outlook',
  sieveSupported: false, credentialsValid: false, creationDate: '2026-06-01T10:00:00Z',
  authMode: 'OAuth2',
}
const LIVE_OAUTH_ACCOUNT = { ...OAUTH_ACCOUNT, id: 'c4', email: 'live@outlook.com', credentialsValid: true }
const ACME = { id: 'd1', name: 'Acme', authMode: 'Password' }
const OUTLOOK = { id: 'd2', name: 'Outlook', authMode: 'OAuth2' }

/** Split from the render so a test can refuse a call the mount effect makes straight away. */
function primeApi(accounts = [WORK, SHARED], domains = [ACME]) {
  mocks.getConnectedAccounts.mockResolvedValue(accounts)
  mocks.getConnectableDomains.mockResolvedValue(domains)
  mocks.connectAccount.mockResolvedValue({})
  mocks.updateConnectedAccountPassword.mockResolvedValue({})
  mocks.deleteConnectedAccount.mockResolvedValue({})
  mocks.startOAuthConnect.mockResolvedValue({ authorizationUrl: 'https://provider/authorize?x=1', state: 's1' })
  mocks.completeOAuthConnect.mockResolvedValue(OAUTH_ACCOUNT)
}

function renderPage(accounts = [WORK, SHARED], domains = [ACME], url = '/settings/accounts') {
  primeApi(accounts, domains)
  return renderAt(url)
}

function renderAt(url: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[url]}>
        <ConnectedAccountsPage />
        <QueryStringProbe />
      </MemoryRouter>
    </QueryClientProvider>)
}

/** MemoryRouter keeps its history off `window`, so the stripped query string is read from here. */
function QueryStringProbe() {
  return <span data-testid="query-string">{useLocation().search}</span>
}

async function openForm() {
  await userEvent.click(await screen.findByRole('button', { name: 'Connect an account' }))
}

describe('ConnectedAccountsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.activeAccountId = 'primary'
  })

  it('renders one tile per connected account, falling back to the address when there is no name', async () => {
    renderPage()

    expect(await screen.findByText('Work')).toBeInTheDocument()
    expect(screen.getByText('shared@weesky.net')).toBeInTheDocument()
  })

  it('names the server and the connection date under the account', async () => {
    renderPage()

    expect(await screen.findByText(/work@acme\.com · Acme · connected on/)).toBeInTheDocument()
    // A mailbox on our own server is named by its own domain, not marked out as a special kind
    // of account: the tile says the same sort of thing for both.
    expect(screen.getByText(/shared@weesky\.net · weesky\.net · connected on/))
      .toBeInTheDocument()
  })

  it('offers the re-enter action and the warning only where the password no longer works', async () => {
    renderPage()
    await screen.findByText('Work')

    expect(screen.getByRole('button', { name: 'Re-enter the password for shared@weesky.net' }))
      .toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Re-enter the password for work@acme.com' }))
      .not.toBeInTheDocument()
    expect(screen.getByText(/Your main password changed/)).toBeInTheDocument()
  })

  it('lists the local mailbox first, then the administrator-defined servers', async () => {
    renderPage()
    await openForm()

    const options = within(await screen.findByLabelText('Server')).getAllByRole('option')

    expect(options.map(option => option.textContent)).toEqual(['Weesky (local)', 'Acme'])
  })

  it('connects a local mailbox with no domain and closes the form', async () => {
    renderPage()
    await openForm()

    await userEvent.type(screen.getByLabelText('Email'), 'other@weesky.net')
    await userEvent.type(screen.getByLabelText('Password'), 'secret')
    await userEvent.click(screen.getByRole('button', { name: 'Connect' }))

    await waitFor(() => expect(mocks.connectAccount)
      .toHaveBeenCalledWith(null, 'other@weesky.net', 'secret'))
    await waitFor(() => expect(screen.queryByLabelText('Email')).not.toBeInTheDocument())
  })

  it('posts the chosen server as the domain id', async () => {
    renderPage()
    await openForm()

    await userEvent.selectOptions(await screen.findByLabelText('Server'), 'd1')
    await userEvent.type(screen.getByLabelText('Email'), 'me@acme.com')
    await userEvent.type(screen.getByLabelText('Password'), 'secret')
    await userEvent.click(screen.getByRole('button', { name: 'Connect' }))

    await waitFor(() => expect(mocks.connectAccount)
      .toHaveBeenCalledWith('d1', 'me@acme.com', 'secret'))
  })

  it('shows a refused connection in the form and keeps it open', async () => {
    renderPage()
    mocks.connectAccount.mockRejectedValue(
      new Error('Could not sign in to this mailbox. Check the address and the password.'))
    await openForm()

    await userEvent.type(screen.getByLabelText('Email'), 'other@weesky.net')
    await userEvent.type(screen.getByLabelText('Password'), 'wrong')
    await userEvent.click(screen.getByRole('button', { name: 'Connect' }))

    expect(await screen.findByRole('alert'))
      .toHaveTextContent('Could not sign in to this mailbox. Check the address and the password.')
    expect(screen.getByLabelText('Email')).toBeInTheDocument()
  })

  it('disconnects through the shared confirmation', async () => {
    renderPage()
    await screen.findByText('Work')

    await userEvent.click(screen.getByRole('button', { name: 'Disconnect work@acme.com' }))
    expect(screen.getByText('Confirm deletion')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Delete' }))

    await waitFor(() => expect(mocks.deleteConnectedAccount).toHaveBeenCalledWith('a1'))
  })

  it('does not disconnect anything until the confirmation is accepted', async () => {
    renderPage()
    await screen.findByText('Work')

    await userEvent.click(screen.getByRole('button', { name: 'Disconnect work@acme.com' }))

    expect(mocks.deleteConnectedAccount).not.toHaveBeenCalled()
  })

  it('saves a re-entered password and refreshes the shared account list', async () => {
    renderPage()
    await screen.findByText('Work')

    await userEvent.click(
      screen.getByRole('button', { name: 'Re-enter the password for shared@weesky.net' }))
    await userEvent.type(screen.getByLabelText('Password'), 'brand-new')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(mocks.updateConnectedAccountPassword)
      .toHaveBeenCalledWith('b2', 'brand-new'))
    // The list is keyed with AuthContext's: the invalidation is what refreshes the switcher.
    await waitFor(() => expect(mocks.getConnectedAccounts).toHaveBeenCalledTimes(2))
  })

  // Server prose never reaches the dialog; the local fallback does — see apiErrorMessage.
  it('keeps the password dialog open on a refusal, with the reason', async () => {
    renderPage()
    mocks.updateConnectedAccountPassword.mockRejectedValue(new Error('The mail server refused'))
    await screen.findByText('Work')

    await userEvent.click(
      screen.getByRole('button', { name: 'Re-enter the password for shared@weesky.net' }))
    await userEvent.type(screen.getByLabelText('Password'), 'still-wrong')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByRole('alert'))
      .toHaveTextContent('Could not sign in to this mailbox. Check the address and the password.')
    expect(screen.getByLabelText('Password')).toBeInTheDocument()
  })

  // The rate limiter answers with no body, so the message-only path would tell a throttled user
  // their password is wrong — and send them straight back into the limit on the retry.
  it('names the rate limit instead of blaming the password', async () => {
    renderPage()
    mocks.connectAccount.mockRejectedValue(apiError('Too Many Requests', 429))
    await openForm()

    await userEvent.type(screen.getByLabelText('Email'), 'other@weesky.net')
    await userEvent.type(screen.getByLabelText('Password'), 'right-password')
    await userEvent.click(screen.getByRole('button', { name: 'Connect' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Too many attempts')
    expect(screen.queryByText(/Check the address and the password/)).not.toBeInTheDocument()
  })

  // A row that vanished between render and submit answers the bare code `account_not_found`.
  it('gives a vanished account a sentence instead of the backend code', async () => {
    renderPage()
    mocks.updateConnectedAccountPassword.mockRejectedValue(apiError('account_not_found', 404))
    await screen.findByText('Work')

    await userEvent.click(
      screen.getByRole('button', { name: 'Re-enter the password for shared@weesky.net' }))
    await userEvent.type(screen.getByLabelText('Password'), 'anything')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('This account is no longer connected.')
    expect(screen.queryByText('account_not_found')).not.toBeInTheDocument()
  })

  it('warns when the account being disconnected is the one being read', async () => {
    auth.activeAccountId = 'a1'
    renderPage()
    await screen.findByText('Work')

    await userEvent.click(screen.getByRole('button', { name: 'Disconnect work@acme.com' }))

    expect(screen.getByText(/You are reading this mailbox right now/)).toBeInTheDocument()
  })

  it('does not warn when disconnecting a mailbox that is not the active one', async () => {
    auth.activeAccountId = 'a1'
    renderPage()
    await screen.findByText('Work')

    await userEvent.click(screen.getByRole('button', { name: 'Disconnect shared@weesky.net' }))

    expect(screen.queryByText(/You are reading this mailbox right now/)).not.toBeInTheDocument()
  })

  it('says so when no mailbox is connected yet', async () => {
    renderPage([])

    expect(await screen.findByText(/No other mailbox is connected/)).toBeInTheDocument()
  })
})

describe('ConnectedAccountsPage — signing in with a provider', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    auth.activeAccountId = 'primary'
  })

  it('drops the password fields for a provider server and offers its sign-in instead', async () => {
    renderPage([WORK], [ACME, OUTLOOK])
    await openForm()

    await userEvent.selectOptions(await screen.findByLabelText('Server'), 'd2')

    expect(screen.getByRole('button', { name: 'Sign in with Outlook' })).toBeInTheDocument()
    expect(screen.queryByLabelText('Email')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Password')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Connect' })).not.toBeInTheDocument()
  })

  it('starts the handshake on that domain and leaves for the provider', async () => {
    renderPage([WORK], [ACME, OUTLOOK])
    await openForm()

    await userEvent.selectOptions(await screen.findByLabelText('Server'), 'd2')
    const button = screen.getByRole('button', { name: 'Sign in with Outlook' })
    await userEvent.click(button)

    await waitFor(() => expect(mocks.startOAuthConnect).toHaveBeenCalledWith({ domainId: 'd2' }))
    expect(vi.mocked(leaveTo)).toHaveBeenCalledWith('https://provider/authorize?x=1')
    // The mutation has already settled; the browser has not left yet. Following isPending here
    // would re-enable the button under the finger and start a second handshake.
    expect(button).toBeDisabled()
  })

  // fetch reports an empty statusText over HTTP/2, so a bodyless refusal carries no message at
  // all — and the shared fallback names a password this user never typed.
  it('does not blame the password when the provider endpoint answers nothing', async () => {
    renderPage([WORK], [ACME, OUTLOOK])
    mocks.startOAuthConnect.mockRejectedValue(apiError('', 502))
    await openForm()

    await userEvent.selectOptions(await screen.findByLabelText('Server'), 'd2')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in with Outlook' }))

    expect(await screen.findByRole('alert'))
      .toHaveTextContent('Could not reach the sign-in provider. Try again in a moment.')
    expect(screen.queryByText(/Check the address and the password/)).not.toBeInTheDocument()
  })

  it('drops a password refusal when the server changes to a provider one', async () => {
    renderPage([WORK], [ACME, OUTLOOK])
    mocks.connectAccount.mockRejectedValue(new Error('Could not sign in to this mailbox.'))
    await openForm()

    await userEvent.type(screen.getByLabelText('Email'), 'me@acme.com')
    await userEvent.type(screen.getByLabelText('Password'), 'wrong')
    await userEvent.click(screen.getByRole('button', { name: 'Connect' }))
    expect(await screen.findByRole('alert')).toBeInTheDocument()

    await userEvent.selectOptions(screen.getByLabelText('Server'), 'd2')

    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  // The panel used to autoFocus the email field; the arrow-key fix must not cost that.
  it('focuses the email field when the connect panel opens', async () => {
    renderPage([WORK], [ACME, OUTLOOK])
    await openForm()

    expect(await screen.findByLabelText('Email')).toHaveFocus()
  })

  // Windows Chrome fires change on every arrow key over a closed select, so a field remounting
  // with autoFocus throws a keyboard user out of the list they are still walking.
  it('leaves the focus on the server select when the fields come back', async () => {
    renderPage([WORK], [ACME, OUTLOOK])
    await openForm()

    const server = await screen.findByLabelText('Server')
    await userEvent.selectOptions(server, 'd2')
    await userEvent.selectOptions(server, 'd1')

    expect(screen.getByLabelText('Email')).toBeInTheDocument()
    expect(server).toHaveFocus()
  })

  it('shows a refused start in the form and stays put', async () => {
    renderPage([WORK], [ACME, OUTLOOK])
    mocks.startOAuthConnect.mockRejectedValue(apiError('Too Many Requests', 429))
    await openForm()

    await userEvent.selectOptions(await screen.findByLabelText('Server'), 'd2')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in with Outlook' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Too many attempts')
    expect(vi.mocked(leaveTo)).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Sign in with Outlook' })).toBeEnabled()
  })

  it('repairs a provider account by reconnecting, and a password one by the key', async () => {
    renderPage([SHARED, OAUTH_ACCOUNT])
    await screen.findByText('Outlook')

    expect(screen.getByRole('button', { name: 'Reconnect me@outlook.com' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Re-enter the password for me@outlook.com' }))
      .not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Re-enter the password for shared@weesky.net' }))
      .toBeInTheDocument()
    expect(screen.getByText('This mailbox needs to be reconnected.')).toBeInTheDocument()
  })

  // credentialsValid says the cipher still opens under the session key, never that the provider
  // still honours the token: a revoked consent leaves the row looking perfectly healthy while the
  // mail module refuses the mailbox, and Disconnect + re-add would throw away its identities.
  it('offers the repair on a provider row the backend still reports as valid', async () => {
    renderPage([LIVE_OAUTH_ACCOUNT])
    await screen.findByText('Outlook')

    await userEvent.click(screen.getByRole('button', { name: 'Reconnect live@outlook.com' }))

    await waitFor(() => expect(mocks.startOAuthConnect).toHaveBeenCalledWith({ accountId: 'c4' }))
    // No warning: nothing is known to be wrong, the door is simply there.
    expect(screen.queryByText('This mailbox needs to be reconnected.')).not.toBeInTheDocument()
  })

  it('restarts the handshake against the row being reconnected', async () => {
    renderPage([OAUTH_ACCOUNT])
    await screen.findByText('Outlook')

    const button = screen.getByRole('button', { name: 'Reconnect me@outlook.com' })
    await userEvent.click(button)

    await waitFor(() => expect(mocks.startOAuthConnect).toHaveBeenCalledWith({ accountId: 'c3' }))
    expect(vi.mocked(leaveTo)).toHaveBeenCalledWith('https://provider/authorize?x=1')
    expect(button).toBeDisabled()
  })

  it('completes the handshake the callback left in the query string', async () => {
    renderPage([WORK], [ACME], '/settings/accounts?oauthState=s1')

    await waitFor(() => expect(mocks.completeOAuthConnect).toHaveBeenCalledWith('s1'))
    expect(await screen.findByText('me@outlook.com is connected')).toBeInTheDocument()
  })

  // Complete's 404 is the handshake, never the account — and a first-time attach has no account
  // that could have been disconnected, so the shared 404 sentence would be plainly untrue.
  it('blames the expired handshake rather than an account that never existed', async () => {
    primeApi([WORK], [ACME])
    mocks.completeOAuthConnect.mockRejectedValue(apiError('account_not_found', 404))
    renderAt('/settings/accounts?oauthState=s1')

    expect(await screen.findByText('That sign-in took too long. Try connecting again.'))
      .toBeInTheDocument()
    expect(screen.queryByText('This account is no longer connected.')).not.toBeInTheDocument()
    expect(screen.queryByText('account_not_found')).not.toBeInTheDocument()
  })

  it('does not blame the password when the completion answers nothing', async () => {
    primeApi([WORK], [ACME])
    mocks.completeOAuthConnect.mockRejectedValue(apiError('', 500))
    renderAt('/settings/accounts?oauthState=s1')

    expect(await screen.findByText('Could not reach the sign-in provider. Try again in a moment.'))
      .toBeInTheDocument()
  })

  it('says the sign-in failed and calls nothing when the callback marks an error', async () => {
    renderPage([WORK], [ACME], '/settings/accounts?oauthError=1')

    expect(await screen.findByText('The sign-in did not complete. Try again.')).toBeInTheDocument()
    expect(mocks.completeOAuthConnect).not.toHaveBeenCalled()
  })

  // A consumed handshake must not be replayed by a refresh, nor its error re-shown by a shared URL.
  it('strips the parameter it resumed from', async () => {
    renderPage([WORK], [ACME], '/settings/accounts?oauthState=s1')

    await waitFor(() => expect(mocks.completeOAuthConnect).toHaveBeenCalledTimes(1))
    expect(screen.getByTestId('query-string')).toHaveTextContent('')
  })
})
