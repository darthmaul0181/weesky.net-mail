import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import ConnectedAccountsPage from './ConnectedAccountsPage'

const mocks = vi.hoisted(() => ({
  getConnectedAccounts: vi.fn(),
  getConnectableDomains: vi.fn(),
  connectAccount: vi.fn(),
  updateConnectedAccountPassword: vi.fn(),
  deleteConnectedAccount: vi.fn(),
}))
vi.mock('../../../api.js', () => ({ api: mocks }))

const auth = vi.hoisted(() => ({ activeAccountId: 'primary' }))
vi.mock('../../../contexts/AuthContext', () => ({ useAuth: () => auth }))

/** An ApiError as `request()` throws it: the status is what carries the real cause. */
function apiError(message: string, status: number) {
  return Object.assign(new Error(message), { status })
}

const WORK = {
  id: 'a1', email: 'work@acme.com', displayName: 'Work', domainId: 'd1', domainName: 'Acme',
  sieveSupported: true, credentialsValid: true, creationDate: '2026-03-04T10:00:00Z',
}
const SHARED = {
  id: 'b2', email: 'shared@weesky.net', displayName: '', domainId: null, domainName: null,
  sieveSupported: true, credentialsValid: false, creationDate: '2026-05-20T10:00:00Z',
}

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

function renderPage(accounts = [WORK, SHARED]) {
  mocks.getConnectedAccounts.mockResolvedValue(accounts)
  mocks.getConnectableDomains.mockResolvedValue([{ id: 'd1', name: 'Acme' }])
  mocks.connectAccount.mockResolvedValue({})
  mocks.updateConnectedAccountPassword.mockResolvedValue({})
  mocks.deleteConnectedAccount.mockResolvedValue({})
  return render(<ConnectedAccountsPage />, { wrapper })
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

  it('keeps the password dialog open on a refusal, with the reason', async () => {
    renderPage()
    mocks.updateConnectedAccountPassword.mockRejectedValue(new Error('The mail server refused'))
    await screen.findByText('Work')

    await userEvent.click(
      screen.getByRole('button', { name: 'Re-enter the password for shared@weesky.net' }))
    await userEvent.type(screen.getByLabelText('Password'), 'still-wrong')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('The mail server refused')
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
