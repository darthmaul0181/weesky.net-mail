import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { api } from '../api.js'
import {
  QuotaMini,
  AccountPanel,
  DeleteConfirmModal,
  AddEditUserModal,
  AddEditDomainModal,
  AccountsTab,
  DomainsTab,
  AdminModal,
} from './AliasesPage.jsx'

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
  },
  clearToken: vi.fn(),
  setIsAdmin: vi.fn(),
}))

const MB = 1024 * 1024
const GB = 1024 * MB

const MOCK_DOMAINS = [{ id: 'WSY', name: 'weesky.be' }]
const MOCK_USERS = [
  { id: 1, userName: 'alice', domainName: 'weesky.be', domainId: 'WSY', fullName: 'Alice Smith', quotaMb: 1024, active: true, admin: false },
]

beforeEach(() => {
  vi.clearAllMocks()
  api.adminGetUsers.mockResolvedValue(MOCK_USERS)
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
    // two 'Delete' buttons now exist: the icon btn (title) + the modal confirm btn
    await userEvent.click(screen.getAllByRole('button', { name: 'Delete' }).at(-1))
    await waitFor(() => expect(api.adminDeleteUser).toHaveBeenCalledWith(1))
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

  it('shows Coming soon on the Ownerships tab', async () => {
    render(<AdminModal onClose={vi.fn()} addToast={vi.fn()} />)
    await userEvent.click(screen.getByRole('button', { name: 'Ownerships' }))
    expect(screen.getByText('Coming soon')).toBeInTheDocument()
  })
})
