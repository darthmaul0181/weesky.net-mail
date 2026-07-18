import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { api } from '../api.js'
import {
  QuotaMini,
  AccountPanel,
  AddEditUserModal,
  AddEditDomainModal,
  AccountsTab,
  DomainsTab,
  VirtualDomainsTab,
  AdminModal,
} from './AliasesPage.jsx'
import DeleteConfirmModal from '../components/DeleteConfirmModal.jsx'

vi.mock('../api.js', () => ({
  api: {
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
  },
  clearSession: vi.fn(),
  setIsAdmin: vi.fn(),
}))

const MB = 1024 * 1024
const GB = 1024 * MB

const MOCK_DOMAINS = [{ id: 'WSY', name: 'weesky.be' }]
const MOCK_USERS = [
  { id: 1, userName: 'alice', domainName: 'weesky.be', domainId: 'WSY', fullName: 'Alice Smith', quotaMb: 1024, active: true, admin: false },
]
const MOCK_VIRTUAL_DOMAINS = [
  { domainId: 'EXT', domainName: 'extra.com', owners: [{ ownerId: 1, ownerEmail: 'alice@weesky.be' }] },
  { domainId: 'ORF', domainName: 'orphan.net', owners: [] },
]

beforeEach(() => {
  vi.clearAllMocks()
  api.adminGetUsers.mockResolvedValue(MOCK_USERS)
  api.adminGetVirtualDomains.mockResolvedValue(MOCK_VIRTUAL_DOMAINS)
  api.adminGetDomains.mockResolvedValue(MOCK_DOMAINS)
  api.adminGetUserQuota.mockRejectedValue(new Error('unavailable'))
})

// ── QuotaMini ─────────────────────────────────────────────────

describe('QuotaMini', () => {
  it('renders — when quota is null', () => {
    render(<QuotaMini quota={null} />)
    expect(screen.getByText('—')).toBeInTheDocument()
  })

  it('renders — when storageBytesLimit is zero', () => {
    render(<QuotaMini quota={{ storageBytesUsed: 0, storageBytesLimit: 0 }} />)
    expect(screen.getByText('—')).toBeInTheDocument()
  })

  it('displays used / total in MB when values are under 1 GB', () => {
    render(<QuotaMini quota={{ storageBytesUsed: 50 * MB, storageBytesLimit: 200 * MB }} />)
    expect(screen.getByText(/50\.0 \/ 200 MB/)).toBeInTheDocument()
  })

  it('displays used / total in GB when values reach 1 GB', () => {
    render(<QuotaMini quota={{ storageBytesUsed: 1 * GB, storageBytesLimit: 2 * GB }} />)
    expect(screen.getByText(/1\.0 \/ 2\.0 GB/)).toBeInTheDocument()
  })

  it('applies is-danger class when usage is ≥ 90%', () => {
    const { container } = render(
      <QuotaMini quota={{ storageBytesUsed: 92 * MB, storageBytesLimit: 100 * MB }} />
    )
    expect(container.querySelector('.panel-quota-bar')).toHaveClass('is-danger')
  })

  it('applies is-warn class when usage is between 75% and 90%', () => {
    const { container } = render(
      <QuotaMini quota={{ storageBytesUsed: 80 * MB, storageBytesLimit: 100 * MB }} />
    )
    expect(container.querySelector('.panel-quota-bar')).toHaveClass('is-warn')
  })
})

// ── AccountPanel — isAdmin behaviour ─────────────────────────

describe('AccountPanel isAdmin', () => {
  function renderPanel(isAdmin = false, onAdmin = vi.fn()) {
    return render(
      <AccountPanel
        initials="WB"
        fullName="Test User"
        primaryEmail="test@weesky.be"
        subDomains={[]}
        quota={null}
        onLogout={vi.fn()}
        onChangePassword={vi.fn()}
        onAdmin={onAdmin}
        isAdmin={isAdmin}
        alphaMode={false}
        onAlphaModeChange={vi.fn()}
        onFullNameChange={vi.fn()}
      />
    )
  }

  it('hides the Administration button when isAdmin is false', async () => {
    renderPanel(false)
    await userEvent.click(screen.getByRole('button', { name: 'WB' }))
    expect(screen.queryByRole('button', { name: 'Administration' })).not.toBeInTheDocument()
  })

  it('shows the Administration button when isAdmin is true', async () => {
    renderPanel(true)
    await userEvent.click(screen.getByRole('button', { name: 'WB' }))
    expect(screen.getByRole('button', { name: 'Administration' })).toBeInTheDocument()
  })

  it('calls onAdmin when the Administration button is clicked', async () => {
    const onAdmin = vi.fn()
    renderPanel(true, onAdmin)
    await userEvent.click(screen.getByRole('button', { name: 'WB' }))
    await userEvent.click(screen.getByRole('button', { name: 'Administration' }))
    expect(onAdmin).toHaveBeenCalledOnce()
  })
})

// ── DeleteConfirmModal ────────────────────────────────────────

describe('DeleteConfirmModal', () => {
  it('renders the entity label', () => {
    render(<DeleteConfirmModal entityLabel="alice@weesky.be" onConfirm={vi.fn()} onClose={vi.fn()} loading={false} />)
    expect(screen.getByText('alice@weesky.be')).toBeInTheDocument()
  })

  it('calls onClose when Cancel is clicked', async () => {
    const onClose = vi.fn()
    render(<DeleteConfirmModal entityLabel="x" onConfirm={vi.fn()} onClose={onClose} loading={false} />)
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(onClose).toHaveBeenCalledOnce()
  })

  it('calls onConfirm when Delete is clicked', async () => {
    const onConfirm = vi.fn()
    render(<DeleteConfirmModal entityLabel="x" onConfirm={onConfirm} onClose={vi.fn()} loading={false} />)
    await userEvent.click(screen.getByRole('button', { name: 'Delete' }))
    expect(onConfirm).toHaveBeenCalledOnce()
  })

  it('disables both buttons while loading', () => {
    render(<DeleteConfirmModal entityLabel="x" onConfirm={vi.fn()} onClose={vi.fn()} loading={true} />)
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled()
  })
})

// ── AddEditUserModal — create mode ────────────────────────────

describe('AddEditUserModal — create mode', () => {
  function renderCreate(props = {}) {
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
    await userEvent.type(screen.getAllByRole('textbox')[0], 'alice')
    expect(screen.getByRole('button', { name: 'Create account' })).toBeDisabled()
  })

  it('calls adminCreateUser with correct payload on submit', async () => {
    api.adminCreateUser.mockResolvedValue({})
    const onSave = vi.fn()
    const { container } = renderCreate({ onSave })
    await userEvent.type(screen.getAllByRole('textbox')[0], 'alice')
    await userEvent.type(container.querySelector('input[type="password"]'), 'secret')
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }))
    await waitFor(() =>
      expect(api.adminCreateUser).toHaveBeenCalledWith(
        expect.objectContaining({ userName: 'alice', domainId: 'WSY', password: 'secret' })
      )
    )
    await waitFor(() => expect(onSave).toHaveBeenCalledOnce())
  })

  it('shows error message when api call fails', async () => {
    api.adminCreateUser.mockRejectedValue(new Error('Duplicate user'))
    const { container } = renderCreate()
    await userEvent.type(screen.getAllByRole('textbox')[0], 'alice')
    await userEvent.type(container.querySelector('input[type="password"]'), 'pw')
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }))
    await waitFor(() => expect(screen.getByText('Duplicate user')).toBeInTheDocument())
  })
})

// ── AddEditUserModal — edit mode ──────────────────────────────

describe('AddEditUserModal — edit mode', () => {
  const EDIT_USER = { id: 1, userName: 'alice', domainId: 'WSY', domainName: 'weesky.be', fullName: 'Alice Smith', quotaMb: 1024, active: true, admin: false }

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
    const { container } = renderEdit()
    expect(container.querySelector('input[type="password"]').placeholder).toBe('leave blank to keep')
  })

  it('calls adminUpdateUser with null password when password field is left empty', async () => {
    api.adminUpdateUser.mockResolvedValue({})
    renderEdit()
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    await waitFor(() =>
      expect(api.adminUpdateUser).toHaveBeenCalledWith(
        1,
        expect.objectContaining({ password: null })
      )
    )
  })

  it('calls adminUpdateUser with new password when one is provided', async () => {
    api.adminUpdateUser.mockResolvedValue({})
    const { container } = renderEdit()
    await userEvent.type(container.querySelector('input[type="password"]'), 'newpass')
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    await waitFor(() =>
      expect(api.adminUpdateUser).toHaveBeenCalledWith(
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
    await userEvent.type(screen.getAllByRole('textbox')[0], 'TST')
    expect(screen.getByRole('button', { name: 'Create domain' })).toBeDisabled()
  })

  it('submit button is disabled when only name is filled', async () => {
    renderCreate()
    await userEvent.type(screen.getAllByRole('textbox')[1], 'test.com')
    expect(screen.getByRole('button', { name: 'Create domain' })).toBeDisabled()
  })

  it('submit button is enabled when both fields are filled with a valid domain', async () => {
    renderCreate()
    await userEvent.type(screen.getAllByRole('textbox')[0], 'TST')
    await userEvent.type(screen.getAllByRole('textbox')[1], 'test.com')
    expect(screen.getByRole('button', { name: 'Create domain' })).not.toBeDisabled()
  })

  it('submit button is disabled when domain name is syntactically invalid', async () => {
    renderCreate()
    await userEvent.type(screen.getAllByRole('textbox')[0], 'TST')
    await userEvent.type(screen.getAllByRole('textbox')[1], 'notadomain')
    expect(screen.getByRole('button', { name: 'Create domain' })).toBeDisabled()
  })

  it('name input gets is-error class when domain name is invalid', async () => {
    renderCreate()
    const nameInput = screen.getAllByRole('textbox')[1]
    await userEvent.type(nameInput, 'notadomain')
    expect(nameInput).toHaveClass('is-error')
  })

  it('name input has no is-error class when domain name is valid', async () => {
    renderCreate()
    const nameInput = screen.getAllByRole('textbox')[1]
    await userEvent.type(nameInput, 'test.com')
    expect(nameInput).not.toHaveClass('is-error')
  })

  it('name input has no is-error class when field is empty', () => {
    renderCreate()
    expect(screen.getAllByRole('textbox')[1]).not.toHaveClass('is-error')
  })

  it('calls adminCreateDomain with correct payload on submit', async () => {
    api.adminCreateDomain.mockResolvedValue({})
    const onSave = vi.fn()
    renderCreate({ onSave })
    const [idInput, nameInput] = screen.getAllByRole('textbox')
    await userEvent.type(idInput, 'TST')
    await userEvent.type(nameInput, 'test.com')
    await userEvent.click(screen.getByRole('button', { name: 'Create domain' }))
    await waitFor(() =>
      expect(api.adminCreateDomain).toHaveBeenCalledWith(
        expect.objectContaining({ id: 'TST', name: 'test.com' })
      )
    )
    await waitFor(() => expect(onSave).toHaveBeenCalledOnce())
  })
})

// ── AddEditDomainModal — edit mode ────────────────────────────

describe('AddEditDomainModal — edit mode', () => {
  const EDIT_DOMAIN = { id: 'WSY', name: 'weesky.be' }

  it('id field is disabled', () => {
    render(<AddEditDomainModal domain={EDIT_DOMAIN} onSave={vi.fn()} onClose={vi.fn()} />)
    const [idInput] = screen.getAllByRole('textbox')
    expect(idInput).toBeDisabled()
  })

  it('calls adminUpdateDomain on submit', async () => {
    api.adminUpdateDomain.mockResolvedValue({})
    render(<AddEditDomainModal domain={EDIT_DOMAIN} onSave={vi.fn()} onClose={vi.fn()} />)
    const nameInput = screen.getAllByRole('textbox')[1]
    await userEvent.clear(nameInput)
    await userEvent.type(nameInput, 'new.weesky.be')
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    await waitFor(() =>
      expect(api.adminUpdateDomain).toHaveBeenCalledWith('WSY', expect.objectContaining({ name: 'new.weesky.be' }))
    )
  })
})

// ── AccountsTab ───────────────────────────────────────────────

describe('AccountsTab', () => {
  it('fetches users and domains on mount', async () => {
    render(<AccountsTab addToast={vi.fn()} />)
    await waitFor(() => expect(api.adminGetUsers).toHaveBeenCalledOnce())
    expect(api.adminGetDomains).toHaveBeenCalledOnce()
  })

  it('renders the user list after loading', async () => {
    render(<AccountsTab addToast={vi.fn()} />)
    expect(await screen.findByText('alice@weesky.be')).toBeInTheDocument()
    expect(screen.getByText('Alice Smith')).toBeInTheDocument()
  })

  it('filters the list by username when searching', async () => {
    api.adminGetUsers.mockResolvedValue([
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
    api.adminGetUsers.mockResolvedValue([
      ...MOCK_USERS,
      { id: 2, userName: 'bob', domainName: 'weesky.be', domainId: 'WSY', fullName: 'Bob Jones', quotaMb: 1024, active: true, admin: false },
    ])
    render(<AccountsTab addToast={vi.fn()} />)
    await screen.findByText('alice@weesky.be')
    await userEvent.type(screen.getByPlaceholderText('Search…'), 'Jones')
    expect(screen.queryByText('alice@weesky.be')).not.toBeInTheDocument()
    expect(screen.getByText('bob@weesky.be')).toBeInTheDocument()
  })

  it('calls adminDeleteUser when delete is confirmed', async () => {
    api.adminDeleteUser.mockResolvedValue(null)
    render(<AccountsTab addToast={vi.fn()} />)
    await screen.findByText('alice@weesky.be')
    await userEvent.click(screen.getByTitle('Delete'))
    await userEvent.click(screen.getAllByRole('button', { name: 'Delete' }).at(-1))
    await waitFor(() => expect(api.adminDeleteUser).toHaveBeenCalledWith(1))
  })

  it('shows error toast when user delete fails', async () => {
    api.adminDeleteUser.mockRejectedValue(new Error('Cannot delete'))
    const addToast = vi.fn()
    render(<AccountsTab addToast={addToast} />)
    await screen.findByText('alice@weesky.be')
    await userEvent.click(screen.getByTitle('Delete'))
    await userEvent.click(screen.getAllByRole('button', { name: 'Delete' }).at(-1))
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Cannot delete', 'error'))
  })

  it('shows error toast when loading fails', async () => {
    api.adminGetUsers.mockRejectedValue(new Error('Server error'))
    const addToast = vi.fn()
    render(<AccountsTab addToast={addToast} />)
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Failed to load accounts', 'error'))
  })

  it('opens AddEditUserModal when Add is clicked', async () => {
    render(<AccountsTab addToast={vi.fn()} />)
    await screen.findByText('alice@weesky.be')
    await userEvent.click(screen.getByRole('button', { name: /Add/ }))
    expect(screen.getByRole('button', { name: 'Create account' })).toBeInTheDocument()
  })

  it('shows success toast after user is created', async () => {
    api.adminCreateUser.mockResolvedValue({})
    const addToast = vi.fn()
    const { container } = render(<AccountsTab addToast={addToast} />)
    await screen.findByText('alice@weesky.be')
    await userEvent.click(screen.getByRole('button', { name: /Add/ }))
    await userEvent.type(screen.getAllByRole('textbox')[0], 'newuser')
    await userEvent.type(container.querySelector('input[type="password"]'), 'pw')
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
    api.adminUpdateUser.mockResolvedValue({})
    const addToast = vi.fn()
    render(<AccountsTab addToast={addToast} />)
    await screen.findByText('alice@weesky.be')
    await userEvent.click(screen.getByTitle('Edit'))
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Account updated'))
  })

  it('cancel closes the delete modal without deleting', async () => {
    render(<AccountsTab addToast={vi.fn()} />)
    await screen.findByText('alice@weesky.be')
    await userEvent.click(screen.getByTitle('Delete'))
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(api.adminDeleteUser).not.toHaveBeenCalled()
    expect(screen.queryByText('Confirm deletion')).not.toBeInTheDocument()
  })

  it('closes the add user modal when ✕ is clicked', async () => {
    render(<AccountsTab addToast={vi.fn()} />)
    await screen.findByText('alice@weesky.be')
    await userEvent.click(screen.getByRole('button', { name: /Add/ }))
    expect(screen.getByRole('button', { name: 'Create account' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: '✕' }))
    expect(screen.queryByRole('button', { name: 'Create account' })).not.toBeInTheDocument()
  })

  it('closes the edit user modal when ✕ is clicked', async () => {
    render(<AccountsTab addToast={vi.fn()} />)
    await screen.findByText('alice@weesky.be')
    await userEvent.click(screen.getByTitle('Edit'))
    expect(screen.getByRole('button', { name: 'Save changes' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: '✕' }))
    expect(screen.queryByRole('button', { name: 'Save changes' })).not.toBeInTheDocument()
  })

  it('shows user quota when adminGetUserQuota resolves', async () => {
    api.adminGetUserQuota.mockResolvedValue({ storageBytesUsed: 50 * MB, storageBytesLimit: 200 * MB })
    render(<AccountsTab addToast={vi.fn()} />)
    await screen.findByText('alice@weesky.be')
    await waitFor(() => expect(api.adminGetUserQuota).toHaveBeenCalledWith(1))
  })

  it('handles null response from adminGetUsers gracefully', async () => {
    api.adminGetUsers.mockResolvedValue(null)
    render(<AccountsTab addToast={vi.fn()} />)
    await waitFor(() => expect(api.adminGetUsers).toHaveBeenCalledOnce())
    expect(screen.queryByText('alice@weesky.be')).not.toBeInTheDocument()
  })

  it('uses fallback message when delete error has no message', async () => {
    api.adminDeleteUser.mockRejectedValue(new Error())
    const addToast = vi.fn()
    render(<AccountsTab addToast={addToast} />)
    await screen.findByText('alice@weesky.be')
    await userEvent.click(screen.getByTitle('Delete'))
    await userEvent.click(screen.getAllByRole('button', { name: 'Delete' }).at(-1))
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Failed to delete user', 'error'))
  })
})

// ── DomainsTab ────────────────────────────────────────────────

describe('DomainsTab', () => {
  it('fetches domains on mount', async () => {
    render(<DomainsTab addToast={vi.fn()} />)
    await waitFor(() => expect(api.adminGetDomains).toHaveBeenCalledOnce())
  })

  it('renders the domain list', async () => {
    render(<DomainsTab addToast={vi.fn()} />)
    expect(await screen.findByText('WSY')).toBeInTheDocument()
    expect(screen.getByText('weesky.be')).toBeInTheDocument()
  })

  it('shows error toast when loading fails', async () => {
    api.adminGetDomains.mockRejectedValue(new Error('Server error'))
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
    api.adminCreateDomain.mockResolvedValue({})
    const addToast = vi.fn()
    render(<DomainsTab addToast={addToast} />)
    await screen.findByText('WSY')
    await userEvent.click(screen.getByRole('button', { name: /Add/ }))
    const [idInput, nameInput] = screen.getAllByRole('textbox')
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
    api.adminUpdateDomain.mockResolvedValue({})
    const addToast = vi.fn()
    render(<DomainsTab addToast={addToast} />)
    await screen.findByText('WSY')
    await userEvent.click(screen.getByTitle('Edit'))
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Domain updated'))
  })

  it('calls adminDeleteDomain and shows toast after deletion', async () => {
    api.adminDeleteDomain.mockResolvedValue(null)
    const addToast = vi.fn()
    render(<DomainsTab addToast={addToast} />)
    await screen.findByText('WSY')
    await userEvent.click(screen.getByTitle('Delete'))
    await userEvent.click(screen.getAllByRole('button', { name: 'Delete' }).at(-1))
    await waitFor(() => expect(api.adminDeleteDomain).toHaveBeenCalledWith('WSY'))
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Domain weesky.be deleted'))
  })

  it('shows error toast when delete fails', async () => {
    api.adminDeleteDomain.mockRejectedValue(new Error('Has users'))
    const addToast = vi.fn()
    render(<DomainsTab addToast={addToast} />)
    await screen.findByText('WSY')
    await userEvent.click(screen.getByTitle('Delete'))
    await userEvent.click(screen.getAllByRole('button', { name: 'Delete' }).at(-1))
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Has users', 'error'))
  })

  it('cancel closes delete modal without deleting', async () => {
    render(<DomainsTab addToast={vi.fn()} />)
    await screen.findByText('WSY')
    await userEvent.click(screen.getByTitle('Delete'))
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(api.adminDeleteDomain).not.toHaveBeenCalled()
    expect(screen.queryByText('Confirm deletion')).not.toBeInTheDocument()
  })

  it('closes the add domain modal when ✕ is clicked', async () => {
    render(<DomainsTab addToast={vi.fn()} />)
    await screen.findByText('WSY')
    await userEvent.click(screen.getByRole('button', { name: /Add/ }))
    expect(screen.getByRole('button', { name: 'Create domain' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: '✕' }))
    expect(screen.queryByRole('button', { name: 'Create domain' })).not.toBeInTheDocument()
  })

  it('closes the edit domain modal when ✕ is clicked', async () => {
    render(<DomainsTab addToast={vi.fn()} />)
    await screen.findByText('WSY')
    await userEvent.click(screen.getByTitle('Edit'))
    expect(screen.getByRole('button', { name: 'Save changes' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: '✕' }))
    expect(screen.queryByRole('button', { name: 'Save changes' })).not.toBeInTheDocument()
  })

  it('handles null response from adminGetDomains gracefully', async () => {
    api.adminGetDomains.mockResolvedValue(null)
    render(<DomainsTab addToast={vi.fn()} />)
    await waitFor(() => expect(api.adminGetDomains).toHaveBeenCalledOnce())
    expect(screen.queryByText('WSY')).not.toBeInTheDocument()
  })

  it('uses fallback message when domain delete error has no message', async () => {
    api.adminDeleteDomain.mockRejectedValue(new Error())
    const addToast = vi.fn()
    render(<DomainsTab addToast={addToast} />)
    await screen.findByText('WSY')
    await userEvent.click(screen.getByTitle('Delete'))
    await userEvent.click(screen.getAllByRole('button', { name: 'Delete' }).at(-1))
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Failed to delete domain', 'error'))
  })
})

// ── AddEditUserModal — additional field changes ───────────────

describe('AddEditUserModal — field changes', () => {
  const TWO_DOMAINS = [
    { id: 'WSY', name: 'weesky.be' },
    { id: 'EXM', name: 'example.com' },
  ]

  it('changing the domain select updates domainId in the payload', async () => {
    api.adminCreateUser.mockResolvedValue({})
    const onSave = vi.fn()
    const { container } = render(
      <AddEditUserModal user={null} domains={TWO_DOMAINS} onSave={onSave} onClose={vi.fn()} />
    )
    await userEvent.type(screen.getAllByRole('textbox')[0], 'alice')
    await userEvent.type(container.querySelector('input[type="password"]'), 'pw')
    await userEvent.selectOptions(screen.getByRole('combobox'), 'EXM')
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }))
    await waitFor(() =>
      expect(api.adminCreateUser).toHaveBeenCalledWith(
        expect.objectContaining({ domainId: 'EXM' })
      )
    )
  })

  it('changing the full name field updates the value', async () => {
    render(
      <AddEditUserModal user={null} domains={MOCK_DOMAINS} onSave={vi.fn()} onClose={vi.fn()} />
    )
    const fullNameInput = screen.getAllByRole('textbox')[1]
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
    api.adminCreateUser.mockResolvedValue({})
    const { container } = renderCreate()
    await userEvent.type(screen.getAllByRole('textbox')[0], 'alice')
    await userEvent.type(container.querySelector('input[type="password"]'), 'pw')
    const [activeCheckbox] = screen.getAllByRole('checkbox')
    await userEvent.click(activeCheckbox) // uncheck active (was true by default)
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }))
    await waitFor(() =>
      expect(api.adminCreateUser).toHaveBeenCalledWith(
        expect.objectContaining({ active: false })
      )
    )
  })

  it('checking admin sets admin:true in the payload', async () => {
    api.adminCreateUser.mockResolvedValue({})
    const { container } = renderCreate()
    await userEvent.type(screen.getAllByRole('textbox')[0], 'alice')
    await userEvent.type(container.querySelector('input[type="password"]'), 'pw')
    const [, adminCheckbox] = screen.getAllByRole('checkbox')
    await userEvent.click(adminCheckbox) // check admin (was false by default)
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }))
    await waitFor(() =>
      expect(api.adminCreateUser).toHaveBeenCalledWith(
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
  it('shows error when create API fails', async () => {
    api.adminCreateDomain.mockRejectedValue(new Error('Invalid ID'))
    render(<AddEditDomainModal domain={null} onSave={vi.fn()} onClose={vi.fn()} />)
    const [idInput, nameInput] = screen.getAllByRole('textbox')
    await userEvent.type(idInput, 'TST')
    await userEvent.type(nameInput, 'test.com')
    await userEvent.click(screen.getByRole('button', { name: 'Create domain' }))
    await waitFor(() => expect(screen.getByText('Invalid ID')).toBeInTheDocument())
  })

  it('shows error when update API fails', async () => {
    api.adminUpdateDomain.mockRejectedValue(new Error('Not found'))
    render(<AddEditDomainModal domain={{ id: 'WSY', name: 'weesky.be' }} onSave={vi.fn()} onClose={vi.fn()} />)
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    await waitFor(() => expect(screen.getByText('Not found')).toBeInTheDocument())
  })
})

// ── AdminModal ────────────────────────────────────────────────

describe('AdminModal', () => {
  it('shows the Accounts tab as active by default', async () => {
    render(<AdminModal onClose={vi.fn()} addToast={vi.fn()} />)
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Accounts' })).toHaveClass('is-active')
    )
  })

  it('switches to Domains tab when clicked', async () => {
    render(<AdminModal onClose={vi.fn()} addToast={vi.fn()} />)
    await userEvent.click(screen.getByRole('button', { name: 'Domains' }))
    expect(screen.getByRole('button', { name: 'Domains' })).toHaveClass('is-active')
    expect(await screen.findByText('WSY')).toBeInTheDocument()
  })

  it('switches to Virtual domains tab and loads alias domains', async () => {
    render(<AdminModal onClose={vi.fn()} addToast={vi.fn()} />)
    await userEvent.click(screen.getByRole('button', { name: 'Virtual domains' }))
    expect(screen.getByRole('button', { name: 'Virtual domains' })).toHaveClass('is-active')
    expect(await screen.findByText('extra.com')).toBeInTheDocument()
  })

  it('switches back to Accounts tab after visiting Domains', async () => {
    render(<AdminModal onClose={vi.fn()} addToast={vi.fn()} />)
    await userEvent.click(screen.getByRole('button', { name: 'Domains' }))
    await userEvent.click(screen.getByRole('button', { name: 'Accounts' }))
    expect(screen.getByRole('button', { name: 'Accounts' })).toHaveClass('is-active')
  })
})

// ── VirtualDomainsTab ─────────────────────────────────────────

describe('VirtualDomainsTab', () => {
  it('fetches virtual domains and users on mount', async () => {
    render(<VirtualDomainsTab addToast={vi.fn()} />)
    await waitFor(() => expect(api.adminGetVirtualDomains).toHaveBeenCalledOnce())
    expect(api.adminGetUsers).toHaveBeenCalledOnce()
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
    api.adminGetVirtualDomains.mockResolvedValue([])
    render(<VirtualDomainsTab addToast={vi.fn()} />)
    expect(await screen.findByText('No virtual alias domains')).toBeInTheDocument()
  })

  it('shows search input when pencil is clicked', async () => {
    render(<VirtualDomainsTab addToast={vi.fn()} />)
    await screen.findByText('extra.com')
    const pencilBtns = screen.getAllByTitle('Edit owner')
    await userEvent.click(pencilBtns[0])
    expect(screen.getByPlaceholderText('Search user…')).toBeInTheDocument()
  })

  it('shows filtered users in dropdown when typing', async () => {
    render(<VirtualDomainsTab addToast={vi.fn()} />)
    await screen.findByText('extra.com')
    await userEvent.click(screen.getAllByTitle('Edit owner')[0])
    await userEvent.type(screen.getByPlaceholderText('Search user…'), 'alice')
    expect(await screen.findByText('alice@weesky.be')).toBeVisible()
  })

  it('calls adminAddVirtualDomainOwner when a user is selected from dropdown', async () => {
    api.adminAddVirtualDomainOwner.mockResolvedValue({ domainId: 'ORF', domainName: 'orphan.net', owners: [{ ownerId: 1, ownerEmail: 'alice@weesky.be' }] })
    render(<VirtualDomainsTab addToast={vi.fn()} />)
    await screen.findByText('extra.com')
    await userEvent.click(screen.getAllByTitle('Edit owner')[1])
    await userEvent.type(screen.getByPlaceholderText('Search user…'), 'alice')
    const option = await screen.findByRole('button', { name: /alice@weesky\.be/ })
    fireEvent.mouseDown(option)
    await waitFor(() => expect(api.adminAddVirtualDomainOwner).toHaveBeenCalledWith('ORF', 1))
  })

  it('shows remove button only for owned domains', async () => {
    render(<VirtualDomainsTab addToast={vi.fn()} />)
    await screen.findByText('extra.com')
    await userEvent.click(screen.getAllByTitle('Edit owner')[0])
    expect(screen.getByTitle('Remove owner')).toBeInTheDocument()
  })

  it('does not show remove button for unowned domains', async () => {
    render(<VirtualDomainsTab addToast={vi.fn()} />)
    await screen.findByText('orphan.net')
    await userEvent.click(screen.getAllByTitle('Edit owner')[1])
    expect(screen.queryByTitle('Remove owner')).not.toBeInTheDocument()
  })

  it('calls adminRemoveVirtualDomainOwner when Remove owner is clicked', async () => {
    api.adminRemoveVirtualDomainOwner.mockResolvedValue(null)
    render(<VirtualDomainsTab addToast={vi.fn()} />)
    await screen.findByText('extra.com')
    await userEvent.click(screen.getAllByTitle('Edit owner')[0])
    fireEvent.mouseDown(screen.getByTitle('Remove owner'))
    await waitFor(() => expect(api.adminRemoveVirtualDomainOwner).toHaveBeenCalledWith('EXT', 1))
  })

  it('cancels edit on Escape key', async () => {
    render(<VirtualDomainsTab addToast={vi.fn()} />)
    await screen.findByText('extra.com')
    await userEvent.click(screen.getAllByTitle('Edit owner')[0])
    const input = screen.getByPlaceholderText('Search user…')
    await userEvent.keyboard('{Escape}')
    expect(input).not.toBeInTheDocument()
  })

  it('shows error toast when loading fails', async () => {
    api.adminGetVirtualDomains.mockRejectedValue(new Error('Server error'))
    const addToast = vi.fn()
    render(<VirtualDomainsTab addToast={addToast} />)
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Failed to load virtual domains', 'error'))
  })

  it('shows error toast when add owner fails', async () => {
    api.adminAddVirtualDomainOwner.mockRejectedValue(new Error('Domain not found'))
    const addToast = vi.fn()
    render(<VirtualDomainsTab addToast={addToast} />)
    await screen.findByText('extra.com')
    await userEvent.click(screen.getAllByTitle('Edit owner')[1])
    await userEvent.type(screen.getByPlaceholderText('Search user…'), 'alice')
    const option = await screen.findByRole('button', { name: /alice@weesky\.be/ })
    fireEvent.mouseDown(option)
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Domain not found', 'error'))
  })

  it('handles null response from adminGetVirtualDomains gracefully', async () => {
    api.adminGetVirtualDomains.mockResolvedValue(null)
    render(<VirtualDomainsTab addToast={vi.fn()} />)
    await waitFor(() => expect(api.adminGetVirtualDomains).toHaveBeenCalledOnce())
    expect(screen.queryByText('extra.com')).not.toBeInTheDocument()
  })
})
