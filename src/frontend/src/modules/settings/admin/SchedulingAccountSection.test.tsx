import { describe, it, expect, vi, beforeEach } from 'vitest'
import { act, render, screen, waitFor, cleanup, fireEvent } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import i18next from 'i18next'
import { StrictMode, type ReactNode } from 'react'
import SchedulingAccountSection from './SchedulingAccountSection'
import type { SchedulingAccount } from './useSchedulingAccount'
import { ApiError } from '../../../api.js'
import enAdmin from '../../../locales/en/admin.json'
import frAdmin from '../../../locales/fr/admin.json'

const mocks = vi.hoisted(() => ({
  adminGetSchedulingAccount: vi.fn(),
  adminSaveSchedulingAccount: vi.fn(),
  adminDeleteSchedulingAccount: vi.fn(),
  adminTestSchedulingAccount: vi.fn(),
}))
// importOriginal so `ApiError` — used by the dialog's own `instanceof` checks — stays the real
// class, and only the network calls are stubbed.
vi.mock('../../../api.js', async importOriginal => ({
  ...(await importOriginal<typeof import('../../../api.js')>()),
  api: mocks,
}))

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

const addToast = vi.fn()

const NOT_CONFIGURED: SchedulingAccount = {
  configured: false, passwordStored: false, passwordReadable: false, allowCleartext: false,
}

const CONFIGURED: SchedulingAccount = {
  configured: true,
  host: 'smtp.weesky.be',
  port: 587,
  security: 'StartTls',
  login: 'agenda@weesky.net',
  passwordStored: true,
  passwordReadable: true,
  allowCleartext: false,
}

const UNREADABLE: SchedulingAccount = { ...CONFIGURED, passwordReadable: false }

function renderSection(account: SchedulingAccount = CONFIGURED) {
  mocks.adminGetSchedulingAccount.mockResolvedValue(account)
  return render(<SchedulingAccountSection addToast={addToast} />, { wrapper })
}

async function openEditDialog(account: SchedulingAccount = CONFIGURED) {
  renderSection(account)
  await screen.findByText(account.login as string)
  await userEvent.click(screen.getByTitle('Edit'))
}

beforeEach(() => vi.clearAllMocks())

describe('SchedulingAccountSection — card states', () => {
  it('shows the configured card: login, host · port · security', async () => {
    renderSection()
    expect(await screen.findByText('agenda@weesky.net')).toBeInTheDocument()
    expect(screen.getByText('smtp.weesky.be · port 587 · STARTTLS')).toBeInTheDocument()
  })

  it('shows the not-configured card with a Configure action', async () => {
    renderSection(NOT_CONFIGURED)
    expect(await screen.findByText(
      'No account configured: changes made from a phone do not send invitations.',
    )).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Configure' })).toBeInTheDocument()
  })

  it('shows the password-unreadable card naming the account, with Edit and Delete', async () => {
    renderSection(UNREADABLE)
    expect(await screen.findByText('agenda@weesky.net')).toBeInTheDocument()
    expect(screen.getByText('smtp.weesky.be')).toBeInTheDocument()
    expect(screen.getByText(
      'The stored password can no longer be read. Enter it again to resume sending invitations.',
    )).toBeInTheDocument()
    expect(screen.getByTitle('Edit')).toBeInTheDocument()
    expect(screen.getByTitle('Delete')).toBeInTheDocument()
  })

  it('shows a load-failure message when the query fails', async () => {
    mocks.adminGetSchedulingAccount.mockRejectedValue(new Error('Server error'))
    render(<SchedulingAccountSection addToast={addToast} />, { wrapper })
    expect(await screen.findByText('Could not load the sending account.')).toBeInTheDocument()
  })
})

describe('SchedulingAccountSection — last-test pill', () => {
  it('shows no pill when the account was never tested', async () => {
    renderSection()
    await screen.findByText('agenda@weesky.net')
    expect(screen.queryByText('Last test failed')).not.toBeInTheDocument()
    expect(screen.queryByText(/Connection tested on/)).not.toBeInTheDocument()
  })

  it('shows a green pill when the last test succeeded', async () => {
    renderSection({ ...CONFIGURED, lastTestAt: '2026-09-14T08:30:00Z', lastTestOk: true })
    const pill = await screen.findByText(/Connection tested on/)
    expect(pill).toHaveClass('is-ok')
  })

  it('shows a red pill when the last test failed', async () => {
    renderSection({ ...CONFIGURED, lastTestAt: '2026-09-14T08:30:00Z', lastTestOk: false })
    const pill = await screen.findByText('Last test failed')
    expect(pill).toHaveClass('is-fail')
  })
})

// "14 sept.": day + abbreviated month, never the locale's full short date
// (dateStyle: 'short' reads "9/14/26" in English and gives no way to tell the two apart by eye).
describe('SchedulingAccountSection — last-test date format', () => {
  it('formats it as day + abbreviated month in English', async () => {
    renderSection({ ...CONFIGURED, lastTestAt: '2026-09-14T08:30:00Z', lastTestOk: true })
    const pill = await screen.findByText(/Connection tested on/)
    expect(pill.textContent).toMatch(/Sep/)
    expect(pill.textContent).not.toMatch(/\d{1,2}\/\d{1,2}\/\d{2,4}/)
  })

  it('formats it the same way in French', async () => {
    await i18next.changeLanguage('fr')
    try {
      renderSection({ ...CONFIGURED, lastTestAt: '2026-09-14T08:30:00Z', lastTestOk: true })
      const pill = await screen.findByText(/Connexion testée le/)
      expect(pill.textContent).toMatch(/sept\.?/i)
      expect(pill.textContent).not.toMatch(/\d{1,2}\/\d{1,2}\/\d{2,4}/)
    } finally {
      cleanup()
      await i18next.changeLanguage('en')
    }
  })
})

describe('SchedulingAccountSection — Tester button (card)', () => {
  it('tests the stored account without a body and shows the result through the pill after refetch', async () => {
    mocks.adminGetSchedulingAccount
      .mockResolvedValueOnce(CONFIGURED)
      .mockResolvedValueOnce({ ...CONFIGURED, lastTestAt: '2026-09-14T08:30:00Z', lastTestOk: true })
    mocks.adminTestSchedulingAccount.mockResolvedValue({ ok: true })
    render(<SchedulingAccountSection addToast={addToast} />, { wrapper })
    await screen.findByText('agenda@weesky.net')

    await userEvent.click(screen.getByRole('button', { name: 'Test' }))

    await waitFor(() => expect(mocks.adminTestSchedulingAccount).toHaveBeenCalledWith(undefined))
    expect(await screen.findByText(/Connection tested on/)).toBeInTheDocument()
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('The connection test succeeded.'))
  })

  it('toasts the mapped reason when the stored account fails its test', async () => {
    renderSection()
    mocks.adminTestSchedulingAccount.mockResolvedValue({ ok: false, error: 'smtp_auth_failed' })
    await screen.findByText('agenda@weesky.net')

    await userEvent.click(screen.getByRole('button', { name: 'Test' }))

    await waitFor(() => expect(addToast)
      .toHaveBeenCalledWith('The server refused the login or password.', 'error'))
  })

  it('toasts a running-test message on a 409 from the card', async () => {
    renderSection()
    mocks.adminTestSchedulingAccount.mockRejectedValue(
      new ApiError('connection_test_in_progress', 409, 'connection_test_in_progress'))
    await screen.findByText('agenda@weesky.net')

    await userEvent.click(screen.getByRole('button', { name: 'Test' }))

    await waitFor(() => expect(addToast)
      .toHaveBeenCalledWith('A connection test is already running.', 'error'))
  })

  it('says the account is gone on a 404 (deleted elsewhere) and the card refetches', async () => {
    mocks.adminGetSchedulingAccount
      .mockResolvedValueOnce(CONFIGURED)
      .mockResolvedValueOnce(NOT_CONFIGURED)
    mocks.adminTestSchedulingAccount.mockRejectedValue(
      new ApiError('No service account is configured', 404, null))
    render(<SchedulingAccountSection addToast={addToast} />, { wrapper })
    await screen.findByText('agenda@weesky.net')

    await userEvent.click(screen.getByRole('button', { name: 'Test' }))

    await waitFor(() => expect(addToast).toHaveBeenCalledWith('The account no longer exists.', 'error'))
    expect(await screen.findByText(/No account configured/)).toBeInTheDocument()
  })
})

describe('SchedulingAccountSection — delete', () => {
  it('deletes after confirmation', async () => {
    renderSection()
    mocks.adminDeleteSchedulingAccount.mockResolvedValue(undefined)
    await screen.findByText('agenda@weesky.net')

    await userEvent.click(screen.getByTitle('Delete'))
    expect(screen.getByText('Confirm deletion')).toBeInTheDocument()
    expect(mocks.adminDeleteSchedulingAccount).not.toHaveBeenCalled()

    const deleteButtons = screen.getAllByRole('button', { name: 'Delete' })
    await userEvent.click(deleteButtons[deleteButtons.length - 1]!)

    await waitFor(() => expect(mocks.adminDeleteSchedulingAccount).toHaveBeenCalled())
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('The sending account was deleted.'))
  })

  it('closing the confirm modal does not delete', async () => {
    renderSection()
    await screen.findByText('agenda@weesky.net')
    await userEvent.click(screen.getByTitle('Delete'))
    await userEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(mocks.adminDeleteSchedulingAccount).not.toHaveBeenCalled()
    expect(screen.queryByText('Confirm deletion')).not.toBeInTheDocument()
  })

  async function confirmDeleteRejectedWith(error: ApiError) {
    renderSection()
    mocks.adminDeleteSchedulingAccount.mockRejectedValue(error)
    await screen.findByText('agenda@weesky.net')
    await userEvent.click(screen.getByTitle('Delete'))
    const deleteButtons = screen.getAllByRole('button', { name: 'Delete' })
    await userEvent.click(deleteButtons[deleteButtons.length - 1]!)
  }

  it('closes the confirmation on a 409, so it never stands over a login that was reloaded', async () => {
    await confirmDeleteRejectedWith(new ApiError(
      'scheduling_account_changed_concurrently', 409, 'scheduling_account_changed_concurrently'))

    await waitFor(() => expect(addToast)
      .toHaveBeenCalledWith('The account changed in the meantime: it was reloaded.', 'error'))
    expect(screen.queryByText('Confirm deletion')).not.toBeInTheDocument()
  })

  it('keeps the confirmation open on any other failure, so the deletion can be retried', async () => {
    await confirmDeleteRejectedWith(new ApiError('Server error', 500, null))

    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Could not delete the sending account.', 'error'))
    expect(screen.getByText('Confirm deletion')).toBeInTheDocument()
  })

  it('offers delete from the password-unreadable card too', async () => {
    renderSection(UNREADABLE)
    mocks.adminDeleteSchedulingAccount.mockResolvedValue(undefined)
    await screen.findByText('agenda@weesky.net')
    await userEvent.click(screen.getByTitle('Delete'))
    const deleteButtons = screen.getAllByRole('button', { name: 'Delete' })
    await userEvent.click(deleteButtons[deleteButtons.length - 1]!)
    await waitFor(() => expect(mocks.adminDeleteSchedulingAccount).toHaveBeenCalled())
  })
})

describe('SchedulingAccountSection — dialog prefill', () => {
  it('prefills every field from the stored account, with the stored-password note', async () => {
    await openEditDialog()

    expect(screen.getByLabelText('SMTP host')).toHaveValue('smtp.weesky.be')
    expect(screen.getByLabelText('SMTP port')).toHaveValue(587)
    expect(screen.getByLabelText('SMTP security')).toHaveValue('StartTls')
    expect(screen.getByLabelText('Login')).toHaveValue('agenda@weesky.net')
    const password = screen.getByLabelText('Password')
    expect(password).toHaveValue('')
    expect(password).toHaveAttribute('placeholder', 'Unchanged')
    expect(screen.getByText(
      'A password is stored. It is never shown; leave the field empty to keep it.',
    )).toBeInTheDocument()
  })

  it('opens empty, on the default port, with no stored-password note and a required password', async () => {
    renderSection(NOT_CONFIGURED)
    await screen.findByText(/No account configured/)
    await userEvent.click(screen.getByRole('button', { name: 'Configure' }))

    expect(screen.getByLabelText('SMTP port')).toHaveValue(587)
    expect(screen.queryByText(
      'A password is stored. It is never shown; leave the field empty to keep it.',
    )).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
  })

  it('opens from the unreadable card with the password required immediately', async () => {
    await openEditDialog(UNREADABLE)

    expect(await screen.findByText('A password is required to save or test this account.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
  })
})

describe('SchedulingAccountSection — security choices', () => {
  const choices = () => Array.from(screen.getByLabelText<HTMLSelectElement>('SMTP security').options, o => o.value)

  it('offers no « None » where the server refuses an unencrypted endpoint', async () => {
    await openEditDialog()
    expect(choices()).toEqual(['StartTls', 'SslOnConnect'])
  })

  it('offers « None » where the server accepts one', async () => {
    await openEditDialog({ ...CONFIGURED, allowCleartext: true })
    expect(choices()).toEqual(['None', 'StartTls', 'SslOnConnect'])
  })

  it('keeps a stored « None » shown and selectable, so the admin can move away from it', async () => {
    await openEditDialog({ ...CONFIGURED, security: 'None' })
    const select = screen.getByLabelText('SMTP security')
    expect(select).toHaveValue('None')

    await userEvent.selectOptions(select, 'StartTls')
    expect(select).toHaveValue('StartTls')
    expect(choices()).toEqual(['None', 'StartTls', 'SslOnConnect'])
  })
})

describe('SchedulingAccountSection — password required on host/port change', () => {
  it('requires a password as soon as the host changes, and allows submit again once typed', async () => {
    await openEditDialog()
    const saveButton = screen.getByRole('button', { name: 'Save' })
    expect(saveButton).not.toBeDisabled()

    const host = screen.getByLabelText('SMTP host')
    await userEvent.clear(host)
    await userEvent.type(host, 'smtp.other.example')

    expect(await screen.findByText('A password is required to save or test this account.')).toBeInTheDocument()
    expect(saveButton).toBeDisabled()

    await userEvent.type(screen.getByLabelText('Password'), 'new-secret')
    expect(saveButton).not.toBeDisabled()
  })

  it('requires a password as soon as the port changes', async () => {
    await openEditDialog()

    const port = screen.getByLabelText('SMTP port')
    await userEvent.clear(port)
    await userEvent.type(port, '2525')

    expect(await screen.findByText('A password is required to save or test this account.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
  })

  it('keeps the password optional when only the host’s case changes', async () => {
    await openEditDialog()

    const host = screen.getByLabelText('SMTP host')
    await userEvent.clear(host)
    await userEvent.type(host, 'SMTP.WEESKY.BE')

    expect(screen.queryByText('A password is required to save or test this account.')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save' })).not.toBeDisabled()
  })

  it('keeps the password optional when only the security changes', async () => {
    await openEditDialog()

    await userEvent.selectOptions(screen.getByLabelText('SMTP security'), 'SslOnConnect')

    expect(screen.queryByText('A password is required to save or test this account.')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save' })).not.toBeDisabled()
  })

  it('keeps the password optional when only the login changes', async () => {
    await openEditDialog()

    const login = screen.getByLabelText('Login')
    await userEvent.clear(login)
    await userEvent.type(login, 'other@weesky.net')

    expect(screen.queryByText('A password is required to save or test this account.')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save' })).not.toBeDisabled()
  })
})

describe('SchedulingAccountSection — inline test result (dialog)', () => {
  it('shows success inline, as an icon plus text, not a boxed alert', async () => {
    await openEditDialog()
    mocks.adminTestSchedulingAccount.mockResolvedValue({ ok: true })

    await userEvent.click(screen.getByRole('button', { name: 'Test the connection' }))

    const result = await screen.findByText('The connection succeeded.')
    expect(result.closest('.svc-account-test-result')).toBeInTheDocument()
    expect(result.closest('.alert')).not.toBeInTheDocument()
  })

  it.each([
    ['smtp_unreachable', 'The server could not be reached. Check the host and port.'],
    ['smtp_auth_failed', 'The server refused the login or password.'],
    ['smtp_auth_unsupported', 'The server offers no compatible way to authenticate.'],
    ['smtp_tls_failed', 'A secure connection could not be established.'],
    ['smtp_timeout', 'The server did not answer in time.'],
    ['password_unreadable', 'The stored password can no longer be read: enter it again.'],
    ['stored_account_invalid', 'The stored account is no longer valid: save it again.'],
  ])('maps the %s test failure to its sentence', async (code, sentence) => {
    await openEditDialog()
    mocks.adminTestSchedulingAccount.mockResolvedValue({ ok: false, error: code })

    await userEvent.click(screen.getByRole('button', { name: 'Test the connection' }))

    expect(await screen.findByText(sentence)).toBeInTheDocument()
  })

  it('shows a running-test message on a 409 from the dialog', async () => {
    await openEditDialog()
    mocks.adminTestSchedulingAccount.mockRejectedValue(
      new ApiError('connection_test_in_progress', 409, 'connection_test_in_progress'))

    await userEvent.click(screen.getByRole('button', { name: 'Test the connection' }))

    expect(await screen.findByText('A connection test is already running.')).toBeInTheDocument()
  })

  it('sends the entered values, not the stored ones', async () => {
    await openEditDialog()
    mocks.adminTestSchedulingAccount.mockResolvedValue({ ok: true })
    await userEvent.clear(screen.getByLabelText('Login'))
    await userEvent.type(screen.getByLabelText('Login'), 'other@weesky.net')

    await userEvent.click(screen.getByRole('button', { name: 'Test the connection' }))

    await waitFor(() => expect(mocks.adminTestSchedulingAccount).toHaveBeenCalledWith({
      host: 'smtp.weesky.be', port: 587, security: 'StartTls', login: 'other@weesky.net',
    }))
  })

  // Only a request that bypassed the form's own checks meets a refusal without a code.
  it('shows the server refusal text after a translated lead on an unmapped 400', async () => {
    await openEditDialog()
    mocks.adminTestSchedulingAccount.mockRejectedValue(new ApiError(
      'Host is not a valid hostname or IP address', 400, 'Host is not a valid hostname or IP address'))

    await userEvent.click(screen.getByRole('button', { name: 'Test the connection' }))

    expect(await screen.findByText(
      'Settings refused by the server: Host is not a valid hostname or IP address',
    )).toBeInTheDocument()
  })

  it('clears a stale result once a field changes', async () => {
    await openEditDialog()
    mocks.adminTestSchedulingAccount.mockResolvedValue({ ok: true })
    await userEvent.click(screen.getByRole('button', { name: 'Test the connection' }))
    expect(await screen.findByText('The connection succeeded.')).toBeInTheDocument()

    await userEvent.type(screen.getByLabelText('Login'), 'x')

    expect(screen.queryByText('The connection succeeded.')).not.toBeInTheDocument()
  })

  it('ignores a test response that arrives after the fields it answered for changed', async () => {
    await openEditDialog()
    let resolveTest: (value: { ok: boolean }) => void = () => {}
    mocks.adminTestSchedulingAccount.mockReturnValue(new Promise(resolve => { resolveTest = resolve }))

    await userEvent.click(screen.getByRole('button', { name: 'Test the connection' }))
    await userEvent.type(screen.getByLabelText('Login'), 'x')
    resolveTest({ ok: true })
    await waitFor(() => expect(screen.getByRole('button', { name: 'Test the connection' }))
      .toHaveAttribute('aria-busy', 'false'))

    expect(screen.queryByText('The connection succeeded.')).not.toBeInTheDocument()
  })
})

describe('SchedulingAccountSection — save', () => {
  it('omits the password when it is left blank and nothing changed', async () => {
    await openEditDialog()
    mocks.adminSaveSchedulingAccount.mockResolvedValue(undefined)

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    const signal: unknown = expect.any(AbortSignal)
    await waitFor(() => expect(mocks.adminSaveSchedulingAccount).toHaveBeenCalledWith({
      host: 'smtp.weesky.be', port: 587, security: 'StartTls', login: 'agenda@weesky.net',
    }, { signal }))
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('The sending account was saved.'))
  })

  it('sends a newly typed password', async () => {
    await openEditDialog()
    mocks.adminSaveSchedulingAccount.mockResolvedValue(undefined)

    await userEvent.type(screen.getByLabelText('Password'), 'rotated-secret')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(mocks.adminSaveSchedulingAccount).toHaveBeenCalledWith(
      expect.objectContaining({ password: 'rotated-secret' }), expect.anything()))
  })

  it('closes the dialog and toasts on a 409 conflict, never claiming success', async () => {
    await openEditDialog()
    mocks.adminSaveSchedulingAccount.mockRejectedValue(new ApiError(
      'scheduling_account_changed_concurrently', 409, 'scheduling_account_changed_concurrently'))

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(addToast)
      .toHaveBeenCalledWith('The account changed in the meantime: it was reloaded.', 'error'))
    expect(screen.queryByLabelText('SMTP host')).not.toBeInTheDocument()
    expect(addToast).not.toHaveBeenCalledWith('The sending account was saved.')
  })

  // Refusals the form itself can meet: another admin's change, or the opt-in turned off since the load.
  it.each([
    ['security_none_disallowed', 'Unencrypted connections are not allowed: choose STARTTLS or SSL/TLS.'],
    ['password_required_for_new_endpoint', 'A password is required: the host or port no longer matches the saved account.'],
    ['password_required', 'A password is required.'],
    ['password_unreadable', 'The stored password can no longer be read: enter it again.'],
  ])('says the %s refusal in a sentence, with no server prose', async (code, sentence) => {
    await openEditDialog()
    mocks.adminSaveSchedulingAccount.mockRejectedValue(new ApiError(code, 400, code))

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect((await screen.findByRole('alert')).textContent).toBe(sentence)
  })

  it('says a refused « None » in French on a French screen', async () => {
    await i18next.changeLanguage('fr')
    try {
      renderSection({ ...CONFIGURED, security: 'None' })
      await userEvent.click(await screen.findByTitle('Modifier'))
      mocks.adminSaveSchedulingAccount.mockRejectedValue(
        new ApiError('security_none_disallowed', 400, 'security_none_disallowed'))

      await userEvent.click(screen.getByRole('button', { name: 'Enregistrer' }))

      expect((await screen.findByRole('alert')).textContent).toBe(
        'Les connexions sans chiffrement ne sont pas autorisées\u00a0: choisissez STARTTLS ou SSL/TLS.')
    } finally {
      cleanup()
      await i18next.changeLanguage('en')
    }
  })

  it('shows the server refusal text on an unmapped 400, and keeps the dialog open', async () => {
    await openEditDialog()
    mocks.adminSaveSchedulingAccount.mockRejectedValue(new ApiError(
      'Login must be between 1 and 320 characters', 400, 'Login must be between 1 and 320 characters'))

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText(
      'Settings refused by the server: Login must be between 1 and 320 characters',
    )).toBeInTheDocument()
    expect(screen.getByLabelText('SMTP host')).toBeInTheDocument()
    expect(mocks.adminGetSchedulingAccount).toHaveBeenCalled()
  })

  it('closing the dialog does not save', async () => {
    await openEditDialog()
    await userEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(mocks.adminSaveSchedulingAccount).not.toHaveBeenCalled()
    expect(screen.queryByLabelText('SMTP host')).not.toBeInTheDocument()
  })

  it('blocks Escape, the ✕ and an overlay click while a save is pending', async () => {
    await openEditDialog()
    let resolveSave: () => void = () => {}
    mocks.adminSaveSchedulingAccount.mockReturnValue(new Promise<void>(resolve => { resolveSave = resolve }))

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(screen.getByLabelText('SMTP host')).toBeInTheDocument()

    await userEvent.keyboard('{Escape}')
    expect(screen.getByLabelText('SMTP host')).toBeInTheDocument()

    expect(screen.getByRole('button', { name: 'Close' })).toBeDisabled()

    const overlay = screen.getByRole('dialog').parentElement as HTMLElement
    await userEvent.click(overlay)
    expect(screen.getByLabelText('SMTP host')).toBeInTheDocument()

    resolveSave()
    await waitFor(() => expect(screen.queryByLabelText('SMTP host')).not.toBeInTheDocument())
  })

  it('announces nothing for a save that lands after the section unmounted', async () => {
    let resolveSave: () => void = () => {}
    mocks.adminSaveSchedulingAccount.mockReturnValue(new Promise<void>(resolve => { resolveSave = resolve }))
    const { unmount } = renderSection()
    await screen.findByText('agenda@weesky.net')
    await userEvent.click(screen.getByTitle('Edit'))
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    unmount()
    resolveSave()
    await new Promise(r => setTimeout(r, 0))

    expect(addToast).not.toHaveBeenCalled()
  })

  it('stops blocking after 30 seconds without an answer, says so, and lets the dialog close', async () => {
    await openEditDialog()
    mocks.adminSaveSchedulingAccount.mockReturnValue(new Promise(() => {}))
    vi.useFakeTimers()
    try {
      fireEvent.click(screen.getByRole('button', { name: 'Save' }))
      await act(() => vi.advanceTimersByTimeAsync(29_000))
      expect(screen.getByRole('button', { name: 'Close' })).toBeDisabled()

      await act(() => vi.advanceTimersByTimeAsync(1_000))

      expect(screen.getByRole('alert')).toHaveTextContent('Saving is not responding.')
      fireEvent.click(screen.getByRole('button', { name: 'Close' }))
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    } finally {
      vi.useRealTimers()
    }
  })
})

describe('SchedulingAccountSection — validation details', () => {
  it('does not flag a trailing space on the host', async () => {
    await openEditDialog()
    const host = screen.getByLabelText('SMTP host')
    await userEvent.type(host, ' ')
    expect(host).not.toHaveClass('is-error')
    expect(screen.getByRole('button', { name: 'Save' })).not.toBeDisabled()
  })

  it('measures the login trimmed, as the server does', async () => {
    await openEditDialog()
    const login = screen.getByLabelText('Login')
    fireEvent.change(login, { target: { value: `  ${'a'.repeat(309)}@weesky.net ` } })
    expect(login).not.toHaveClass('is-error')
    expect(screen.getByRole('button', { name: 'Save' })).not.toBeDisabled()
  })

  it('flags an emptied port as invalid', async () => {
    await openEditDialog()
    const port = screen.getByLabelText('SMTP port')
    await userEvent.clear(port)
    expect(port).toHaveClass('is-error')
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
  })
})

describe('SchedulingAccountSection — accessibility', () => {
  it('carries dialog semantics, named by its own title, and closes on Escape', async () => {
    await openEditDialog()
    expect(screen.getByRole('dialog', { name: 'Calendar invitation sending account' })).toBeInTheDocument()

    await userEvent.keyboard('{Escape}')

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('moves focus into the dialog on open and returns it to the trigger on close', async () => {
    renderSection()
    await screen.findByText('agenda@weesky.net')
    const editButton = screen.getByTitle('Edit')

    await userEvent.click(editButton)
    expect(screen.getByLabelText('SMTP host')).toHaveFocus()

    await userEvent.keyboard('{Escape}')
    await waitFor(() => expect(screen.queryByLabelText('SMTP host')).not.toBeInTheDocument())
    expect(editButton).toHaveFocus()
  })

  it('keeps focus inside the dialog when Save disables the button that had it', async () => {
    await openEditDialog()
    let rejectSave: (err: Error) => void = () => {}
    mocks.adminSaveSchedulingAccount.mockReturnValue(new Promise((_, reject) => { rejectSave = reject }))

    // jsdom leaves focus on the disabled Save, where Chrome drops it to <body>; neither may let Tab out.
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await userEvent.tab()
    expect(screen.getByLabelText('SMTP host')).toHaveFocus()

    act(() => (document.activeElement as HTMLElement).blur())
    rejectSave(new ApiError('Login must be between 1 and 320 characters', 400, null))
    await screen.findByRole('alert')

    expect(screen.getByRole('button', { name: 'Save' })).toHaveFocus()
  })

  async function configureFromEmpty(refetch: Promise<SchedulingAccount> | SchedulingAccount) {
    mocks.adminGetSchedulingAccount.mockResolvedValueOnce(NOT_CONFIGURED).mockReturnValue(refetch)
    mocks.adminSaveSchedulingAccount.mockResolvedValue(undefined)
    render(<SchedulingAccountSection addToast={addToast} />, { wrapper })
    await userEvent.click(await screen.findByRole('button', { name: 'Configure' }))
    await userEvent.type(screen.getByLabelText('SMTP host'), 'smtp.weesky.be')
    await userEvent.type(screen.getByLabelText('Login'), 'agenda@weesky.net')
    await userEvent.type(screen.getByLabelText('Password'), 'secret')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
  }

  it('hands focus to the section heading when the Configure button is gone before the dialog closes', async () => {
    await configureFromEmpty(CONFIGURED)

    await screen.findByText('agenda@weesky.net')
    expect(screen.getByRole('heading', { name: 'Calendar invitation sending account' })).toHaveFocus()
  })

  it('hands focus to the section heading when the card is replaced after the dialog closed', async () => {
    let resolveRefetch: (account: SchedulingAccount) => void = () => {}
    await configureFromEmpty(new Promise(resolve => { resolveRefetch = resolve }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(screen.getByRole('button', { name: 'Configure' })).toHaveFocus()

    await act(async () => resolveRefetch(CONFIGURED))

    await screen.findByText('agenda@weesky.net')
    expect(screen.getByRole('heading', { name: 'Calendar invitation sending account' })).toHaveFocus()
  })

  it('keeps the Tester button’s accessible name while its spinner is showing', async () => {
    renderSection()
    let resolveTest: (value: { ok: boolean }) => void = () => {}
    mocks.adminTestSchedulingAccount.mockReturnValue(new Promise(resolve => { resolveTest = resolve }))
    await screen.findByText('agenda@weesky.net')

    await userEvent.click(screen.getByRole('button', { name: 'Test' }))
    expect(screen.getByRole('button', { name: 'Test' })).toHaveAttribute('aria-busy', 'true')

    resolveTest({ ok: true })
    await waitFor(() => expect(screen.getByRole('button', { name: 'Test' })).toHaveAttribute('aria-busy', 'false'))
  })
})

describe('SchedulingAccountSection — typed password in memory', () => {
  it('leaves no typed password in the mutation cache once the dialog has closed', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    mocks.adminGetSchedulingAccount.mockResolvedValue(CONFIGURED)
    mocks.adminTestSchedulingAccount.mockResolvedValue({ ok: true })
    mocks.adminSaveSchedulingAccount.mockResolvedValue(undefined)
    render(<QueryClientProvider client={client}><SchedulingAccountSection addToast={addToast} /></QueryClientProvider>)
    await userEvent.click(await screen.findByTitle('Edit'))
    await userEvent.type(screen.getByLabelText('Password'), 'rotated-secret')
    await userEvent.click(screen.getByRole('button', { name: 'Test the connection' }))
    await screen.findByText('The connection succeeded.')

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())

    await waitFor(() => expect(JSON.stringify(client.getMutationCache().getAll().map(m => m.state.variables)))
      .not.toContain('rotated-secret'))
  })
})

// main.tsx renders under StrictMode, whose mount-unmount-mount cycle is where a mounted ref breaks.
describe('SchedulingAccountSection — under StrictMode', () => {
  async function openStrict() {
    mocks.adminGetSchedulingAccount.mockResolvedValue(CONFIGURED)
    render(<StrictMode><SchedulingAccountSection addToast={addToast} /></StrictMode>, { wrapper })
    await userEvent.click(await screen.findByTitle('Edit'))
  }

  it('closes the dialog and toasts after a successful save', async () => {
    mocks.adminSaveSchedulingAccount.mockResolvedValue(undefined)
    await openStrict()

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(addToast).toHaveBeenCalledWith('The sending account was saved.')
  })

  it('shows the test result', async () => {
    mocks.adminTestSchedulingAccount.mockResolvedValue({ ok: false, error: 'smtp_auth_failed' })
    await openStrict()

    await userEvent.click(screen.getByRole('button', { name: 'Test the connection' }))

    expect(await screen.findByText('The server refused the login or password.')).toBeInTheDocument()
  })

  it('shows a save error', async () => {
    mocks.adminSaveSchedulingAccount.mockRejectedValue(
      new ApiError('Login must be between 1 and 320 characters', 400, null))
    await openStrict()

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText(
      'Settings refused by the server: Login must be between 1 and 320 characters',
    )).toBeInTheDocument()
  })
})

describe('help bubble', () => {
  it('states the consequence, with a verb, in both languages', () => {
    expect(enAdmin.help.application).toMatch(/does not notify the guests/)
    expect(frAdmin.help.application).toMatch(/ne prévient pas les invités/)
  })
})
