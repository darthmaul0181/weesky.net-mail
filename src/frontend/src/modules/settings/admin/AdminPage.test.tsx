import { act, render as rtlRender, screen, waitFor, fireEvent, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactElement, ReactNode } from 'react'
import Toasts from '../../../components/Toasts'
import { useToasts } from '../../../hooks/useToasts'
import { holdNextCall, settle } from '../../../test-utils'
import AdminPage from './AdminPage'
import { AddEditUserModal } from './AddEditUserModal'
import { AddEditDomainModal } from './AddEditDomainModal'
import { AccountsTab } from './AccountsTab'
import { DomainsTab } from './DomainsTab'
import { VirtualDomainsTab } from './VirtualDomainsTab'
import ExternalDomainsTab from './ExternalDomainsTab'
import type { AdminDomain, AdminUser, VirtualDomain } from './adminTypes'

const mocks = vi.hoisted(() => ({
  changeFullName: vi.fn(),
  adminGetUsers: vi.fn(),
  adminCreateUser: vi.fn(),
  adminUpdateUser: vi.fn(),
  adminDeleteUser: vi.fn(),
  adminGetDomains: vi.fn(),
  adminCreateDomain: vi.fn(),
  adminUpdateDomain: vi.fn(),
  adminDeleteDomain: vi.fn(),
  adminGetUserQuota: vi.fn(),
  adminGetVirtualDomains: vi.fn(),
  adminAddVirtualDomainOwner: vi.fn(),
  adminRemoveVirtualDomainOwner: vi.fn(),
  adminGetExternalDomains: vi.fn(),
  getAppSettings: vi.fn(),
  setAppSetting: vi.fn(),
}))

vi.mock('../../../api.js', () => ({ api: mocks, clearSession: vi.fn() }))

function newClient() {
  return new QueryClient({ defaultOptions: { queries: { retry: false } } })
}

// Every tab and dialog reads and writes through TanStack Query: each render gets a fresh cache,
// held for the life of that render so a rerender keeps it.
function render(ui: ReactElement, client = newClient()) {
  const wrapper = ({ children }: { children: ReactNode }) =>
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  return rtlRender(ui, { wrapper })
}

function renderAdminPage() {
  return render(<AdminPage />)
}

/** The confirm button of the open delete dialog, never the row's own trash button. */
const confirmButton = () => within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Delete' })

const MB = 1024 * 1024

const MOCK_DOMAINS: AdminDomain[] = [{ id: 'WSY', name: 'weesky.be', aliasCount: 0 }]
const EDIT_DOMAIN: AdminDomain = { id: 'WSY', name: 'weesky.be', aliasCount: 0 }
const MOCK_USERS: AdminUser[] = [
  { id: 1, userName: 'alice', domainName: 'weesky.be', domainId: 'WSY', fullName: 'Alice Smith', quotaMb: 1024, active: true, admin: false, lastLogins: [] },
]
const MOCK_VIRTUAL_DOMAINS: VirtualDomain[] = [
  { domainId: 'EXT', domainName: 'extra.com', owners: [{ ownerId: 1, ownerEmail: 'alice@weesky.be' }] },
  { domainId: 'ORF', domainName: 'orphan.net', owners: [] },
]

const MOCK_APP_SETTINGS = {
  'app.installable': 'true', 'app.name': 'Weesky Mail', 'app.shortName': 'Weesky',
}

beforeEach(() => {
  vi.clearAllMocks()
  mocks.adminGetUsers.mockResolvedValue(MOCK_USERS)
  mocks.adminGetVirtualDomains.mockResolvedValue(MOCK_VIRTUAL_DOMAINS)
  mocks.adminGetDomains.mockResolvedValue(MOCK_DOMAINS)
  mocks.adminGetExternalDomains.mockResolvedValue([])
  mocks.adminGetUserQuota.mockRejectedValue(new Error('unavailable'))
  mocks.getAppSettings.mockResolvedValue(MOCK_APP_SETTINGS)
  mocks.setAppSetting.mockResolvedValue(undefined)
})

// ── AddEditUserModal — create mode ────────────────────────────

describe('AddEditUserModal — create mode', () => {
  function renderCreate(props: Partial<Parameters<typeof AddEditUserModal>[0]> = {}) {
    return render(
      <AddEditUserModal user={null} domains={MOCK_DOMAINS} onSave={vi.fn()} onClose={vi.fn()} {...props} />
    )
  }

  it('submit button is disabled when username is empty', () => {
    renderCreate()
    expect(screen.getByRole('button', { name: 'Create account' })).toBeDisabled()
  })

  it('submit button is disabled when username is filled but password is empty', async () => {
    renderCreate()
    await userEvent.type(screen.getAllByRole('textbox')[0]!, 'alice')
    expect(screen.getByRole('button', { name: 'Create account' })).toBeDisabled()
  })

  it('calls adminCreateUser with correct payload on submit', async () => {
    mocks.adminCreateUser.mockResolvedValue({})
    const onSave = vi.fn()
    renderCreate({ onSave })
    await userEvent.type(screen.getAllByRole('textbox')[0]!, 'alice')
    await userEvent.type(screen.getByLabelText('Password'), 'secret')
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }))
    await waitFor(() =>
      expect(mocks.adminCreateUser).toHaveBeenCalledWith(
        expect.objectContaining({ userName: 'alice', domainId: 'WSY', password: 'secret' })
      )
    )
    await waitFor(() => expect(onSave).toHaveBeenCalledOnce())
  })

  // Server prose never reaches the screen; the local fallback does — see apiErrorMessage.
  it('shows the local fallback when the api call fails', async () => {
    mocks.adminCreateUser.mockRejectedValue(new Error('Duplicate user'))
    renderCreate()
    await userEvent.type(screen.getAllByRole('textbox')[0]!, 'alice')
    await userEvent.type(screen.getByLabelText('Password'), 'pw')
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }))
    await waitFor(() => expect(screen.getByText('An error occurred')).toBeInTheDocument())
  })

  // Abandoning the POST via Escape would skip onSave() and leave the created user invisible
  // until a reload.
  it('blocks Escape while the create request is in flight', async () => {
    let resolveCreate: (value: unknown) => void = () => {}
    mocks.adminCreateUser.mockReturnValue(new Promise(resolve => { resolveCreate = resolve }))
    const onClose = vi.fn()
    renderCreate({ onClose })
    await userEvent.type(screen.getAllByRole('textbox')[0]!, 'alice')
    await userEvent.type(screen.getByLabelText('Password'), 'secret')
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }))

    await userEvent.keyboard('{Escape}')
    expect(onClose).not.toHaveBeenCalled()

    resolveCreate({})
    await waitFor(() => expect(screen.getByRole('button', { name: 'Create account' })).not.toBeDisabled())
    await userEvent.keyboard('{Escape}')
    expect(onClose).toHaveBeenCalledTimes(1)
  })
})

// ── AddEditUserModal — edit mode ──────────────────────────────

describe('AddEditUserModal — edit mode', () => {
  const EDIT_USER: AdminUser = { id: 1, userName: 'alice', domainId: 'WSY', domainName: 'weesky.be', fullName: 'Alice Smith', quotaMb: 1024, active: true, admin: false, lastLogins: [] }

  function renderEdit(props = {}) {
    return render(
      <AddEditUserModal user={EDIT_USER} domains={MOCK_DOMAINS} onSave={vi.fn()} onClose={vi.fn()} {...props} />
    )
  }

  it('username field is disabled', () => {
    renderEdit()
    expect(screen.getAllByRole('textbox')[0]).toBeDisabled()
  })

  it('submit button is enabled without filling password', () => {
    renderEdit()
    expect(screen.getByRole('button', { name: 'Save changes' })).not.toBeDisabled()
  })

  it('password placeholder says "leave blank to keep"', () => {
    renderEdit()
    expect(screen.getByLabelText('Password')).toHaveAttribute('placeholder', 'leave blank to keep')
  })

  it('calls adminUpdateUser with null password when password field is left empty', async () => {
    mocks.adminUpdateUser.mockResolvedValue({})
    renderEdit()
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    await waitFor(() =>
      expect(mocks.adminUpdateUser).toHaveBeenCalledWith(
        1,
        expect.objectContaining({ password: null })
      )
    )
  })

  it('calls adminUpdateUser with new password when one is provided', async () => {
    mocks.adminUpdateUser.mockResolvedValue({})
    renderEdit()
    await userEvent.type(screen.getByLabelText('Password'), 'newpass')
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    await waitFor(() =>
      expect(mocks.adminUpdateUser).toHaveBeenCalledWith(
        1,
        expect.objectContaining({ password: 'newpass' })
      )
    )
  })
})

// ── AddEditDomainModal — create mode ──────────────────────────

describe('AddEditDomainModal — create mode', () => {
  function renderCreate(props = {}) {
    return render(
      <AddEditDomainModal domain={null} onSave={vi.fn()} onClose={vi.fn()} {...props} />
    )
  }

  it('renders id and name fields', () => {
    renderCreate()
    expect(screen.getAllByRole('textbox')).toHaveLength(2)
  })

  it('submit button is disabled when both fields are empty', () => {
    renderCreate()
    expect(screen.getByRole('button', { name: 'Create domain' })).toBeDisabled()
  })

  it('submit button is disabled when only id is filled', async () => {
    renderCreate()
    await userEvent.type(screen.getAllByRole('textbox')[0]!, 'TST')
    expect(screen.getByRole('button', { name: 'Create domain' })).toBeDisabled()
  })

  it('submit button is disabled when only name is filled', async () => {
    renderCreate()
    await userEvent.type(screen.getAllByRole('textbox')[1]!, 'test.com')
    expect(screen.getByRole('button', { name: 'Create domain' })).toBeDisabled()
  })

  it('submit button is enabled when both fields are filled with a valid domain', async () => {
    renderCreate()
    await userEvent.type(screen.getAllByRole('textbox')[0]!, 'TST')
    await userEvent.type(screen.getAllByRole('textbox')[1]!, 'test.com')
    expect(screen.getByRole('button', { name: 'Create domain' })).not.toBeDisabled()
  })

  it('submit button is disabled when domain name is syntactically invalid', async () => {
    renderCreate()
    await userEvent.type(screen.getAllByRole('textbox')[0]!, 'TST')
    await userEvent.type(screen.getAllByRole('textbox')[1]!, 'notadomain')
    expect(screen.getByRole('button', { name: 'Create domain' })).toBeDisabled()
  })

  it('name input gets is-error class when domain name is invalid', async () => {
    renderCreate()
    const nameInput = screen.getAllByRole('textbox')[1]!
    await userEvent.type(nameInput, 'notadomain')
    expect(nameInput).toHaveClass('is-error')
  })

  it('name input has no is-error class when domain name is valid', async () => {
    renderCreate()
    const nameInput = screen.getAllByRole('textbox')[1]!
    await userEvent.type(nameInput, 'test.com')
    expect(nameInput).not.toHaveClass('is-error')
  })

  it('name input has no is-error class when field is empty', () => {
    renderCreate()
    expect(screen.getAllByRole('textbox')[1]).not.toHaveClass('is-error')
  })

  it('calls adminCreateDomain with correct payload on submit', async () => {
    mocks.adminCreateDomain.mockResolvedValue({})
    const onSave = vi.fn()
    renderCreate({ onSave })
    const [idInput, nameInput] = screen.getAllByRole('textbox') as [HTMLElement, HTMLElement]
    await userEvent.type(idInput, 'TST')
    await userEvent.type(nameInput, 'test.com')
    await userEvent.click(screen.getByRole('button', { name: 'Create domain' }))
    await waitFor(() =>
      expect(mocks.adminCreateDomain).toHaveBeenCalledWith(
        expect.objectContaining({ id: 'TST', name: 'test.com' })
      )
    )
    await waitFor(() => expect(onSave).toHaveBeenCalledOnce())
  })

  it('blocks Escape while the create request is in flight', async () => {
    let resolveCreate: (value: unknown) => void = () => {}
    mocks.adminCreateDomain.mockReturnValue(new Promise(resolve => { resolveCreate = resolve }))
    const onClose = vi.fn()
    renderCreate({ onClose })
    const [idInput, nameInput] = screen.getAllByRole('textbox') as [HTMLElement, HTMLElement]
    await userEvent.type(idInput, 'TST')
    await userEvent.type(nameInput, 'test.com')
    await userEvent.click(screen.getByRole('button', { name: 'Create domain' }))

    await userEvent.keyboard('{Escape}')
    expect(onClose).not.toHaveBeenCalled()

    resolveCreate({})
    await waitFor(() => expect(screen.getByRole('button', { name: 'Create domain' })).not.toBeDisabled())
    await userEvent.keyboard('{Escape}')
    expect(onClose).toHaveBeenCalledTimes(1)
  })
})

// ── AddEditDomainModal — edit mode ────────────────────────────

describe('AddEditDomainModal — edit mode', () => {
  it('id field is disabled', () => {
    render(<AddEditDomainModal domain={EDIT_DOMAIN} onSave={vi.fn()} onClose={vi.fn()} />)
    const [idInput] = screen.getAllByRole('textbox') as [HTMLElement]
    expect(idInput).toBeDisabled()
  })

  it('calls adminUpdateDomain on submit', async () => {
    mocks.adminUpdateDomain.mockResolvedValue({})
    render(<AddEditDomainModal domain={EDIT_DOMAIN} onSave={vi.fn()} onClose={vi.fn()} />)
    const nameInput = screen.getAllByRole('textbox')[1]!
    await userEvent.clear(nameInput)
    await userEvent.type(nameInput, 'new.weesky.be')
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    await waitFor(() =>
      expect(mocks.adminUpdateDomain).toHaveBeenCalledWith('WSY', expect.objectContaining({ name: 'new.weesky.be' }))
    )
  })
})

// ── AccountsTab ───────────────────────────────────────────────

describe('AccountsTab', () => {
  it('fetches users and domains on mount', async () => {
    render(<AccountsTab addToast={vi.fn()} />)
    await waitFor(() => expect(mocks.adminGetUsers).toHaveBeenCalledOnce())
    expect(mocks.adminGetDomains).toHaveBeenCalledOnce()
  })

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

  it('filters the list by username when searching', async () => {
    mocks.adminGetUsers.mockResolvedValue([
      ...MOCK_USERS,
      { id: 2, userName: 'bob', domainName: 'weesky.be', domainId: 'WSY', fullName: 'Bob Jones', quotaMb: 1024, active: true, admin: false },
    ])
    render(<AccountsTab addToast={vi.fn()} />)
    await screen.findByText('alice@weesky.be')
    await userEvent.type(screen.getByPlaceholderText('Search…'), 'bob')
    expect(screen.queryByText('alice@weesky.be')).not.toBeInTheDocument()
    expect(screen.getByText('bob@weesky.be')).toBeInTheDocument()
  })

  it('filters the list by full name when searching', async () => {
    mocks.adminGetUsers.mockResolvedValue([
      ...MOCK_USERS,
      { id: 2, userName: 'bob', domainName: 'weesky.be', domainId: 'WSY', fullName: 'Bob Jones', quotaMb: 1024, active: true, admin: false },
    ])
    render(<AccountsTab addToast={vi.fn()} />)
    await screen.findByText('alice@weesky.be')
    await userEvent.type(screen.getByPlaceholderText('Search…'), 'Jones')
    expect(screen.queryByText('alice@weesky.be')).not.toBeInTheDocument()
    expect(screen.getByText('bob@weesky.be')).toBeInTheDocument()
  })

  // The deleted row's own button leaves with the refetched list, after the confirm has closed:
  // the page's own region is what takes the focus back.
  it('hands focus to the tab content when the deleted row goes with the reload', async () => {
    mocks.adminDeleteUser.mockResolvedValue(null)
    const { container } = renderAdminPage()
    await screen.findByText('alice@weesky.be')
    await userEvent.click(screen.getByTitle('Delete'))

    await userEvent.click(confirmButton())

    await waitFor(() => expect(mocks.adminDeleteUser).toHaveBeenCalledWith(1))
    expect(container.querySelector('.admin-tab-content')).toHaveFocus()
  })

  it('calls adminDeleteUser when delete is confirmed', async () => {
    mocks.adminDeleteUser.mockResolvedValue(null)
    render(<AccountsTab addToast={vi.fn()} />)
    await screen.findByText('alice@weesky.be')
    await userEvent.click(screen.getByTitle('Delete'))
    await userEvent.click(confirmButton())
    await waitFor(() => expect(mocks.adminDeleteUser).toHaveBeenCalledWith(1))
  })

  // Server prose never reaches the toast; the local fallback does — see apiErrorMessage.
  it('shows the local fallback when user delete fails', async () => {
    mocks.adminDeleteUser.mockRejectedValue(new Error('Cannot delete'))
    const addToast = vi.fn()
    render(<AccountsTab addToast={addToast} />)
    await screen.findByText('alice@weesky.be')
    await userEvent.click(screen.getByTitle('Delete'))
    await userEvent.click(confirmButton())
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Failed to delete user', 'error'))
  })

  // A refused write still re-reads the list: the screen ends on server state, never on a guess.
  it('re-reads the accounts after a refused delete', async () => {
    mocks.adminDeleteUser.mockRejectedValue(new Error('Cannot delete'))
    render(<AccountsTab addToast={vi.fn()} />)
    await screen.findByText('alice@weesky.be')
    await userEvent.click(screen.getByTitle('Delete'))
    await userEvent.click(confirmButton())
    await waitFor(() => expect(mocks.adminGetUsers).toHaveBeenCalledTimes(2))
    expect(screen.getByText('alice@weesky.be', { selector: '.admin-list-item-email' })).toBeInTheDocument()
  })

  it('shows error toast when loading fails', async () => {
    mocks.adminGetUsers.mockRejectedValue(new Error('Server error'))
    const addToast = vi.fn()
    render(<AccountsTab addToast={addToast} />)
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Failed to load accounts', 'error'))
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

  it('opens AddEditUserModal when Add is clicked', async () => {
    render(<AccountsTab addToast={vi.fn()} />)
    await screen.findByText('alice@weesky.be')
    await userEvent.click(screen.getByRole('button', { name: /Add/ }))
    expect(screen.getByRole('button', { name: 'Create account' })).toBeInTheDocument()
  })

  it('shows success toast after user is created', async () => {
    mocks.adminCreateUser.mockResolvedValue({})
    const addToast = vi.fn()
    render(<AccountsTab addToast={addToast} />)
    await screen.findByText('alice@weesky.be')
    await userEvent.click(screen.getByRole('button', { name: /Add/ }))
    await userEvent.type(screen.getAllByRole('textbox')[0]!, 'newuser')
    await userEvent.type(screen.getByLabelText('Password'), 'pw')
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }))
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Account created'))
  })

  it('opens AddEditUserModal with user data when Edit is clicked', async () => {
    render(<AccountsTab addToast={vi.fn()} />)
    await screen.findByText('alice@weesky.be')
    await userEvent.click(screen.getByTitle('Edit'))
    expect(screen.getByRole('button', { name: 'Save changes' })).toBeInTheDocument()
  })

  it('shows success toast after user is updated', async () => {
    mocks.adminUpdateUser.mockResolvedValue({})
    const addToast = vi.fn()
    render(<AccountsTab addToast={addToast} />)
    await screen.findByText('alice@weesky.be')
    await userEvent.click(screen.getByTitle('Edit'))
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Account updated'))
  })

  it('✕ closes the delete modal without deleting', async () => {
    render(<AccountsTab addToast={vi.fn()} />)
    await screen.findByText('alice@weesky.be')
    await userEvent.click(screen.getByTitle('Delete'))
    await userEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(mocks.adminDeleteUser).not.toHaveBeenCalled()
    expect(screen.queryByText('Confirm deletion')).not.toBeInTheDocument()
  })

  it('closes the add user modal when ✕ is clicked', async () => {
    render(<AccountsTab addToast={vi.fn()} />)
    await screen.findByText('alice@weesky.be')
    await userEvent.click(screen.getByRole('button', { name: /Add/ }))
    expect(screen.getByRole('button', { name: 'Create account' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(screen.queryByRole('button', { name: 'Create account' })).not.toBeInTheDocument()
  })

  it('closes the edit user modal when ✕ is clicked', async () => {
    render(<AccountsTab addToast={vi.fn()} />)
    await screen.findByText('alice@weesky.be')
    await userEvent.click(screen.getByTitle('Edit'))
    expect(screen.getByRole('button', { name: 'Save changes' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(screen.queryByRole('button', { name: 'Save changes' })).not.toBeInTheDocument()
  })

  it('shows user quota when adminGetUserQuota resolves', async () => {
    mocks.adminGetUserQuota.mockResolvedValue({ storageBytesUsed: 50 * MB, storageBytesLimit: 200 * MB })
    render(<AccountsTab addToast={vi.fn()} />)
    await screen.findByText('alice@weesky.be')
    await waitFor(() => expect(mocks.adminGetUserQuota).toHaveBeenCalledWith(1))
  })

  it('handles null response from adminGetUsers gracefully', async () => {
    mocks.adminGetUsers.mockResolvedValue(null)
    render(<AccountsTab addToast={vi.fn()} />)
    await waitFor(() => expect(mocks.adminGetUsers).toHaveBeenCalledOnce())
    expect(screen.queryByText('alice@weesky.be')).not.toBeInTheDocument()
  })

  it('uses fallback message when delete error has no message', async () => {
    mocks.adminDeleteUser.mockRejectedValue(new Error())
    const addToast = vi.fn()
    render(<AccountsTab addToast={addToast} />)
    await screen.findByText('alice@weesky.be')
    await userEvent.click(screen.getByTitle('Delete'))
    await userEvent.click(confirmButton())
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Failed to delete user', 'error'))
  })
})

// ── DomainsTab ────────────────────────────────────────────────

describe('DomainsTab', () => {
  it('fetches domains on mount', async () => {
    render(<DomainsTab addToast={vi.fn()} />)
    await waitFor(() => expect(mocks.adminGetDomains).toHaveBeenCalledOnce())
  })

  it('loads once and does not reload on a re-render', async () => {
    const addToast = vi.fn()
    const { rerender } = render(<DomainsTab addToast={addToast} />)
    await screen.findByText('WSY')

    rerender(<DomainsTab addToast={addToast} />)
    await settle()

    expect(mocks.adminGetDomains).toHaveBeenCalledOnce()
  })

  it('renders the domain list', async () => {
    render(<DomainsTab addToast={vi.fn()} />)
    expect(await screen.findByText('WSY')).toBeInTheDocument()
    expect(screen.getByText('weesky.be')).toBeInTheDocument()
  })

  it('shows error toast when loading fails', async () => {
    mocks.adminGetDomains.mockRejectedValue(new Error('Server error'))
    const addToast = vi.fn()
    render(<DomainsTab addToast={addToast} />)
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Failed to load domains', 'error'))
  })

  it('opens AddEditDomainModal when Add is clicked', async () => {
    render(<DomainsTab addToast={vi.fn()} />)
    await screen.findByText('WSY')
    await userEvent.click(screen.getByRole('button', { name: /Add/ }))
    expect(screen.getByRole('button', { name: 'Create domain' })).toBeInTheDocument()
  })

  it('shows success toast after domain is created', async () => {
    mocks.adminCreateDomain.mockResolvedValue({})
    const addToast = vi.fn()
    render(<DomainsTab addToast={addToast} />)
    await screen.findByText('WSY')
    await userEvent.click(screen.getByRole('button', { name: /Add/ }))
    const [idInput, nameInput] = screen.getAllByRole('textbox') as [HTMLElement, HTMLElement]
    await userEvent.type(idInput, 'TST')
    await userEvent.type(nameInput, 'test.com')
    await userEvent.click(screen.getByRole('button', { name: 'Create domain' }))
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Domain created'))
  })

  it('opens AddEditDomainModal with domain data when Edit is clicked', async () => {
    render(<DomainsTab addToast={vi.fn()} />)
    await screen.findByText('WSY')
    await userEvent.click(screen.getByTitle('Edit'))
    expect(screen.getByRole('button', { name: 'Save changes' })).toBeInTheDocument()
  })

  it('shows success toast after domain is updated', async () => {
    mocks.adminUpdateDomain.mockResolvedValue({})
    const addToast = vi.fn()
    render(<DomainsTab addToast={addToast} />)
    await screen.findByText('WSY')
    await userEvent.click(screen.getByTitle('Edit'))
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Domain updated'))
  })

  it('calls adminDeleteDomain and shows toast after deletion', async () => {
    mocks.adminDeleteDomain.mockResolvedValue(null)
    const addToast = vi.fn()
    render(<DomainsTab addToast={addToast} />)
    await screen.findByText('WSY')
    await userEvent.click(screen.getByTitle('Delete'))
    await userEvent.click(confirmButton())
    await waitFor(() => expect(mocks.adminDeleteDomain).toHaveBeenCalledWith('WSY', false))
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Domain weesky.be deleted'))
  })

  // The aliases cascade away with the domain and no screen anywhere lists another user's, so the
  // count on the confirmation is the only warning the admin will ever get.
  it('says how many aliases go with the domain before deleting it', async () => {
    mocks.adminGetDomains.mockResolvedValue([{ id: 'WSY', name: 'weesky.be', aliasCount: 3 }])
    render(<DomainsTab addToast={vi.fn()} />)
    await screen.findByText('WSY')

    await userEvent.click(screen.getByTitle('Delete'))

    expect(screen.getByText(/3 aliases/)).toBeInTheDocument()
    expect(screen.getByText(/stop being delivered/)).toBeInTheDocument()
  })

  it('reads the count as singular when one alias goes', async () => {
    mocks.adminGetDomains.mockResolvedValue([{ id: 'WSY', name: 'weesky.be', aliasCount: 1 }])
    render(<DomainsTab addToast={vi.fn()} />)
    await screen.findByText('WSY')

    await userEvent.click(screen.getByTitle('Delete'))

    expect(screen.getByText(/1 alias(?!es)/)).toBeInTheDocument()
    expect(screen.getByText(/that address/)).toBeInTheDocument()
  })

  // Confirming *is* the acknowledgement: without it on the wire the API refuses, and the admin
  // would be shown a warning that changed nothing.
  it('acknowledges the cascade once the admin has confirmed', async () => {
    mocks.adminGetDomains.mockResolvedValue([{ id: 'WSY', name: 'weesky.be', aliasCount: 3 }])
    mocks.adminDeleteDomain.mockResolvedValue(null)
    render(<DomainsTab addToast={vi.fn()} />)
    await screen.findByText('WSY')

    await userEvent.click(screen.getByTitle('Delete'))
    await userEvent.click(confirmButton())

    await waitFor(() => expect(mocks.adminDeleteDomain).toHaveBeenCalledWith('WSY', true))
  })

  it('keeps the plain wording when the domain holds no alias', async () => {
    mocks.adminGetDomains.mockResolvedValue([{ id: 'WSY', name: 'weesky.be', aliasCount: 0 }])
    render(<DomainsTab addToast={vi.fn()} />)
    await screen.findByText('WSY')

    await userEvent.click(screen.getByTitle('Delete'))

    expect(screen.getByText(/This action cannot be undone/)).toBeInTheDocument()
    expect(screen.queryByText(/stop being delivered/)).not.toBeInTheDocument()
  })

  // Server prose never reaches the toast; the local fallback does — see apiErrorMessage.
  it('shows the local fallback when delete fails', async () => {
    mocks.adminDeleteDomain.mockRejectedValue(new Error('Has users'))
    const addToast = vi.fn()
    render(<DomainsTab addToast={addToast} />)
    await screen.findByText('WSY')
    await userEvent.click(screen.getByTitle('Delete'))
    await userEvent.click(confirmButton())
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Failed to delete domain', 'error'))
  })

  it('✕ closes delete modal without deleting', async () => {
    render(<DomainsTab addToast={vi.fn()} />)
    await screen.findByText('WSY')
    await userEvent.click(screen.getByTitle('Delete'))
    await userEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(mocks.adminDeleteDomain).not.toHaveBeenCalled()
    expect(screen.queryByText('Confirm deletion')).not.toBeInTheDocument()
  })

  it('closes the add domain modal when ✕ is clicked', async () => {
    render(<DomainsTab addToast={vi.fn()} />)
    await screen.findByText('WSY')
    await userEvent.click(screen.getByRole('button', { name: /Add/ }))
    expect(screen.getByRole('button', { name: 'Create domain' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(screen.queryByRole('button', { name: 'Create domain' })).not.toBeInTheDocument()
  })

  it('closes the edit domain modal when ✕ is clicked', async () => {
    render(<DomainsTab addToast={vi.fn()} />)
    await screen.findByText('WSY')
    await userEvent.click(screen.getByTitle('Edit'))
    expect(screen.getByRole('button', { name: 'Save changes' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(screen.queryByRole('button', { name: 'Save changes' })).not.toBeInTheDocument()
  })

  it('handles null response from adminGetDomains gracefully', async () => {
    mocks.adminGetDomains.mockResolvedValue(null)
    render(<DomainsTab addToast={vi.fn()} />)
    await waitFor(() => expect(mocks.adminGetDomains).toHaveBeenCalledOnce())
    expect(screen.queryByText('WSY')).not.toBeInTheDocument()
  })

  it('uses fallback message when domain delete error has no message', async () => {
    mocks.adminDeleteDomain.mockRejectedValue(new Error())
    const addToast = vi.fn()
    render(<DomainsTab addToast={addToast} />)
    await screen.findByText('WSY')
    await userEvent.click(screen.getByTitle('Delete'))
    await userEvent.click(confirmButton())
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Failed to delete domain', 'error'))
  })
})

// ── AddEditUserModal — additional field changes ───────────────

describe('AddEditUserModal — field changes', () => {
  const TWO_DOMAINS: AdminDomain[] = [
    { id: 'WSY', name: 'weesky.be', aliasCount: 0 },
    { id: 'EXM', name: 'example.com', aliasCount: 0 },
  ]

  it('changing the domain select updates domainId in the payload', async () => {
    mocks.adminCreateUser.mockResolvedValue({})
    const onSave = vi.fn()
    render(
      <AddEditUserModal user={null} domains={TWO_DOMAINS} onSave={onSave} onClose={vi.fn()} />
    )
    await userEvent.type(screen.getAllByRole('textbox')[0]!, 'alice')
    await userEvent.type(screen.getByLabelText('Password'), 'pw')
    await userEvent.selectOptions(screen.getByRole('combobox'), 'EXM')
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }))
    await waitFor(() =>
      expect(mocks.adminCreateUser).toHaveBeenCalledWith(
        expect.objectContaining({ domainId: 'EXM' })
      )
    )
  })

  it('changing the full name field updates the value', async () => {
    render(
      <AddEditUserModal user={null} domains={MOCK_DOMAINS} onSave={vi.fn()} onClose={vi.fn()} />
    )
    const fullNameInput = screen.getAllByRole('textbox')[1]!
    await userEvent.type(fullNameInput, 'Alice Smith')
    expect(fullNameInput).toHaveValue('Alice Smith')
  })

  it('changing the range slider updates the quota number input', () => {
    render(
      <AddEditUserModal user={null} domains={MOCK_DOMAINS} onSave={vi.fn()} onClose={vi.fn()} />
    )
    fireEvent.change(screen.getByRole('slider'), { target: { value: '2048' } })
    expect(screen.getByRole('spinbutton')).toHaveValue(2048)
  })
})

// ── AddEditUserModal — toggles & quota ───────────────────────

describe('AddEditUserModal — toggles and quota', () => {
  function renderCreate(props = {}) {
    return render(
      <AddEditUserModal user={null} domains={MOCK_DOMAINS} onSave={vi.fn()} onClose={vi.fn()} {...props} />
    )
  }

  it('unchecking active sets active:false in the payload', async () => {
    mocks.adminCreateUser.mockResolvedValue({})
    renderCreate()
    await userEvent.type(screen.getAllByRole('textbox')[0]!, 'alice')
    await userEvent.type(screen.getByLabelText('Password'), 'pw')
    const [activeCheckbox] = screen.getAllByRole('checkbox') as [HTMLElement]
    await userEvent.click(activeCheckbox) // uncheck active (was true by default)
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }))
    await waitFor(() =>
      expect(mocks.adminCreateUser).toHaveBeenCalledWith(
        expect.objectContaining({ active: false })
      )
    )
  })

  it('checking admin sets admin:true in the payload', async () => {
    mocks.adminCreateUser.mockResolvedValue({})
    renderCreate()
    await userEvent.type(screen.getAllByRole('textbox')[0]!, 'alice')
    await userEvent.type(screen.getByLabelText('Password'), 'pw')
    const [, adminCheckbox] = screen.getAllByRole('checkbox') as [HTMLElement, HTMLElement]
    await userEvent.click(adminCheckbox) // check admin (was false by default)
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }))
    await waitFor(() =>
      expect(mocks.adminCreateUser).toHaveBeenCalledWith(
        expect.objectContaining({ admin: true })
      )
    )
  })

  it('changing the quota number input updates the slider value', () => {
    renderCreate()
    const numberInput = screen.getByRole('spinbutton')
    fireEvent.change(numberInput, { target: { value: '512' } })
    expect(screen.getByRole('slider')).toHaveValue('512')
  })
})

// ── AddEditDomainModal — error case ───────────────────────────

describe('AddEditDomainModal — error handling', () => {
  // Server prose never reaches the screen; the local fallback does — see apiErrorMessage.
  it('shows the local fallback when create API fails', async () => {
    mocks.adminCreateDomain.mockRejectedValue(new Error('Invalid ID'))
    render(<AddEditDomainModal domain={null} onSave={vi.fn()} onClose={vi.fn()} />)
    const [idInput, nameInput] = screen.getAllByRole('textbox') as [HTMLElement, HTMLElement]
    await userEvent.type(idInput, 'TST')
    await userEvent.type(nameInput, 'test.com')
    await userEvent.click(screen.getByRole('button', { name: 'Create domain' }))
    await waitFor(() => expect(screen.getByText('An error occurred')).toBeInTheDocument())
  })

  it('shows the local fallback when update API fails', async () => {
    mocks.adminUpdateDomain.mockRejectedValue(new Error('Not found'))
    render(<AddEditDomainModal domain={EDIT_DOMAIN} onSave={vi.fn()} onClose={vi.fn()} />)
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    await waitFor(() => expect(screen.getByText('An error occurred')).toBeInTheDocument())
  })
})

// ── AddEditUserModal — accessible field names ─────────────────

describe('AddEditUserModal — accessible field names', () => {
  it('every field is reachable through its label', () => {
    render(
      <AddEditUserModal user={null} domains={MOCK_DOMAINS} onSave={vi.fn()} onClose={vi.fn()} />
    )
    expect(screen.getByLabelText('Username')).toBeInTheDocument()
    expect(screen.getByLabelText('Domain')).toBeInTheDocument()
    expect(screen.getByLabelText('Password')).toBeInTheDocument()
    expect(screen.getByLabelText('Full name')).toBeInTheDocument()
    expect(screen.getByRole('spinbutton', { name: 'Quota (MB)' })).toBeInTheDocument()
    expect(screen.getByRole('slider', { name: 'Quota (MB)' })).toBeInTheDocument()
    expect(screen.getByRole('checkbox', { name: 'Active' })).toBeInTheDocument()
    expect(screen.getByRole('checkbox', { name: 'Administrator' })).toBeInTheDocument()
  })

  it('every field is reachable through its label in edit mode, and the username stays disabled', () => {
    const EDIT_USER: AdminUser = { id: 1, userName: 'alice', domainId: 'WSY', domainName: 'weesky.be', fullName: 'Alice Smith', quotaMb: 1024, active: true, admin: false, lastLogins: [] }
    render(
      <AddEditUserModal user={EDIT_USER} domains={MOCK_DOMAINS} onSave={vi.fn()} onClose={vi.fn()} />
    )
    const username = screen.getByLabelText('Username')
    expect(username).toBeInTheDocument()
    expect(username).toBeDisabled()
  })

  it('the ✕ close button has an accessible name', () => {
    render(
      <AddEditUserModal user={null} domains={MOCK_DOMAINS} onSave={vi.fn()} onClose={vi.fn()} />
    )
    expect(screen.getByRole('button', { name: 'Close' })).toBeInTheDocument()
  })

  it('the error banner announces itself as an alert', async () => {
    mocks.adminCreateUser.mockRejectedValue(new Error('Duplicate user'))
    render(
      <AddEditUserModal user={null} domains={MOCK_DOMAINS} onSave={vi.fn()} onClose={vi.fn()} />
    )
    await userEvent.type(screen.getByLabelText('Username'), 'alice')
    await userEvent.type(screen.getByLabelText('Password'), 'pw')
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('An error occurred')
  })

  it('two instances mounted at once do not collide on id', () => {
    render(
      <>
        <AddEditUserModal user={null} domains={MOCK_DOMAINS} onSave={vi.fn()} onClose={vi.fn()} />
        <AddEditUserModal user={null} domains={MOCK_DOMAINS} onSave={vi.fn()} onClose={vi.fn()} />
      </>
    )
    expect(screen.getAllByLabelText('Username')).toHaveLength(2)
  })
})

// ── AddEditDomainModal — accessible field names ────────────────

describe('AddEditDomainModal — accessible field names', () => {
  it('every field is reachable through its label', () => {
    render(<AddEditDomainModal domain={null} onSave={vi.fn()} onClose={vi.fn()} />)
    expect(screen.getByLabelText('ID (3 chars max)')).toBeInTheDocument()
    expect(screen.getByLabelText('Domain name')).toBeInTheDocument()
  })

  it('every field is reachable through its label in edit mode, and the id stays disabled', () => {
    render(
      <AddEditDomainModal domain={EDIT_DOMAIN} onSave={vi.fn()} onClose={vi.fn()} />
    )
    const idInput = screen.getByLabelText('ID (3 chars max)')
    expect(idInput).toBeInTheDocument()
    expect(idInput).toBeDisabled()
  })

  it('the ✕ close button has an accessible name', () => {
    render(<AddEditDomainModal domain={null} onSave={vi.fn()} onClose={vi.fn()} />)
    expect(screen.getByRole('button', { name: 'Close' })).toBeInTheDocument()
  })

  it('the error banner announces itself as an alert', async () => {
    mocks.adminCreateDomain.mockRejectedValue(new Error('Invalid ID'))
    render(<AddEditDomainModal domain={null} onSave={vi.fn()} onClose={vi.fn()} />)
    await userEvent.type(screen.getByLabelText('ID (3 chars max)'), 'TST')
    await userEvent.type(screen.getByLabelText('Domain name'), 'test.com')
    await userEvent.click(screen.getByRole('button', { name: 'Create domain' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('An error occurred')
  })
})

// ── AdminPage ─────────────────────────────────────────────────

describe('AdminPage', () => {
  it('shows the Accounts tab as active by default', async () => {
    renderAdminPage()
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Accounts' })).toHaveClass('is-active')
    )
  })

  it('switches to Domains tab when clicked', async () => {
    renderAdminPage()
    await userEvent.click(screen.getByRole('button', { name: 'Domains' }))
    expect(screen.getByRole('button', { name: 'Domains' })).toHaveClass('is-active')
    expect(await screen.findByText('WSY')).toBeInTheDocument()
  })

  it('switches to Virtual domains tab and loads alias domains', async () => {
    renderAdminPage()
    await userEvent.click(screen.getByRole('button', { name: 'Virtual domains' }))
    expect(screen.getByRole('button', { name: 'Virtual domains' })).toHaveClass('is-active')
    expect(await screen.findByText('extra.com')).toBeInTheDocument()
  })

  it('switches to External domains tab and loads external domains', async () => {
    mocks.adminGetExternalDomains.mockResolvedValue([{
      id: '1', name: 'Gmail', imapHost: 'imap.gmail.com', imapPort: 993, imapSecurity: 'SslOnConnect',
      smtpHost: 'smtp.gmail.com', smtpPort: 587, smtpSecurity: 'StartTls',
    }])
    renderAdminPage()
    await userEvent.click(screen.getByRole('button', { name: 'External domains' }))
    expect(screen.getByRole('button', { name: 'External domains' })).toHaveClass('is-active')
    expect(await screen.findByText('Gmail')).toBeInTheDocument()
  })

  it('switches to Application tab and shows the app settings', async () => {
    renderAdminPage()
    await userEvent.click(screen.getByRole('button', { name: 'Application' }))
    expect(screen.getByRole('button', { name: 'Application' })).toHaveClass('is-active')
    expect(await screen.findByLabelText('Application name')).toHaveValue('Weesky Mail')
  })

  // R2: the list the tab already showed comes back from the cache, not from behind a spinner.
  it('shows the cached accounts at once when the admin returns to the tab', async () => {
    renderAdminPage()
    await screen.findByText('alice@weesky.be')
    await userEvent.click(screen.getByRole('button', { name: 'Domains' }))
    await screen.findByText('WSY')

    fireEvent.click(screen.getByRole('button', { name: 'Accounts' }))

    expect(screen.getByText('alice@weesky.be')).toBeInTheDocument()
    expect(screen.queryByRole('status', { name: 'Loading' })).not.toBeInTheDocument()
  })

  it('announces the first load as a status rather than a bare spinner', async () => {
    mocks.adminGetUsers.mockReturnValue(new Promise(() => {}))
    renderAdminPage()
    await settle()
    expect(screen.getByRole('status', { name: 'Loading' })).toBeInTheDocument()
  })

  // The cache must not cost requests: opening Accounts issues what it always did (the users, the
  // domains, one quota per user), and the trip back from Domains issues none while the App's
  // 30 s staleTime holds. A quota that failed has nothing cached, so it is asked again, as before.
  it('issues no request on the way back to Accounts while the cache is fresh', async () => {
    mocks.adminGetUserQuota.mockResolvedValue({ storageBytesUsed: 50 * MB, storageBytesLimit: 200 * MB })
    render(<AdminPage />, new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 30_000 } } }))
    const calls = () => [mocks.adminGetUsers, mocks.adminGetDomains, mocks.adminGetVirtualDomains, mocks.adminGetUserQuota]
      .map(f => f.mock.calls.length)
    await screen.findByText('alice@weesky.be')
    await settle()
    expect(calls()).toEqual([1, 1, 0, 1])

    await userEvent.click(screen.getByRole('button', { name: 'Domains' }))
    await screen.findByText('WSY')
    await userEvent.click(screen.getByRole('button', { name: 'Accounts' }))
    await screen.findByText('alice@weesky.be')
    await settle()

    expect(calls()).toEqual([1, 1, 0, 1])
  })

  it('switches back to Accounts tab after visiting Domains', async () => {
    renderAdminPage()
    await userEvent.click(screen.getByRole('button', { name: 'Domains' }))
    await userEvent.click(screen.getByRole('button', { name: 'Accounts' }))
    expect(screen.getByRole('button', { name: 'Accounts' })).toHaveClass('is-active')
  })

  // Accounts offers no help, and that is the decision rather than the missing key it looked like:
  // the tab explains itself, and a "?" with nothing behind it is worse than none.
  it('offers no help on the Accounts tab', async () => {
    renderAdminPage()
    await waitFor(() => expect(screen.getByRole('button', { name: 'Accounts' })).toHaveClass('is-active'))

    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument()
  })

  it('shows the matching help text on every tab that has some', async () => {
    renderAdminPage()

    await userEvent.click(screen.getByRole('button', { name: 'Domains' }))
    expect(screen.getByRole('tooltip')).toHaveTextContent('A domain is a mail domain hosted directly')

    await userEvent.click(screen.getByRole('button', { name: 'Virtual domains' }))
    expect(screen.getByRole('tooltip')).toHaveTextContent('A virtual alias domain is a domain with no mailboxes')

    await userEvent.click(screen.getByRole('button', { name: 'External domains' }))
    expect(screen.getByRole('tooltip')).toHaveTextContent('Define the external mail providers')

    await userEvent.click(screen.getByRole('button', { name: 'Application' }))
    expect(screen.getByRole('tooltip')).toHaveTextContent('Offers the webmail for installation')
  })
})

// ── VirtualDomainsTab ─────────────────────────────────────────

describe('VirtualDomainsTab', () => {
  it('fetches virtual domains and users on mount', async () => {
    render(<VirtualDomainsTab addToast={vi.fn()} />)
    await waitFor(() => expect(mocks.adminGetVirtualDomains).toHaveBeenCalledOnce())
    expect(mocks.adminGetUsers).toHaveBeenCalledOnce()
  })

  it('loads once and does not reload on a re-render', async () => {
    const addToast = vi.fn()
    const { rerender } = render(<VirtualDomainsTab addToast={addToast} />)
    await screen.findByText('extra.com')

    rerender(<VirtualDomainsTab addToast={addToast} />)
    await settle()

    expect(mocks.adminGetVirtualDomains).toHaveBeenCalledOnce()
  })

  it('renders domain names after loading', async () => {
    render(<VirtualDomainsTab addToast={vi.fn()} />)
    expect(await screen.findByText('extra.com')).toBeInTheDocument()
    expect(screen.getByText('orphan.net')).toBeInTheDocument()
  })

  it('renders owner email for owned domains', async () => {
    render(<VirtualDomainsTab addToast={vi.fn()} />)
    expect(await screen.findByText('alice@weesky.be')).toBeInTheDocument()
  })

  it('renders — for unowned domains', async () => {
    render(<VirtualDomainsTab addToast={vi.fn()} />)
    await screen.findByText('extra.com')
    expect(screen.getByText('—')).toBeInTheDocument()
  })

  it('shows "No alias domains" when list is empty', async () => {
    mocks.adminGetVirtualDomains.mockResolvedValue([])
    render(<VirtualDomainsTab addToast={vi.fn()} />)
    expect(await screen.findByText('No virtual alias domains')).toBeInTheDocument()
  })

  it('shows search input when pencil is clicked', async () => {
    render(<VirtualDomainsTab addToast={vi.fn()} />)
    await screen.findByText('extra.com')
    const pencilBtns = screen.getAllByTitle('Edit owner')
    await userEvent.click(pencilBtns[0]!)
    expect(screen.getByPlaceholderText('Search user…')).toBeInTheDocument()
  })

  it('shows filtered users in dropdown when typing', async () => {
    render(<VirtualDomainsTab addToast={vi.fn()} />)
    await screen.findByText('extra.com')
    await userEvent.click(screen.getAllByTitle('Edit owner')[0]!)
    await userEvent.type(screen.getByPlaceholderText('Search user…'), 'alice')
    expect(await screen.findByText('alice@weesky.be')).toBeVisible()
  })

  it('calls adminAddVirtualDomainOwner when a user is selected from dropdown', async () => {
    mocks.adminAddVirtualDomainOwner.mockResolvedValue({ domainId: 'ORF', domainName: 'orphan.net', owners: [{ ownerId: 1, ownerEmail: 'alice@weesky.be' }] })
    render(<VirtualDomainsTab addToast={vi.fn()} />)
    await screen.findByText('extra.com')
    await userEvent.click(screen.getAllByTitle('Edit owner')[1]!)
    await userEvent.type(screen.getByPlaceholderText('Search user…'), 'alice')
    const option = await screen.findByRole('button', { name: /alice@weesky\.be/ })
    // The press only holds the caret in the box; the click is what picks, so the keyboard can too.
    fireEvent.click(option)
    await waitFor(() => expect(mocks.adminAddVirtualDomainOwner).toHaveBeenCalledWith('ORF', 1))
  })

  it('shows remove button only for owned domains', async () => {
    render(<VirtualDomainsTab addToast={vi.fn()} />)
    await screen.findByText('extra.com')
    await userEvent.click(screen.getAllByTitle('Edit owner')[0]!)
    expect(screen.getByTitle('Remove owner')).toBeInTheDocument()
  })

  it('does not show remove button for unowned domains', async () => {
    render(<VirtualDomainsTab addToast={vi.fn()} />)
    await screen.findByText('orphan.net')
    await userEvent.click(screen.getAllByTitle('Edit owner')[1]!)
    expect(screen.queryByTitle('Remove owner')).not.toBeInTheDocument()
  })

  it('calls adminRemoveVirtualDomainOwner when Remove owner is clicked', async () => {
    mocks.adminRemoveVirtualDomainOwner.mockResolvedValue(null)
    render(<VirtualDomainsTab addToast={vi.fn()} />)
    await screen.findByText('extra.com')
    await userEvent.click(screen.getAllByTitle('Edit owner')[0]!)
    // The press only holds the caret in the box; the click is what removes, keyboard included.
    fireEvent.click(screen.getByTitle('Remove owner'))
    await waitFor(() => expect(mocks.adminRemoveVirtualDomainOwner).toHaveBeenCalledWith('EXT', 1))
  })

  // Two surfaces, two Escapes: the list the search opened is the nearer one, and cancelling the
  // whole edit on the key that dismisses it throws away the query with it.
  it('closes the user list on Escape and cancels the edit only on the next one', async () => {
    render(<VirtualDomainsTab addToast={vi.fn()} />)
    await screen.findByText('extra.com')
    await userEvent.click(screen.getAllByTitle('Edit owner')[1]!)
    const input = screen.getByPlaceholderText('Search user…')
    await userEvent.type(input, 'alice')
    await screen.findByRole('button', { name: /alice@weesky\.be/ })

    await userEvent.keyboard('{Escape}')

    expect(screen.queryByRole('button', { name: /alice@weesky\.be/ })).not.toBeInTheDocument()
    expect(input).toBeInTheDocument()
    expect(input).toHaveValue('alice')

    await userEvent.keyboard('{Escape}')

    expect(input).not.toBeInTheDocument()
  })

  it('cancels edit on Escape key', async () => {
    render(<VirtualDomainsTab addToast={vi.fn()} />)
    await screen.findByText('extra.com')
    await userEvent.click(screen.getAllByTitle('Edit owner')[0]!)
    const input = screen.getByPlaceholderText('Search user…')
    await userEvent.keyboard('{Escape}')
    expect(input).not.toBeInTheDocument()
  })

  it('shows error toast when loading fails', async () => {
    mocks.adminGetVirtualDomains.mockRejectedValue(new Error('Server error'))
    const addToast = vi.fn()
    render(<VirtualDomainsTab addToast={addToast} />)
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Failed to load virtual domains', 'error'))
  })

  // Server prose never reaches the toast; the local fallback does — see apiErrorMessage.
  it('shows the local fallback when add owner fails', async () => {
    mocks.adminAddVirtualDomainOwner.mockRejectedValue(new Error('Domain not found'))
    const addToast = vi.fn()
    render(<VirtualDomainsTab addToast={addToast} />)
    await screen.findByText('extra.com')
    await userEvent.click(screen.getAllByTitle('Edit owner')[1]!)
    await userEvent.type(screen.getByPlaceholderText('Search user…'), 'alice')
    const option = await screen.findByRole('button', { name: /alice@weesky\.be/ })
    fireEvent.click(option)
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Failed to set owner', 'error'))
  })

  it('re-reads the virtual domains after a refused owner change', async () => {
    mocks.adminAddVirtualDomainOwner.mockRejectedValue(new Error('Domain not found'))
    render(<VirtualDomainsTab addToast={vi.fn()} />)
    await screen.findByText('extra.com')
    await userEvent.click(screen.getAllByTitle('Edit owner')[1]!)
    await userEvent.type(screen.getByPlaceholderText('Search user…'), 'alice')
    fireEvent.click(await screen.findByRole('button', { name: /alice@weesky\.be/ }))
    await waitFor(() => expect(mocks.adminGetVirtualDomains).toHaveBeenCalledTimes(2))
    expect(screen.getByText('orphan.net')).toBeInTheDocument()
  })

  it('handles null response from adminGetVirtualDomains gracefully', async () => {
    mocks.adminGetVirtualDomains.mockResolvedValue(null)
    render(<VirtualDomainsTab addToast={vi.fn()} />)
    await waitFor(() => expect(mocks.adminGetVirtualDomains).toHaveBeenCalledOnce())
    expect(screen.queryByText('extra.com')).not.toBeInTheDocument()
  })
})

// ── Load failures ─────────────────────────────────────────────

describe('Admin tabs — load failures', () => {
  // The domains only feed the dialog: their failure must not cost the admin the accounts list.
  it('lists the users when only the domains fail, with one toast', async () => {
    mocks.adminGetDomains.mockRejectedValue(new Error('Server error'))
    const addToast = vi.fn()
    render(<AccountsTab addToast={addToast} />)
    expect(await screen.findByText('alice@weesky.be')).toBeInTheDocument()
    await settle()
    expect(addToast.mock.calls).toEqual([['Failed to load the domain list', 'error']])
  })

  it('lists the virtual domains when only the users fail, naming the users', async () => {
    mocks.adminGetUsers.mockRejectedValue(new Error('Server error'))
    const addToast = vi.fn()
    render(<VirtualDomainsTab addToast={addToast} />)
    expect(await screen.findByText('extra.com')).toBeInTheDocument()
    await settle()
    expect(addToast.mock.calls).toEqual([['Failed to load the account list', 'error']])
  })

  // A refetch of a query holding no data puts it back to pending: that is not a first load, so
  // neither the list nor an open dialog may go behind the spinner, and the failure is not news.
  it('keeps the list and an open dialog through a refetch of the failed domains', async () => {
    mocks.adminGetDomains.mockRejectedValue(new Error('Server error'))
    const addToast = vi.fn()
    const client = newClient()
    render(<AccountsTab addToast={addToast} />, client)
    await screen.findByText('alice@weesky.be')
    await userEvent.click(screen.getByRole('button', { name: /Add/ }))
    await userEvent.type(screen.getByLabelText('Username'), 'bob')
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

  it('draws no spinner and no second toast when a tab refetches its own failed list', async () => {
    mocks.adminGetDomains.mockRejectedValue(new Error('Server error'))
    const addToast = vi.fn()
    const client = newClient()
    render(<DomainsTab addToast={addToast} />, client)
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Failed to load domains', 'error'))

    const refetch = holdNextCall(mocks.adminGetDomains)

    act(() => { void client.invalidateQueries({ queryKey: ['admin'] }) })
    await settle()
    expect(screen.queryByRole('status', { name: 'Loading' })).not.toBeInTheDocument()
    await act(async () => { refetch.fail() })
    await settle()

    expect(mocks.adminGetDomains).toHaveBeenCalledTimes(2)
    expect(addToast).toHaveBeenCalledTimes(1)
  })

  // The failure belongs to the cached list, not to this mount: a tab opened again over a list
  // that failed earlier is a first load for the admin, and the old failure is not news.
  it('treats a remount over a failed list as a first load', async () => {
    mocks.adminGetDomains.mockRejectedValue(new Error('Server error'))
    const client = newClient()
    const firstToast = vi.fn()
    const { unmount } = render(<DomainsTab addToast={firstToast} />, client)
    await waitFor(() => expect(firstToast).toHaveBeenCalledTimes(1))
    unmount()
    const again = holdNextCall(mocks.adminGetDomains)
    const addToast = vi.fn()

    render(<DomainsTab addToast={addToast} />, client)
    await settle()
    expect(screen.getByRole('status', { name: 'Loading' })).toBeInTheDocument()
    expect(addToast).not.toHaveBeenCalled()
    await act(async () => { again.resolve(MOCK_DOMAINS) })

    expect(await screen.findByText('WSY')).toBeInTheDocument()
    expect(addToast).not.toHaveBeenCalled()
  })

  // A list that could not load says so: "(0)" would state an empty list nobody has seen. The
  // toast is the one announcement; the block is the lasting explanation, and stays silent.
  it.each([
    ['Accounts', () => mocks.adminGetUsers, AccountsTab, 'Could not load the accounts.', 'Failed to load accounts'],
    ['Domains', () => mocks.adminGetDomains, DomainsTab, 'Could not load the domains.', 'Failed to load domains'],
    ['Virtual domains', () => mocks.adminGetVirtualDomains, VirtualDomainsTab, 'Could not load the virtual domains.',
      'Failed to load virtual domains'],
    ['External domains', () => mocks.adminGetExternalDomains, ExternalDomainsTab,
      'Could not load the external domains.', 'Failed to load external domains'],
  ] as const)('%s says its list failed rather than drawing it empty', async (_tab, api, Tab, text, toast) => {
    api().mockRejectedValue(new Error('Server error'))
    function WithToasts() {
      const { toasts, addToast, removeToast, pauseToast, resumeToast } = useToasts()
      return (
        <>
          <Tab addToast={addToast} />
          <Toasts toasts={toasts} onRemove={removeToast} onPause={pauseToast} onResume={resumeToast} />
        </>
      )
    }
    render(<WithToasts />)
    expect(await screen.findByText(text)).toBeInTheDocument()
    expect(screen.queryByText(/\(0\)/)).not.toBeInTheDocument()
    const alerts = await screen.findAllByRole('alert')
    expect(alerts).toHaveLength(1)
    expect(alerts[0]).toHaveTextContent(toast)
  })

  // The server omits `at` for a service the account never logged into.
  it('names a service never logged into without inventing a time', () => {
    const user: AdminUser = { ...MOCK_USERS[0]!, lastLogins: [{ service: 'imap' }] }
    render(<AddEditUserModal user={user} domains={MOCK_DOMAINS} onSave={vi.fn()} onClose={vi.fn()} />)
    expect(screen.getByText('IMAP')).toBeInTheDocument()
    expect(screen.queryByText(/NaN/)).not.toBeInTheDocument()
  })
})
