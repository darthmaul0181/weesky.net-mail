import { describe, it, expect, vi, beforeEach } from 'vitest'
import { cleanup, render, screen, fireEvent, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import i18next from 'i18next'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthProvider } from '../../../contexts/AuthContext'
import AccountPage from './AccountPage'

const mocks = vi.hoisted(() => ({
  getAccount: vi.fn(),
  getCapabilities: vi.fn(),
  getQuota: vi.fn(),
  changeFullName: vi.fn(),
  changePassword: vi.fn(),
  logout: vi.fn(),
  hasSession: vi.fn(() => true),
  clearSession: vi.fn(),
  setUnauthorizedHandler: vi.fn(),
  setIsAdmin: vi.fn(),
}))

vi.mock('../../../api.js', () => ({
  api: {
    getAccount: mocks.getAccount,
    getCapabilities: mocks.getCapabilities,
    getQuota: mocks.getQuota,
    changeFullName: mocks.changeFullName,
    changePassword: mocks.changePassword,
    logout: mocks.logout,
    getConnectedAccounts: vi.fn().mockResolvedValue([]),
  },
  hasSession: mocks.hasSession,
  clearSession: mocks.clearSession,
  setUnauthorizedHandler: mocks.setUnauthorizedHandler,
  setIsAdmin: mocks.setIsAdmin,
}))

function renderPage() {
  return render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter>
        <AuthProvider><AccountPage /></AuthProvider>
      </MemoryRouter>
    </QueryClientProvider>
  )
}

const MB = 1024 * 1024

describe('AccountPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.hasSession.mockReturnValue(true)
    mocks.getAccount.mockResolvedValue({
      userName: 'mick', mailbox: 'WSY', fullName: 'Mick', isAdmin: false,
      domains: [{ id: 'WSY', name: 'weesky.be' }, { id: 'EXT', name: 'example.org' }],
    })
    // Omits every flag — the current, unrestricted behaviour.
    mocks.getCapabilities.mockResolvedValue({})
    // Real QuotaBlock shape (see AliasesPage.main.test.jsx): { storageBytesUsed, storageBytesLimit },
    // not the brief's { used, limit }.
    mocks.getQuota.mockResolvedValue({ storageBytesUsed: 100 * MB, storageBytesLimit: 1024 * MB })
  })

  it('shows identity, other domains and quota', async () => {
    renderPage()
    expect(await screen.findByText('mick@weesky.be')).toBeInTheDocument()
    expect(screen.getByText('example.org')).toBeInTheDocument()
    await waitFor(() => expect(mocks.getQuota).toHaveBeenCalled())
  })

  // The quota block used to print a heading of its own right under the section's.
  it('names the storage section once', async () => {
    renderPage()
    await waitFor(() => expect(mocks.getQuota).toHaveBeenCalled())

    expect(await screen.findByText('Storage')).toBeInTheDocument()
    expect(screen.getAllByText('Storage')).toHaveLength(1)
  })

  // jsdom applies no stylesheet, so the class carrying `list-style: none` is all this can hold
  // on to — the domains are read-only names, not an outline.
  it('lists the other domains without bullets', async () => {
    const { container } = renderPage()
    await screen.findByText('example.org')

    expect(container.querySelector('.account-domains')).toBeTruthy()
  })

  it('saves an edited full name', async () => {
    mocks.changeFullName.mockResolvedValue(null)
    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: /edit name/i }))
    const input = screen.getByDisplayValue('Mick')
    fireEvent.change(input, { target: { value: 'Mick D.' } })
    fireEvent.click(screen.getByRole('button', { name: /save/i }))
    await waitFor(() => expect(mocks.changeFullName).toHaveBeenCalledWith('Mick D.'))
  })

  it('rejects a new password shorter than 10 characters', async () => {
    renderPage()
    fireEvent.change(await screen.findByLabelText(/current password/i), { target: { value: 'old-pass-123' } })
    fireEvent.change(screen.getByLabelText(/^new password/i), { target: { value: 'short' } })
    fireEvent.change(screen.getByLabelText(/confirm/i), { target: { value: 'short' } })
    fireEvent.click(screen.getByRole('button', { name: /change password/i }))
    expect(await screen.findByText(/at least 10 characters/i)).toBeInTheDocument()
    expect(mocks.changePassword).not.toHaveBeenCalled()
  })

  it('rejects mismatched confirmation', async () => {
    renderPage()
    fireEvent.change(await screen.findByLabelText(/current password/i), { target: { value: 'old-pass-123' } })
    fireEvent.change(screen.getByLabelText(/^new password/i), { target: { value: 'long-enough-pw' } })
    fireEvent.change(screen.getByLabelText(/confirm/i), { target: { value: 'different-pw-99' } })
    fireEvent.click(screen.getByRole('button', { name: /change password/i }))
    expect(await screen.findByText(/do not match/i)).toBeInTheDocument()
    expect(mocks.changePassword).not.toHaveBeenCalled()
  })

  it('submits a valid password change', async () => {
    mocks.changePassword.mockResolvedValue(null)
    renderPage()
    fireEvent.change(await screen.findByLabelText(/current password/i), { target: { value: 'old-pass-123' } })
    fireEvent.change(screen.getByLabelText(/^new password/i), { target: { value: 'long-enough-pw' } })
    fireEvent.change(screen.getByLabelText(/confirm/i), { target: { value: 'long-enough-pw' } })
    fireEvent.click(screen.getByRole('button', { name: /change password/i }))
    await waitFor(() =>
      expect(mocks.changePassword).toHaveBeenCalledWith('old-pass-123', 'long-enough-pw'))
  })

  it('surfaces an API error and keeps the entered values when the password change fails', async () => {
    mocks.changePassword.mockRejectedValue(new Error('boom'))
    renderPage()
    const oldInput = await screen.findByLabelText(/current password/i)
    const newInput = screen.getByLabelText(/^new password/i)
    const confirmInput = screen.getByLabelText(/confirm/i)
    fireEvent.change(oldInput, { target: { value: 'old-pass-123' } })
    fireEvent.change(newInput, { target: { value: 'long-enough-pw' } })
    fireEvent.change(confirmInput, { target: { value: 'long-enough-pw' } })
    fireEvent.click(screen.getByRole('button', { name: /change password/i }))
    expect(await screen.findByText(/incorrect/i)).toBeInTheDocument()
    expect(oldInput).toHaveValue('old-pass-123')
    expect(newInput).toHaveValue('long-enough-pw')
    expect(confirmInput).toHaveValue('long-enough-pw')
  })

  it('warns on the change-password form that the gesture turns contact sync off, and in what order', async () => {
    // The gesture destroys the sync secret without the Sync tab being open, so the warning has to
    // live where the gesture is — scoped to the form, or it would pass from anywhere on the page.
    renderPage()
    const form = (await screen.findByRole('button', { name: /change password/i })).closest('form')
    const onForm = within(form as HTMLElement)

    // RotateSecurityStampAsync deletes the dav_credentials row rather than re-keying it, so there
    // is no new password to enter; and devices left retrying take 401s that AuthAttemptThrottle
    // counts, with IsBlocked running before the digest comparison. Hence the order.
    expect(onForm.getByText(/turns contact sync off/i)).toBeInTheDocument()
    expect(onForm.getByText(/Turn syncing off on your devices first/i)).toBeInTheDocument()
  })

  // Parity checks keys and typography, never prose, so the order clause could be dropped in French
  // alone with every other assertion green — the same pin the regenerate warning carries.
  it('states the order in French too, not only that sync goes off', async () => {
    await i18next.changeLanguage('fr')
    try {
      renderPage()
      const form = (await screen.findByRole('button', { name: /modifier le mot de passe/i })).closest('form')

      expect(within(form as HTMLElement)
        .getByText(/Désactivez d’abord la synchronisation sur vos appareils/)).toBeInTheDocument()
    } finally {
      cleanup()
      await i18next.changeLanguage('en')
    }
  })

  it('blocks submission when the current password is empty', async () => {
    renderPage()
    fireEvent.change(await screen.findByLabelText(/^new password/i), { target: { value: 'long-enough-pw' } })
    fireEvent.change(screen.getByLabelText(/confirm/i), { target: { value: 'long-enough-pw' } })
    fireEvent.click(screen.getByRole('button', { name: /change password/i }))
    expect(await screen.findByText(/current password is required/i)).toBeInTheDocument()
    expect(mocks.changePassword).not.toHaveBeenCalled()
  })

  describe('capability gating', () => {
    it('hides the change-password section when the platform does not wire it up', async () => {
      mocks.getCapabilities.mockResolvedValue({ passwordChange: false })
      renderPage()
      await screen.findByText('mick@weesky.be')
      await waitFor(() => expect(screen.queryByText('Change password')).not.toBeInTheDocument())
      expect(screen.queryByLabelText(/current password/i)).not.toBeInTheDocument()
    })

    it('hides the name-editing pencil when the platform does not wire profile editing up', async () => {
      mocks.getCapabilities.mockResolvedValue({ profileEditing: false })
      renderPage()
      await screen.findByText('mick@weesky.be')
      await waitFor(() =>
        expect(screen.queryByRole('button', { name: /edit name/i })).not.toBeInTheDocument())
      expect(screen.getByText('Mick')).toBeInTheDocument()
    })

    // GET /api/Account/Quota answers 204 when the IMAP server advertises no QUOTA capability —
    // api.getQuota() already resolves that to null, and QuotaBlock renders nothing for null.
    it('renders no quota gauge when the quota endpoint answers empty', async () => {
      mocks.getQuota.mockResolvedValue(null)
      const { container } = renderPage()
      await screen.findByText('mick@weesky.be')
      await waitFor(() => expect(mocks.getQuota).toHaveBeenCalled())
      expect(container.querySelector('.panel-quota')).toBeNull()
    })
  })
})
