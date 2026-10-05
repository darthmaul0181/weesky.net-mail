import { act, screen, waitFor } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { createTestQueryClient, holdNextCall, settle, setupUser } from '../../../test-utils'
import { AccountsTab } from './AccountsTab'
import { BOB, MB, MOCK_DOMAINS, MOCK_USERS, confirmButton, render } from './adminTestFixtures'

const mocks = vi.hoisted(() => ({
  adminGetUsers: vi.fn(),
  adminCreateUser: vi.fn(),
  adminUpdateUser: vi.fn(),
  adminDeleteUser: vi.fn(),
  adminGetDomains: vi.fn(),
  adminGetUserQuota: vi.fn(),
}))

vi.mock('../../../api.js', () => ({ api: mocks, clearSession: vi.fn() }))

let user: ReturnType<typeof setupUser>
beforeEach(() => {
  vi.clearAllMocks()
  mocks.adminGetUsers.mockResolvedValue(MOCK_USERS)
  mocks.adminGetDomains.mockResolvedValue(MOCK_DOMAINS)
  mocks.adminGetUserQuota.mockRejectedValue(new Error('unavailable'))
  user = setupUser()
})

async function renderLoaded(addToast = vi.fn()) {
  render(<AccountsTab addToast={addToast} />)
  await screen.findByText('alice@weesky.be')
  return addToast
}

describe('AccountsTab', () => {
  // A re-render must not refetch, or the tab would hammer the admin API. settle() is the right
  // tool here, the assertion is that nothing more ran.
  it('loads once and does not reload on a re-render', async () => {
    const addToast = vi.fn()
    const { rerender } = render(<AccountsTab addToast={addToast} />)
    await screen.findByText('alice@weesky.be')

    rerender(<AccountsTab addToast={addToast} />)
    await settle()

    expect(mocks.adminGetUsers).toHaveBeenCalledOnce()
    expect(mocks.adminGetDomains).toHaveBeenCalledOnce()
  })

  it('renders the user list after loading', async () => {
    render(<AccountsTab addToast={vi.fn()} />)
    expect(await screen.findByText('alice@weesky.be')).toBeInTheDocument()
    expect(screen.getByText('Alice Smith')).toBeInTheDocument()
  })

  it.each([['username', 'bob'], ['full name', 'Jones']])('filters the list by %s when searching', async (_by, term) => {
    mocks.adminGetUsers.mockResolvedValue([...MOCK_USERS, BOB])
    await renderLoaded()
    await user.type(screen.getByPlaceholderText('Search…'), term)
    expect(screen.queryByText('alice@weesky.be')).not.toBeInTheDocument()
    expect(screen.getByText('bob@weesky.be')).toBeInTheDocument()
  })

  it('calls adminDeleteUser when delete is confirmed', async () => {
    mocks.adminDeleteUser.mockResolvedValue(null)
    await renderLoaded()
    await user.click(screen.getByTitle('Delete'))
    await user.click(confirmButton())
    await waitFor(() => expect(mocks.adminDeleteUser).toHaveBeenCalledWith(1))
  })

  // Server prose never reaches the toast, nor does its absence: the local fallback does — see
  // apiErrorMessage.
  it.each([['a server message', 'Cannot delete'], ['no message', undefined]])(
    'shows the local fallback when user delete fails with %s', async (_case, message) => {
      mocks.adminDeleteUser.mockRejectedValue(new Error(message))
      const addToast = await renderLoaded()
      await user.click(screen.getByTitle('Delete'))
      await user.click(confirmButton())
      await waitFor(() => expect(addToast).toHaveBeenCalledWith('Failed to delete user', 'error'))
    })

  // A refused write still re-reads the list: the screen ends on server state, never on a guess.
  it('re-reads the accounts after a refused delete', async () => {
    mocks.adminDeleteUser.mockRejectedValue(new Error('Cannot delete'))
    await renderLoaded()
    await user.click(screen.getByTitle('Delete'))
    await user.click(confirmButton())
    await waitFor(() => expect(mocks.adminGetUsers).toHaveBeenCalledTimes(2))
    expect(screen.getByText('alice@weesky.be', { selector: '.admin-list-item-email' })).toBeInTheDocument()
  })

  // Both lists fail: the domains toast names a list nothing on screen shows either, since the
  // users list — this tab's primary — already drew its own failure note. One toast, not two.
  it('shows only the primary toast when both lists fail', async () => {
    mocks.adminGetUsers.mockRejectedValue(new Error('Server error'))
    mocks.adminGetDomains.mockRejectedValue(new Error('Server error'))
    const addToast = vi.fn()
    render(<AccountsTab addToast={addToast} />)
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Failed to load accounts', 'error'))
    await settle()
    expect(addToast).toHaveBeenCalledTimes(1)
  })

  it('shows success toast after user is created', async () => {
    mocks.adminCreateUser.mockResolvedValue({})
    const addToast = await renderLoaded()
    await user.click(screen.getByRole('button', { name: /Add/ }))
    await user.type(screen.getAllByRole('textbox')[0]!, 'newuser')
    await user.type(screen.getByLabelText('Password'), 'pw')
    await user.click(screen.getByRole('button', { name: 'Create account' }))
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Account created'))
  })

  it('shows success toast after user is updated', async () => {
    mocks.adminUpdateUser.mockResolvedValue({})
    const addToast = await renderLoaded()
    await user.click(screen.getByTitle('Edit'))
    await user.click(screen.getByRole('button', { name: 'Save changes' }))
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Account updated'))
  })

  it('✕ closes the delete modal without deleting', async () => {
    await renderLoaded()
    await user.click(screen.getByTitle('Delete'))
    await user.click(screen.getByRole('button', { name: 'Close' }))
    expect(mocks.adminDeleteUser).not.toHaveBeenCalled()
    expect(screen.queryByText('Confirm deletion')).not.toBeInTheDocument()
  })

  it('opens the add user modal on Add and closes it when ✕ is clicked', async () => {
    await renderLoaded()
    await user.click(screen.getByRole('button', { name: /Add/ }))
    expect(screen.getByRole('button', { name: 'Create account' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Close' }))
    expect(screen.queryByRole('button', { name: 'Create account' })).not.toBeInTheDocument()
  })

  it('opens the edit user modal on Edit and closes it when ✕ is clicked', async () => {
    await renderLoaded()
    await user.click(screen.getByTitle('Edit'))
    expect(screen.getByRole('button', { name: 'Save changes' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Close' }))
    expect(screen.queryByRole('button', { name: 'Save changes' })).not.toBeInTheDocument()
  })

  it('shows the user quota once adminGetUserQuota resolves', async () => {
    mocks.adminGetUserQuota.mockResolvedValue({ storageBytesUsed: 50 * MB, storageBytesLimit: 200 * MB })
    await renderLoaded()
    expect(await screen.findByText('50.0 / 200 MB')).toBeInTheDocument()
    expect(mocks.adminGetUserQuota).toHaveBeenCalledWith(1)
  })

  it('draws an empty list when adminGetUsers answers null', async () => {
    mocks.adminGetUsers.mockResolvedValue(null)
    render(<AccountsTab addToast={vi.fn()} />)
    expect(await screen.findByText('Accounts (0)')).toBeInTheDocument()
    expect(screen.queryByText('alice@weesky.be')).not.toBeInTheDocument()
  })
})

describe('AccountsTab — load failures', () => {
  // The domains only feed the dialog: their failure must not cost the admin the accounts list.
  it('lists the users when only the domains fail, with one toast', async () => {
    mocks.adminGetDomains.mockRejectedValue(new Error('Server error'))
    const addToast = vi.fn()
    render(<AccountsTab addToast={addToast} />)
    expect(await screen.findByText('alice@weesky.be')).toBeInTheDocument()
    await settle()
    expect(addToast.mock.calls).toEqual([['Failed to load the domain list', 'error']])
  })

  // A refetch of a query holding no data puts it back to pending: that is not a first load, so
  // neither the list nor an open dialog may go behind the spinner, and the failure is not news.
  it('keeps the list and an open dialog through a refetch of the failed domains', async () => {
    mocks.adminGetDomains.mockRejectedValue(new Error('Server error'))
    const addToast = vi.fn()
    const client = createTestQueryClient()
    render(<AccountsTab addToast={addToast} />, client)
    await screen.findByText('alice@weesky.be')
    await user.click(screen.getByRole('button', { name: /Add/ }))
    await user.type(screen.getByLabelText('Username'), 'bob')
    const refetch = holdNextCall(mocks.adminGetDomains)

    act(() => { void client.invalidateQueries({ queryKey: ['admin'] }) })
    await settle()
    expect(screen.getByLabelText('Username')).toHaveValue('bob')
    expect(screen.getByText('alice@weesky.be', { selector: '.admin-list-item-email' })).toBeInTheDocument()
    await act(async () => { refetch.fail() })
    await settle()

    expect(mocks.adminGetDomains).toHaveBeenCalledTimes(2)
    expect(screen.getByLabelText('Username')).toHaveValue('bob')
    expect(addToast).toHaveBeenCalledTimes(1)
  })
})
