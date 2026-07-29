import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthProvider } from '../../../contexts/AuthContext'
import AccountPage from './AccountPage'

const mocks = vi.hoisted(() => ({
  getAccount: vi.fn(),
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

  it('blocks submission when the current password is empty', async () => {
    renderPage()
    fireEvent.change(await screen.findByLabelText(/^new password/i), { target: { value: 'long-enough-pw' } })
    fireEvent.change(screen.getByLabelText(/confirm/i), { target: { value: 'long-enough-pw' } })
    fireEvent.click(screen.getByRole('button', { name: /change password/i }))
    expect(await screen.findByText(/current password is required/i)).toBeInTheDocument()
    expect(mocks.changePassword).not.toHaveBeenCalled()
  })
})
