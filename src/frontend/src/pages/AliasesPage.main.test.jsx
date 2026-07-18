import { render, screen, waitFor, fireEvent, act } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { api, clearSession } from '../api.js'
import AliasesPage, {
  ChangePasswordModal,
  QuotaBlock,
  AccountPanel,
} from './AliasesPage.jsx'
import Toasts from '../components/Toasts.jsx'

vi.mock('../api.js', () => ({
  api: {
    getAccount: vi.fn(),
    getQuota: vi.fn(),
    getAliases: vi.fn(),
    createAlias: vi.fn(),
    deleteAlias: vi.fn(),
    changePassword: vi.fn(),
    changeFullName: vi.fn(),
    logout: vi.fn(),
    adminGetUsers: vi.fn(),
    adminGetDomains: vi.fn(),
    adminGetUserQuota: vi.fn(),
  },
  clearSession: vi.fn(),
  setIsAdmin: vi.fn(),
}))

const MB = 1024 * 1024
const GB = 1024 * MB

const ACCOUNT = {
  userName: 'john',
  fullName: 'John Doe',
  mailbox: 'WSY',
  domains: [{ id: 'WSY', name: 'weesky.be' }],
  isAdmin: false,
}
const QUOTA = { storageBytesUsed: 100 * MB, storageBytesLimit: 1024 * MB }
const ALIASES = [
  { name: 'alias1', domain: 'weesky.be' },
  { name: 'alias2', domain: 'weesky.be' },
]

beforeEach(() => {
  vi.clearAllMocks()
  localStorage.clear()
  api.getAccount.mockResolvedValue(ACCOUNT)
  api.getQuota.mockResolvedValue(QUOTA)
  api.getAliases.mockResolvedValue(ALIASES)
  api.adminGetUsers.mockResolvedValue([])
  api.adminGetDomains.mockResolvedValue([])
  api.adminGetUserQuota.mockRejectedValue(new Error('n/a'))
})

// ── Toasts ────────────────────────────────────────────────────

describe('Toasts', () => {
  it('renders nothing when the list is empty', () => {
    const { container } = render(<Toasts toasts={[]} onRemove={vi.fn()} />)
    expect(container.firstChild).toBeNull()
  })

  it('renders a success toast without a close button', () => {
    render(<Toasts toasts={[{ id: 1, message: 'Done', type: 'success' }]} onRemove={vi.fn()} />)
    expect(screen.getByText('Done')).toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('renders an error toast with a close button', () => {
    render(<Toasts toasts={[{ id: 1, message: 'Oops', type: 'error' }]} onRemove={vi.fn()} />)
    expect(screen.getByText('Oops')).toBeInTheDocument()
    expect(screen.getByRole('button')).toBeInTheDocument()
  })

  it('calls onRemove with the toast id when the close button is clicked', async () => {
    const onRemove = vi.fn()
    render(<Toasts toasts={[{ id: 42, message: 'Err', type: 'error' }]} onRemove={onRemove} />)
    await userEvent.click(screen.getByRole('button'))
    expect(onRemove).toHaveBeenCalledWith(42)
  })
})

// ── QuotaBlock ────────────────────────────────────────────────

describe('QuotaBlock', () => {
  it('renders nothing when quota is null', () => {
    const { container } = render(<QuotaBlock quota={null} />)
    expect(container.firstChild).toBeNull()
  })

  it('renders nothing when storageBytesLimit is 0', () => {
    const { container } = render(<QuotaBlock quota={{ storageBytesUsed: 0, storageBytesLimit: 0 }} />)
    expect(container.firstChild).toBeNull()
  })

  it('shows MB unit when usage is under 1 GB', () => {
    const { container } = render(<QuotaBlock quota={{ storageBytesUsed: 200 * MB, storageBytesLimit: 500 * MB }} />)
    // format(200) uses toFixed(0) because 200 >= 100 → "200 MB"
    expect(container.querySelector('.panel-quota-used').textContent).toMatch(/200\s+MB/)
    expect(container.querySelector('.panel-quota-total').textContent).toMatch(/500\s+MB/)
  })

  it('shows GB unit when any value reaches 1 GB', () => {
    const { container } = render(<QuotaBlock quota={{ storageBytesUsed: 1 * GB, storageBytesLimit: 2 * GB }} />)
    expect(container.querySelector('.panel-quota-used').textContent).toMatch(/GB/)
    expect(container.querySelector('.panel-quota-total').textContent).toMatch(/GB/)
  })

  it('shows percentage', () => {
    render(<QuotaBlock quota={{ storageBytesUsed: 50 * MB, storageBytesLimit: 100 * MB }} />)
    expect(screen.getByText('50%')).toBeInTheDocument()
  })

  it('applies is-danger class at ≥ 90% usage', () => {
    const { container } = render(
      <QuotaBlock quota={{ storageBytesUsed: 95 * MB, storageBytesLimit: 100 * MB }} />
    )
    expect(container.querySelector('.panel-quota-bar')).toHaveClass('is-danger')
  })

  it('applies is-warn class at 75–89% usage', () => {
    const { container } = render(
      <QuotaBlock quota={{ storageBytesUsed: 80 * MB, storageBytesLimit: 100 * MB }} />
    )
    expect(container.querySelector('.panel-quota-bar')).toHaveClass('is-warn')
  })

  it('has no level class below 75%', () => {
    const { container } = render(
      <QuotaBlock quota={{ storageBytesUsed: 40 * MB, storageBytesLimit: 100 * MB }} />
    )
    const bar = container.querySelector('.panel-quota-bar')
    expect(bar).not.toHaveClass('is-danger')
    expect(bar).not.toHaveClass('is-warn')
  })
})

// ── ChangePasswordModal ───────────────────────────────────────

describe('ChangePasswordModal', () => {
  it('renders three password fields', () => {
    const { container } = render(<ChangePasswordModal onClose={vi.fn()} />)
    expect(container.querySelectorAll('input[type="password"]')).toHaveLength(3)
  })

  it('calls onClose when the ✕ button is clicked', async () => {
    const onClose = vi.fn()
    render(<ChangePasswordModal onClose={onClose} />)
    await userEvent.click(screen.getByRole('button', { name: '✕' }))
    expect(onClose).toHaveBeenCalledOnce()
  })

  it('shows error when new password is too short', async () => {
    const { container } = render(<ChangePasswordModal onClose={vi.fn()} />)
    const [old, newPw, confirm] = container.querySelectorAll('input[type="password"]')
    await userEvent.type(old, 'oldpassword')
    await userEvent.type(newPw, 'short')
    await userEvent.type(confirm, 'short')
    await userEvent.click(screen.getByRole('button', { name: 'Update password' }))
    expect(screen.getByText('Password is too short (minimum 10 characters).')).toBeInTheDocument()
  })

  it('shows mismatch error when new passwords differ', async () => {
    const { container } = render(<ChangePasswordModal onClose={vi.fn()} />)
    const [old, newPw, confirm] = container.querySelectorAll('input[type="password"]')
    await userEvent.type(old, 'oldpassword')
    await userEvent.type(newPw, 'newpassword1')
    await userEvent.type(confirm, 'newpassword2')
    await userEvent.click(screen.getByRole('button', { name: 'Update password' }))
    expect(screen.getByText('Passwords do not match.')).toBeInTheDocument()
  })

  it('shows success message after a successful change', async () => {
    api.changePassword.mockResolvedValue(null)
    const { container } = render(<ChangePasswordModal onClose={vi.fn()} />)
    const [old, newPw, confirm] = container.querySelectorAll('input[type="password"]')
    await userEvent.type(old, 'oldpassword')
    await userEvent.type(newPw, 'newpassword1')
    await userEvent.type(confirm, 'newpassword1')
    await userEvent.click(screen.getByRole('button', { name: 'Update password' }))
    await waitFor(() => expect(screen.getByText('Password changed successfully.')).toBeInTheDocument())
  })

  it('shows error message when API call fails', async () => {
    api.changePassword.mockRejectedValue(new Error('wrong'))
    const { container } = render(<ChangePasswordModal onClose={vi.fn()} />)
    const [old, newPw, confirm] = container.querySelectorAll('input[type="password"]')
    await userEvent.type(old, 'oldpassword')
    await userEvent.type(newPw, 'samepassword')
    await userEvent.type(confirm, 'samepassword')
    await userEvent.click(screen.getByRole('button', { name: 'Update password' }))
    await waitFor(() => expect(screen.getByText('Current password is incorrect.')).toBeInTheDocument())
  })
})

// ── AccountPanel ──────────────────────────────────────────────

describe('AccountPanel', () => {
  function renderPanel(overrides = {}) {
    return render(
      <AccountPanel
        initials="JD"
        fullName="John Doe"
        primaryEmail="john@weesky.be"
        subDomains={[]}
        quota={null}
        onLogout={vi.fn()}
        onChangePassword={vi.fn()}
        onAdmin={vi.fn()}
        isAdmin={false}
        alphaMode={false}
        onAlphaModeChange={vi.fn()}
        onFullNameChange={vi.fn()}
        {...overrides}
      />
    )
  }

  it('shows the avatar button but hides the panel initially', () => {
    renderPanel()
    expect(screen.getByRole('button', { name: 'JD' })).toBeInTheDocument()
    expect(screen.queryByText('Main mailbox')).not.toBeInTheDocument()
  })

  it('opens the panel when the avatar is clicked', async () => {
    renderPanel()
    await userEvent.click(screen.getByRole('button', { name: 'JD' }))
    expect(screen.getByText('Main mailbox')).toBeInTheDocument()
    expect(screen.getByText('john@weesky.be')).toBeInTheDocument()
  })

  it('shows subdomains when subDomains is not empty', async () => {
    renderPanel({ subDomains: [{ id: 'EXM', name: 'example.com' }] })
    await userEvent.click(screen.getByRole('button', { name: 'JD' }))
    expect(screen.getByText('Other domains')).toBeInTheDocument()
    expect(screen.getByText('example.com')).toBeInTheDocument()
  })

  it('shows the quota bar when quota is provided', async () => {
    renderPanel({ quota: { storageBytesUsed: 100 * MB, storageBytesLimit: 500 * MB } })
    await userEvent.click(screen.getByRole('button', { name: 'JD' }))
    expect(screen.getByText('Storage')).toBeInTheDocument()
  })

  it('calls onChangePassword when Change password is clicked', async () => {
    const onChangePassword = vi.fn()
    renderPanel({ onChangePassword })
    await userEvent.click(screen.getByRole('button', { name: 'JD' }))
    await userEvent.click(screen.getByRole('button', { name: 'Change password' }))
    expect(onChangePassword).toHaveBeenCalledOnce()
  })

  it('calls onLogout when Sign out is clicked', async () => {
    const onLogout = vi.fn()
    renderPanel({ onLogout })
    await userEvent.click(screen.getByRole('button', { name: 'JD' }))
    await userEvent.click(screen.getByRole('button', { name: 'Sign out' }))
    expect(onLogout).toHaveBeenCalledOnce()
  })

  it('calls onAlphaModeChange when the alpha mode toggle is clicked', async () => {
    const onAlphaModeChange = vi.fn()
    renderPanel({ onAlphaModeChange })
    await userEvent.click(screen.getByRole('button', { name: 'JD' }))
    await userEvent.click(screen.getByRole('checkbox'))
    expect(onAlphaModeChange).toHaveBeenCalledWith(true)
  })

  it('enters fullname edit mode when the pencil button is clicked', async () => {
    renderPanel()
    await userEvent.click(screen.getByRole('button', { name: 'JD' }))
    await userEvent.click(screen.getByTitle('Edit name'))
    expect(screen.getByDisplayValue('John Doe')).toBeInTheDocument()
  })

  it('cancels fullname edit without saving when Cancel is clicked', async () => {
    renderPanel()
    await userEvent.click(screen.getByRole('button', { name: 'JD' }))
    await userEvent.click(screen.getByTitle('Edit name'))
    await userEvent.click(screen.getByTitle('Cancel'))
    expect(api.changeFullName).not.toHaveBeenCalled()
    expect(screen.getByText('John Doe')).toBeInTheDocument()
  })

  it('calls api.changeFullName and onFullNameChange when Confirm is clicked', async () => {
    api.changeFullName.mockResolvedValue(null)
    const onFullNameChange = vi.fn()
    renderPanel({ onFullNameChange })
    await userEvent.click(screen.getByRole('button', { name: 'JD' }))
    await userEvent.click(screen.getByTitle('Edit name'))
    const input = screen.getByDisplayValue('John Doe')
    await userEvent.clear(input)
    await userEvent.type(input, 'Jane Doe')
    await userEvent.click(screen.getByTitle('Confirm'))
    await waitFor(() => expect(api.changeFullName).toHaveBeenCalledWith('Jane Doe'))
    await waitFor(() => expect(onFullNameChange).toHaveBeenCalledWith('Jane Doe'))
  })

  it('confirms fullname edit when Enter is pressed in the input', async () => {
    api.changeFullName.mockResolvedValue(null)
    renderPanel()
    await userEvent.click(screen.getByRole('button', { name: 'JD' }))
    await userEvent.click(screen.getByTitle('Edit name'))
    await userEvent.keyboard('{Enter}')
    await waitFor(() => expect(api.changeFullName).toHaveBeenCalled())
  })

  it('stays in edit mode when changeFullName fails', async () => {
    api.changeFullName.mockRejectedValue(new Error('Network error'))
    renderPanel()
    await userEvent.click(screen.getByRole('button', { name: 'JD' }))
    await userEvent.click(screen.getByTitle('Edit name'))
    await userEvent.click(screen.getByTitle('Confirm'))
    await waitFor(() => expect(api.changeFullName).toHaveBeenCalled())
    expect(screen.getByDisplayValue('John Doe')).toBeInTheDocument()
  })

  it('closes the panel when the overlay is clicked', async () => {
    const { container } = renderPanel()
    await userEvent.click(screen.getByRole('button', { name: 'JD' }))
    expect(screen.getByText('Main mailbox')).toBeInTheDocument()
    await userEvent.click(container.querySelector('.panel-overlay'))
    expect(screen.queryByText('Main mailbox')).not.toBeInTheDocument()
  })

  it('closes the panel when clicking outside', async () => {
    renderPanel()
    await userEvent.click(screen.getByRole('button', { name: 'JD' }))
    expect(screen.getByText('Main mailbox')).toBeInTheDocument()
    await userEvent.click(document.body)
    await waitFor(() => expect(screen.queryByText('Main mailbox')).not.toBeInTheDocument())
  })

  it('closes the panel when the overlay element is directly clicked', async () => {
    const { container } = renderPanel()
    await userEvent.click(screen.getByRole('button', { name: 'JD' }))
    expect(screen.getByText('Main mailbox')).toBeInTheDocument()
    // fireEvent.click bypasses the mousedown listener so the overlay's own onClick fires
    fireEvent.click(container.querySelector('.panel-overlay'))
    await waitFor(() => expect(screen.queryByText('Main mailbox')).not.toBeInTheDocument())
  })
})

// ── AliasesPage ───────────────────────────────────────────────

describe('AliasesPage', () => {
  function renderPage(props = {}) {
    return render(<AliasesPage onLogout={vi.fn()} {...props} />)
  }

  it('shows alias tiles after loading', async () => {
    renderPage()
    expect(await screen.findByText('alias1')).toBeInTheDocument()
    expect(screen.getByText('alias2')).toBeInTheDocument()
  })

  it('shows the greeting after account data loads', async () => {
    renderPage()
    expect(await screen.findByText('Hello John Doe !')).toBeInTheDocument()
  })

  it('shows the empty state when there are no aliases', async () => {
    api.getAliases.mockResolvedValue([])
    renderPage()
    expect(await screen.findByText('No aliases for this domain.')).toBeInTheDocument()
  })

  it('shows an error alert when alias load fails', async () => {
    api.getAliases.mockRejectedValue(new Error('net'))
    renderPage()
    expect(await screen.findByText('Failed to load aliases.')).toBeInTheDocument()
  })

  it('filters visible aliases by search term', async () => {
    renderPage()
    await screen.findByText('alias1')
    await userEvent.type(screen.getByPlaceholderText('Search or create…'), 'alias1')
    expect(screen.getByText('alias1')).toBeInTheDocument()
    expect(screen.queryByText('alias2')).not.toBeInTheDocument()
  })

  it('shows an error toast when search term exceeds 30 characters', async () => {
    renderPage()
    await screen.findByText('alias1')
    await userEvent.type(screen.getByPlaceholderText('Search or create…'), 'a'.repeat(31))
    expect(await screen.findByText('An alias cannot exceed 30 characters')).toBeInTheDocument()
  })

  it('hides the domain select with a single domain', async () => {
    renderPage()
    await screen.findByText('alias1')
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument()
  })

  it('shows the domain select with multiple domains', async () => {
    api.getAccount.mockResolvedValue({
      ...ACCOUNT,
      domains: [
        { id: 'WSY', name: 'weesky.be' },
        { id: 'EXM', name: 'example.com' },
      ],
    })
    renderPage()
    await waitFor(() => expect(screen.getByRole('combobox')).toBeInTheDocument())
  })

  it('deletes an alias when the delete button is clicked', async () => {
    api.deleteAlias.mockResolvedValue(null)
    renderPage()
    await screen.findByText('alias1')
    await userEvent.click(screen.getAllByTitle('Delete')[0])
    await userEvent.click(await screen.findByText('Delete', { selector: 'button' }))
    await waitFor(() => expect(api.deleteAlias).toHaveBeenCalledWith('alias1', 'weesky.be'))
    await waitFor(() => expect(screen.queryByText('alias1')).not.toBeInTheDocument())
  })

  it('shows a success toast when an alias is created', async () => {
    api.createAlias.mockResolvedValue(null)
    api.getAliases
      .mockResolvedValueOnce(ALIASES)
      .mockResolvedValue([...ALIASES, { name: 'new', domain: 'weesky.be' }])
    renderPage()
    await screen.findByText('alias1')
    await userEvent.type(screen.getByPlaceholderText('Search or create…'), 'new')
    await userEvent.click(screen.getByRole('button', { name: 'Create alias' }))
    await waitFor(() => expect(api.createAlias).toHaveBeenCalledWith('new', 'weesky.be'))
    expect(await screen.findByText('new@weesky.be added')).toBeInTheDocument()
  })

  it('shows an error toast when alias creation fails', async () => {
    api.createAlias.mockRejectedValue(new Error('Alias exists'))
    renderPage()
    await screen.findByText('alias1')
    await userEvent.type(screen.getByPlaceholderText('Search or create…'), 'bad')
    await userEvent.click(screen.getByRole('button', { name: 'Create alias' }))
    expect(await screen.findByText('Alias exists')).toBeInTheDocument()
  })

  it('opens the change password modal when Change password is clicked', async () => {
    renderPage()
    await screen.findByText('alias1')
    await userEvent.click(screen.getByTitle('john@weesky.be'))
    await userEvent.click(screen.getByRole('button', { name: 'Change password' }))
    expect(screen.getByRole('button', { name: 'Update password' })).toBeInTheDocument()
  })

  it('calls clearSession and onLogout when Sign out is clicked', async () => {
    const onLogout = vi.fn()
    renderPage({ onLogout })
    await screen.findByText('alias1')
    await userEvent.click(screen.getByTitle('john@weesky.be'))
    await userEvent.click(screen.getByRole('button', { name: 'Sign out' }))
    await waitFor(() => expect(clearSession).toHaveBeenCalledOnce())
    expect(onLogout).toHaveBeenCalledOnce()
  })

  it('opens the admin modal when Administration is clicked by an admin user', async () => {
    api.getAccount.mockResolvedValue({ ...ACCOUNT, isAdmin: true })
    renderPage()
    await screen.findByText('alias1')
    await userEvent.click(screen.getByTitle('john@weesky.be'))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Administration' })).toBeInTheDocument())
    await userEvent.click(screen.getByRole('button', { name: 'Administration' }))
    expect(await screen.findByText('Accounts')).toBeInTheDocument()
  })

  it('stores the alpha mode preference in localStorage when toggled', async () => {
    renderPage()
    await screen.findByText('alias1')
    await userEvent.click(screen.getByTitle('john@weesky.be'))
    await userEvent.click(screen.getByRole('checkbox'))
    expect(localStorage.getItem('alias_alpha_mode')).toBe('true')
  })

  it('reads alpha mode from localStorage on initial render', async () => {
    localStorage.setItem('alias_alpha_mode', 'true')
    api.getAliases.mockResolvedValue([
      { name: 'beta', domain: 'weesky.be' },
      { name: 'alpha', domain: 'weesky.be' },
    ])
    const { container } = renderPage()
    // alpha mode renders group letters as .alias-group-letter elements
    await waitFor(() => expect(container.querySelector('.alias-group-letter')).toBeTruthy())
    const letters = [...container.querySelectorAll('.alias-group-letter')].map(el => el.textContent)
    expect(letters).toContain('A')
    expect(letters).toContain('B')
  })

  it('shows a success toast after deleting an alias', async () => {
    api.deleteAlias.mockResolvedValue(null)
    renderPage()
    await screen.findByText('alias1')
    await userEvent.click(screen.getAllByTitle('Delete')[0])
    await userEvent.click(await screen.findByText('Delete', { selector: 'button' }))
    expect(await screen.findByText('alias1@weesky.be deleted')).toBeInTheDocument()
  })

  it('updates the greeting when fullname is changed via AccountPanel', async () => {
    api.changeFullName.mockResolvedValue(null)
    renderPage()
    await screen.findByText('Hello John Doe !')
    await userEvent.click(screen.getByTitle('john@weesky.be'))
    await userEvent.click(screen.getByTitle('Edit name'))
    const input = screen.getByDisplayValue('John Doe')
    await userEvent.clear(input)
    await userEvent.type(input, 'Jane Doe')
    await userEvent.click(screen.getByTitle('Confirm'))
    await waitFor(() => expect(screen.getByText('Hello Jane Doe !')).toBeInTheDocument())
  })

  it('handles getAccount failure gracefully', async () => {
    api.getAccount.mockRejectedValue(new Error('Server error'))
    renderPage()
    expect(await screen.findByText('alias1')).toBeInTheDocument()
  })

  it('handles getQuota failure gracefully', async () => {
    api.getQuota.mockRejectedValue(new Error('Quota unavailable'))
    renderPage()
    expect(await screen.findByText('alias1')).toBeInTheDocument()
  })

  it('reloads aliases when delete fails', async () => {
    api.deleteAlias.mockRejectedValue(new Error('Not found'))
    renderPage()
    await screen.findByText('alias1')
    await userEvent.click(screen.getAllByTitle('Delete')[0])
    await userEvent.click(await screen.findByText('Delete', { selector: 'button' }))
    await waitFor(() => expect(api.getAliases).toHaveBeenCalledTimes(2))
  })

  it('closes the change password modal when ✕ is clicked', async () => {
    renderPage()
    await screen.findByText('alias1')
    await userEvent.click(screen.getByTitle('john@weesky.be'))
    await userEvent.click(screen.getByRole('button', { name: 'Change password' }))
    expect(screen.getByRole('button', { name: 'Update password' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: '✕' }))
    expect(screen.queryByRole('button', { name: 'Update password' })).not.toBeInTheDocument()
  })

  it('closes the admin modal when ✕ is clicked', async () => {
    api.getAccount.mockResolvedValue({ ...ACCOUNT, isAdmin: true })
    renderPage()
    await screen.findByText('alias1')
    await userEvent.click(screen.getByTitle('john@weesky.be'))
    await waitFor(() => screen.getByRole('button', { name: 'Administration' }))
    await userEvent.click(screen.getByRole('button', { name: 'Administration' }))
    expect(await screen.findByRole('button', { name: 'Virtual domains' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: '✕' }))
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Virtual domains' })).not.toBeInTheDocument())
  })

  it('fires alpha nav letter click (scrollToLetter)', async () => {
    localStorage.setItem('alias_alpha_mode', 'true')
    api.getAliases.mockResolvedValue([
      { name: 'alpha', domain: 'weesky.be' },
      { name: 'beta', domain: 'weesky.be' },
    ])
    const { container } = renderPage()
    await waitFor(() => expect(container.querySelector('.alpha-nav-letter')).toBeTruthy())
    const navButtons = container.querySelectorAll('.alpha-nav-letter')
    await userEvent.click(navButtons[1]) // click 'B'
    expect(navButtons[1]).toBeInTheDocument()
  })

  it('fires scroll event in alpha mode (handleScroll)', async () => {
    localStorage.setItem('alias_alpha_mode', 'true')
    api.getAliases.mockResolvedValue([
      { name: 'alpha', domain: 'weesky.be' },
      { name: 'beta', domain: 'weesky.be' },
    ])
    const { container } = renderPage()
    await waitFor(() => expect(container.querySelector('.alias-scroll-area')).toBeTruthy())
    fireEvent.scroll(container.querySelector('.alias-scroll-area'))
    expect(container.querySelector('.alias-group-letter')).toBeTruthy()
  })

  it('clears alias highlight after animation ends (non-alpha mode)', async () => {
    api.createAlias.mockResolvedValue(null)
    api.getAliases
      .mockResolvedValueOnce(ALIASES)
      .mockResolvedValue([...ALIASES, { name: 'newone', domain: 'weesky.be' }])
    const { container } = renderPage()
    await screen.findByText('Hello John Doe !')
    await screen.findByText('alias1')
    await userEvent.type(screen.getByPlaceholderText('Search or create…'), 'newone')
    await waitFor(() => expect(screen.getByRole('button', { name: 'Create alias' })).not.toBeDisabled())
    await userEvent.click(screen.getByRole('button', { name: 'Create alias' }))
    const newTile = await waitFor(
      () => {
        const el = container.querySelector('.alias-tile-new')
        if (!el) throw new Error('tile not yet highlighted')
        return el
      },
      { timeout: 3000 }
    )
    // Invoke the onAnimationEnd handler directly via React internal props
    const propsKey = Object.keys(newTile).find(k => k.startsWith('__reactProps'))
    if (propsKey) {
      await act(async () => { newTile[propsKey].onAnimationEnd() })
    }
    await waitFor(() => expect(container.querySelector('.alias-tile-new')).toBeNull())
  })

  it('changes the selected domain in the domain toolbar', async () => {
    api.getAccount.mockResolvedValue({
      ...ACCOUNT,
      domains: [
        { id: 'WSY', name: 'weesky.be' },
        { id: 'EXM', name: 'example.com' },
      ],
    })
    renderPage()
    await waitFor(() => expect(screen.getByRole('combobox')).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByRole('combobox'), 'example.com')
    expect(screen.getByRole('combobox')).toHaveValue('example.com')
  })

  it('deletes an alias in alpha mode', async () => {
    localStorage.setItem('alias_alpha_mode', 'true')
    api.deleteAlias.mockResolvedValue(null)
    api.getAliases.mockResolvedValue([{ name: 'alpha', domain: 'weesky.be' }])
    renderPage()
    await screen.findByText('alpha')
    await userEvent.click(screen.getByTitle('Delete'))
    await userEvent.click(await screen.findByText('Delete', { selector: 'button' }))
    await waitFor(() => expect(api.deleteAlias).toHaveBeenCalledWith('alpha', 'weesky.be'))
  })

  it('removes an error toast when its close button is clicked', async () => {
    api.createAlias.mockRejectedValue(new Error('Alias exists'))
    renderPage()
    await screen.findByText('alias1')
    await userEvent.type(screen.getByPlaceholderText('Search or create…'), 'bad')
    await userEvent.click(screen.getByRole('button', { name: 'Create alias' }))
    const closeBtn = await screen.findByRole('button', { name: '✕' })
    await userEvent.click(closeBtn)
    await waitFor(() => expect(screen.queryByText('Alias exists')).not.toBeInTheDocument())
  })

  it('uses fallback error message when alias creation error has no message', async () => {
    api.createAlias.mockRejectedValue(new Error())
    renderPage()
    await screen.findByText('alias1')
    await userEvent.type(screen.getByPlaceholderText('Search or create…'), 'bad')
    await userEvent.click(screen.getByRole('button', { name: 'Create alias' }))
    expect(await screen.findByText('Failed to create alias.')).toBeInTheDocument()
  })

  it('handles getAliases returning null', async () => {
    api.getAliases.mockResolvedValue(null)
    renderPage()
    expect(await screen.findByText('No aliases for this domain.')).toBeInTheDocument()
  })

  it('handles account with no domains and null fullName', async () => {
    api.getAccount.mockResolvedValue({
      userName: null,
      fullName: null,
      mailbox: null,
      domains: [],
      isAdmin: false,
    })
    renderPage()
    // page renders without crashing; aliases still show via the default mock
    expect(await screen.findByText('alias1')).toBeInTheDocument()
  })

  it('updates greeting to primary email when fullname is cleared', async () => {
    api.changeFullName.mockResolvedValue(null)
    renderPage()
    await screen.findByText('Hello John Doe !')
    await userEvent.click(screen.getByTitle('john@weesky.be'))
    await userEvent.click(screen.getByTitle('Edit name'))
    const input = screen.getByDisplayValue('John Doe')
    await userEvent.clear(input)
    await userEvent.click(screen.getByTitle('Confirm'))
    await waitFor(() => expect(screen.getByText('Hello john@weesky.be !')).toBeInTheDocument())
  })

  it('renders alpha mode with no aliases (empty state)', async () => {
    localStorage.setItem('alias_alpha_mode', 'true')
    api.getAliases.mockResolvedValue([])
    renderPage()
    expect(await screen.findByText('No aliases for this domain.')).toBeInTheDocument()
  })

  it('clears alias highlight after animation ends (alpha mode)', async () => {
    localStorage.setItem('alias_alpha_mode', 'true')
    api.createAlias.mockResolvedValue(null)
    api.getAliases
      .mockResolvedValueOnce([{ name: 'alpha', domain: 'weesky.be' }])
      .mockResolvedValue([{ name: 'alpha', domain: 'weesky.be' }, { name: 'newone', domain: 'weesky.be' }])
    const { container } = renderPage()
    await screen.findByText('Hello John Doe !')
    await waitFor(() => expect(container.querySelector('.alias-group-letter')).toBeTruthy())
    await userEvent.type(screen.getByPlaceholderText('Search or create…'), 'newone')
    await waitFor(() => expect(screen.getByRole('button', { name: 'Create alias' })).not.toBeDisabled())
    await userEvent.click(screen.getByRole('button', { name: 'Create alias' }))
    const newTile = await waitFor(
      () => {
        const el = container.querySelector('.alias-tile-new')
        if (!el) throw new Error('tile not yet highlighted')
        return el
      },
      { timeout: 3000 }
    )
    const propsKey = Object.keys(newTile).find(k => k.startsWith('__reactProps'))
    if (propsKey) {
      await act(async () => { newTile[propsKey].onAnimationEnd() })
    }
    await waitFor(() => expect(container.querySelector('.alias-tile-new')).toBeNull())
  })
})
