import { describe, it, expect, vi, beforeEach } from 'vitest'
import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { createTestQueryClient, holdNextCall, settle } from '../../../test-utils'
import ExternalDomainsTab from './ExternalDomainsTab'
import type { ExternalDomain } from './useExternalDomains'

const mocks = vi.hoisted(() => ({
  adminGetExternalDomains: vi.fn(),
  adminCreateExternalDomain: vi.fn(),
  adminUpdateExternalDomain: vi.fn(),
  adminDeleteExternalDomain: vi.fn(),
}))
vi.mock('../../../api.js', () => ({ api: mocks }))

function wrapper({ children }: { children: ReactNode }) {
  const client = createTestQueryClient()
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

const addToast = vi.fn()

const NO_OAUTH = {
  authMode: 'Password' as const,
  oauthClientSecretSet: false,
}

const GMAIL: ExternalDomain = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Gmail',
  imapHost: 'imap.gmail.com',
  imapPort: 993,
  imapSecurity: 'SslOnConnect',
  smtpHost: 'smtp.gmail.com',
  smtpPort: 587,
  smtpSecurity: 'StartTls',
  ...NO_OAUTH,
}

const OUTLOOK_SERVERS = {
  imapHost: 'outlook.office365.com',
  imapPort: 993,
  imapSecurity: 'SslOnConnect',
  smtpHost: 'smtp.office365.com',
  smtpPort: 587,
  smtpSecurity: 'StartTls',
}

const OUTLOOK: ExternalDomain = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'Outlook',
  ...OUTLOOK_SERVERS,
  sieveHost: 'sieve.office365.com',
  sievePort: 4190,
  ...NO_OAUTH,
}

const OUTLOOK_OAUTH: ExternalDomain = {
  ...OUTLOOK_SERVERS,
  id: '33333333-3333-3333-3333-333333333333',
  name: 'Outlook (OAuth)',
  authMode: 'OAuth2',
  oauthAuthorizationUrl: 'https://login.microsoftonline.com/common/oauth2/v2.0/authorize',
  oauthTokenUrl: 'https://login.microsoftonline.com/common/oauth2/v2.0/token',
  oauthScopes: 'offline_access openid email profile',
  oauthClientId: 'client-123',
  oauthClientSecretSet: true,
}

function renderTab(domains: ExternalDomain[] = [GMAIL, OUTLOOK]) {
  mocks.adminGetExternalDomains.mockResolvedValue(domains)
  return render(<ExternalDomainsTab addToast={addToast} />, { wrapper })
}

beforeEach(() => vi.clearAllMocks())

describe('ExternalDomainsTab — list', () => {
  it('renders the domain names', async () => {
    renderTab()
    expect(await screen.findByText('Gmail')).toBeInTheDocument()
    expect(screen.getByText('Outlook')).toBeInTheDocument()
  })

  it('renders no configuration details on the tile, only name and actions', async () => {
    renderTab()
    await screen.findByText('Gmail')
    expect(screen.queryByText('imap.gmail.com')).not.toBeInTheDocument()
    expect(screen.queryByText('993')).not.toBeInTheDocument()
  })

  it('shows an empty state when there is nothing configured', async () => {
    renderTab([])
    expect(await screen.findByText('No external domains')).toBeInTheDocument()
  })

  it('shows an error toast when loading fails', async () => {
    mocks.adminGetExternalDomains.mockRejectedValue(new Error('Server error'))
    render(<ExternalDomainsTab addToast={addToast} />, { wrapper })
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Failed to load external domains', 'error'))
  })

  // A refetch of a list holding no data puts it back to pending: that is not a first load, so the
  // tab keeps its failure text rather than a spinner, and the failure is not news a second time.
  it('draws no spinner and no second toast when the failed list is refetched', async () => {
    mocks.adminGetExternalDomains.mockRejectedValue(new Error('Server error'))
    const client = createTestQueryClient()
    render(<QueryClientProvider client={client}><ExternalDomainsTab addToast={addToast} /></QueryClientProvider>)
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Failed to load external domains', 'error'))
    const refetch = holdNextCall(mocks.adminGetExternalDomains)

    act(() => { void client.invalidateQueries({ queryKey: ['adminExternalDomains'] }) })
    await settle()
    expect(screen.queryByRole('status', { name: 'Loading' })).not.toBeInTheDocument()
    expect(screen.getByText('Could not load the external domains.')).toBeInTheDocument()
    await act(async () => { refetch.fail() })
    await settle()

    expect(mocks.adminGetExternalDomains).toHaveBeenCalledTimes(2)
    expect(addToast).toHaveBeenCalledTimes(1)
  })
})

describe('ExternalDomainsTab — create', () => {
  it('posts the full DTO on create', async () => {
    mocks.adminCreateExternalDomain.mockResolvedValue({ ...GMAIL, id: '3' })
    renderTab()
    await screen.findByText('Gmail')

    await userEvent.click(screen.getByRole('button', { name: /Add/ }))
    await userEvent.type(screen.getByLabelText('Display name'), 'Yahoo')
    await userEvent.type(screen.getByLabelText('IMAP host'), 'imap.mail.yahoo.com')
    await userEvent.clear(screen.getByLabelText('IMAP port'))
    await userEvent.type(screen.getByLabelText('IMAP port'), '993')
    await userEvent.selectOptions(screen.getByLabelText('IMAP security'), 'SslOnConnect')
    await userEvent.type(screen.getByLabelText('SMTP host'), 'smtp.mail.yahoo.com')
    await userEvent.clear(screen.getByLabelText('SMTP port'))
    await userEvent.type(screen.getByLabelText('SMTP port'), '465')
    await userEvent.selectOptions(screen.getByLabelText('SMTP security'), 'SslOnConnect')

    await userEvent.click(screen.getByRole('button', { name: 'Create domain' }))

    await waitFor(() => expect(mocks.adminCreateExternalDomain).toHaveBeenCalledWith({
      name: 'Yahoo',
      imapHost: 'imap.mail.yahoo.com',
      imapPort: 993,
      imapSecurity: 'SslOnConnect',
      smtpHost: 'smtp.mail.yahoo.com',
      smtpPort: 465,
      smtpSecurity: 'SslOnConnect',
      sieveHost: null,
      sievePort: null,
      authMode: 'Password',
      oauthAuthorizationUrl: null,
      oauthTokenUrl: null,
      oauthScopes: null,
      oauthClientId: null,
      oauthClientSecret: null,
    }))
  })

  it('includes the sieve host/port in the DTO when both are filled', async () => {
    mocks.adminCreateExternalDomain.mockResolvedValue({ ...GMAIL, id: '3' })
    renderTab()
    await screen.findByText('Gmail')

    await userEvent.click(screen.getByRole('button', { name: /Add/ }))
    await userEvent.type(screen.getByLabelText('Display name'), 'Yahoo')
    await userEvent.type(screen.getByLabelText('IMAP host'), 'imap.mail.yahoo.com')
    await userEvent.type(screen.getByLabelText('SMTP host'), 'smtp.mail.yahoo.com')
    await userEvent.type(screen.getByLabelText('Sieve host'), 'sieve.mail.yahoo.com')
    await userEvent.type(screen.getByLabelText('Sieve port'), '4190')

    await userEvent.click(screen.getByRole('button', { name: 'Create domain' }))

    await waitFor(() => expect(mocks.adminCreateExternalDomain).toHaveBeenCalledWith(
      expect.objectContaining({ sieveHost: 'sieve.mail.yahoo.com', sievePort: 4190 })
    ))
  })

  it('shows a success toast after create', async () => {
    mocks.adminCreateExternalDomain.mockResolvedValue({ ...GMAIL, id: '3' })
    renderTab()
    await screen.findByText('Gmail')
    await userEvent.click(screen.getByRole('button', { name: /Add/ }))
    await userEvent.type(screen.getByLabelText('Display name'), 'Yahoo')
    await userEvent.type(screen.getByLabelText('IMAP host'), 'imap.mail.yahoo.com')
    await userEvent.type(screen.getByLabelText('SMTP host'), 'smtp.mail.yahoo.com')
    await userEvent.click(screen.getByRole('button', { name: 'Create domain' }))
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('External domain created'))
  })

  // Server prose never reaches the screen; the local fallback does — see apiErrorMessage.
  it('shows the local fallback instead of the server message', async () => {
    mocks.adminCreateExternalDomain.mockRejectedValue(new Error('Name is already taken'))
    renderTab()
    await screen.findByText('Gmail')
    await userEvent.click(screen.getByRole('button', { name: /Add/ }))
    await userEvent.type(screen.getByLabelText('Display name'), 'Gmail')
    await userEvent.type(screen.getByLabelText('IMAP host'), 'imap.mail.yahoo.com')
    await userEvent.type(screen.getByLabelText('SMTP host'), 'smtp.mail.yahoo.com')
    await userEvent.click(screen.getByRole('button', { name: 'Create domain' }))
    await waitFor(() => expect(screen.getByText('An error occurred')).toBeInTheDocument())
  })

  it('blocks Escape while the create request is in flight', async () => {
    let resolveCreate: (value: ExternalDomain) => void = () => {}
    mocks.adminCreateExternalDomain.mockReturnValue(new Promise(resolve => { resolveCreate = resolve }))
    renderTab()
    await screen.findByText('Gmail')
    await userEvent.click(screen.getByRole('button', { name: /Add/ }))
    await userEvent.type(screen.getByLabelText('Display name'), 'Yahoo')
    await userEvent.type(screen.getByLabelText('IMAP host'), 'imap.mail.yahoo.com')
    await userEvent.type(screen.getByLabelText('SMTP host'), 'smtp.mail.yahoo.com')
    await userEvent.click(screen.getByRole('button', { name: 'Create domain' }))

    await userEvent.keyboard('{Escape}')
    expect(screen.getByLabelText('Display name')).toBeInTheDocument()

    resolveCreate({ ...GMAIL, id: '3' })
    await waitFor(() => expect(screen.queryByLabelText('Display name')).not.toBeInTheDocument())
  })
})

describe('ExternalDomainsTab — edit', () => {
  it('pre-fills every field from the domain being edited', async () => {
    renderTab()
    await screen.findByText('Outlook')
    const editButtons = screen.getAllByTitle('Edit')
    await userEvent.click(editButtons[1]!)

    expect(screen.getByLabelText('Display name')).toHaveValue('Outlook')
    expect(screen.getByLabelText('IMAP host')).toHaveValue('outlook.office365.com')
    expect(screen.getByLabelText('IMAP port')).toHaveValue(993)
    expect(screen.getByLabelText('IMAP security')).toHaveValue('SslOnConnect')
    expect(screen.getByLabelText('SMTP host')).toHaveValue('smtp.office365.com')
    expect(screen.getByLabelText('SMTP port')).toHaveValue(587)
    expect(screen.getByLabelText('SMTP security')).toHaveValue('StartTls')
    expect(screen.getByLabelText('Sieve host')).toHaveValue('sieve.office365.com')
    expect(screen.getByLabelText('Sieve port')).toHaveValue(4190)
  })

  it('labels the security options None / STARTTLS / SSL/TLS while sending the exact literals', async () => {
    renderTab()
    await screen.findByText('Gmail')
    await userEvent.click(screen.getAllByTitle('Edit')[0]!)

    const select = screen.getByLabelText('IMAP security')
    const options = Array.from(select.querySelectorAll('option'))
    expect(options.map(o => [o.value, o.textContent])).toEqual([
      ['None', 'None'],
      ['StartTls', 'STARTTLS'],
      ['SslOnConnect', 'SSL/TLS'],
    ])
  })

  it('sends the update with the edited domain id', async () => {
    mocks.adminUpdateExternalDomain.mockResolvedValue(undefined)
    renderTab()
    await screen.findByText('Gmail')
    await userEvent.click(screen.getAllByTitle('Edit')[0]!)
    await userEvent.clear(screen.getByLabelText('Display name'))
    await userEvent.type(screen.getByLabelText('Display name'), 'Gmail (personal)')
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))

    await waitFor(() => expect(mocks.adminUpdateExternalDomain).toHaveBeenCalledWith(
      GMAIL.id, expect.objectContaining({ name: 'Gmail (personal)' })
    ))
  })

  it('shows a success toast after an edit is saved', async () => {
    mocks.adminUpdateExternalDomain.mockResolvedValue(undefined)
    renderTab()
    await screen.findByText('Gmail')
    await userEvent.click(screen.getAllByTitle('Edit')[0]!)
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('External domain updated'))
  })
})

describe('ExternalDomainsTab — sieve both-or-neither', () => {
  it('shows the refusal inline when only the sieve host is filled', async () => {
    renderTab()
    await screen.findByText('Gmail')
    await userEvent.click(screen.getByRole('button', { name: /Add/ }))
    await userEvent.type(screen.getByLabelText('Display name'), 'Yahoo')
    await userEvent.type(screen.getByLabelText('IMAP host'), 'imap.mail.yahoo.com')
    await userEvent.type(screen.getByLabelText('SMTP host'), 'smtp.mail.yahoo.com')
    await userEvent.type(screen.getByLabelText('Sieve host'), 'sieve.mail.yahoo.com')

    expect(await screen.findByText('Sieve host and port must both be present or both be absent'))
      .toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Create domain' })).toBeDisabled()
  })

  it('shows the refusal inline when only the sieve port is filled', async () => {
    renderTab()
    await screen.findByText('Gmail')
    await userEvent.click(screen.getByRole('button', { name: /Add/ }))
    await userEvent.type(screen.getByLabelText('Sieve port'), '4190')

    expect(await screen.findByText('Sieve host and port must both be present or both be absent'))
      .toBeInTheDocument()
  })

  it('does not show the refusal, and allows submit, once both sieve fields are filled', async () => {
    mocks.adminCreateExternalDomain.mockResolvedValue({ ...OUTLOOK, id: '3' })
    renderTab()
    await screen.findByText('Gmail')
    await userEvent.click(screen.getByRole('button', { name: /Add/ }))
    await userEvent.type(screen.getByLabelText('Display name'), 'Yahoo')
    await userEvent.type(screen.getByLabelText('IMAP host'), 'imap.mail.yahoo.com')
    await userEvent.type(screen.getByLabelText('SMTP host'), 'smtp.mail.yahoo.com')
    await userEvent.type(screen.getByLabelText('Sieve host'), 'sieve.mail.yahoo.com')
    await userEvent.type(screen.getByLabelText('Sieve port'), '4190')

    expect(screen.queryByText('Sieve host and port must both be present or both be absent'))
      .not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Create domain' })).not.toBeDisabled()
  })
})

describe('ExternalDomainsTab — OAuth provider configuration', () => {
  async function fillBaseFields() {
    await userEvent.click(screen.getByRole('button', { name: /Add/ }))
    await userEvent.type(screen.getByLabelText('Display name'), 'Outlook')
    await userEvent.type(screen.getByLabelText('IMAP host'), 'outlook.office365.com')
    await userEvent.type(screen.getByLabelText('SMTP host'), 'smtp.office365.com')
  }

  it('shows the OAuth tag on an OAuth2 tile, and nothing on a password one', async () => {
    renderTab([GMAIL, OUTLOOK_OAUTH])
    await screen.findByText('Gmail')
    expect(screen.getByText('OAuth')).toBeInTheDocument()
    expect(screen.getByText('Outlook (OAuth)')).toBeInTheDocument()
  })

  it('reveals the provider fields on OAuth 2.0 and requires them before submitting', async () => {
    renderTab()
    await screen.findByText('Gmail')
    await fillBaseFields()

    expect(screen.queryByLabelText('Client secret')).not.toBeInTheDocument()
    await userEvent.selectOptions(screen.getByLabelText('Authentication'), 'OAuth2')
    expect(screen.getByRole('button', { name: 'Create domain' })).toBeDisabled()

    await userEvent.type(screen.getByLabelText('Authorization URL'), 'https://login.test/authorize')
    await userEvent.type(screen.getByLabelText('Token URL'), 'https://login.test/token')
    await userEvent.type(screen.getByLabelText('Scopes'), 'offline_access openid email')
    await userEvent.type(screen.getByLabelText('Client id'), 'client-123')
    expect(screen.getByRole('button', { name: 'Create domain' })).toBeDisabled()
    await userEvent.type(screen.getByLabelText('Client secret'), 'shh-secret')
    expect(screen.getByRole('button', { name: 'Create domain' })).not.toBeDisabled()

    mocks.adminCreateExternalDomain.mockResolvedValue({ ...OUTLOOK_OAUTH })
    await userEvent.click(screen.getByRole('button', { name: 'Create domain' }))
    await waitFor(() => expect(mocks.adminCreateExternalDomain).toHaveBeenCalledWith(
      expect.objectContaining({
        authMode: 'OAuth2',
        oauthAuthorizationUrl: 'https://login.test/authorize',
        oauthTokenUrl: 'https://login.test/token',
        oauthScopes: 'offline_access openid email',
        oauthClientId: 'client-123',
        oauthClientSecret: 'shh-secret',
      })
    ))
  })

  it('refuses an http authorization URL client-side', async () => {
    renderTab()
    await screen.findByText('Gmail')
    await fillBaseFields()
    await userEvent.selectOptions(screen.getByLabelText('Authentication'), 'OAuth2')

    await userEvent.type(screen.getByLabelText('Authorization URL'), 'http://login.test/authorize')
    await userEvent.type(screen.getByLabelText('Token URL'), 'https://login.test/token')
    await userEvent.type(screen.getByLabelText('Scopes'), 'openid')
    await userEvent.type(screen.getByLabelText('Client id'), 'client-123')
    await userEvent.type(screen.getByLabelText('Client secret'), 'shh')

    expect(screen.getByLabelText('Authorization URL')).toHaveClass('is-error')
    expect(screen.getByRole('button', { name: 'Create domain' })).toBeDisabled()
  })

  it('never echoes the stored secret and keeps it when the field stays empty on an edit', async () => {
    mocks.adminUpdateExternalDomain.mockResolvedValue(undefined)
    renderTab([OUTLOOK_OAUTH])
    await screen.findByText('Outlook (OAuth)')
    await userEvent.click(screen.getByTitle('Edit'))

    expect(screen.getByLabelText('Authentication')).toHaveValue('OAuth2')
    expect(screen.getByLabelText('Authorization URL'))
      .toHaveValue('https://login.microsoftonline.com/common/oauth2/v2.0/authorize')
    const secret = screen.getByLabelText('Client secret')
    expect(secret).toHaveValue('')
    expect(secret).toHaveAttribute('placeholder', 'Unchanged')

    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    await waitFor(() => expect(mocks.adminUpdateExternalDomain).toHaveBeenCalledWith(
      OUTLOOK_OAUTH.id, expect.objectContaining({ authMode: 'OAuth2', oauthClientSecret: null })
    ))
  })

  it('sends a newly typed secret on an edit', async () => {
    mocks.adminUpdateExternalDomain.mockResolvedValue(undefined)
    renderTab([OUTLOOK_OAUTH])
    await screen.findByText('Outlook (OAuth)')
    await userEvent.click(screen.getByTitle('Edit'))
    await userEvent.type(screen.getByLabelText('Client secret'), 'rotated-secret')
    await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    await waitFor(() => expect(mocks.adminUpdateExternalDomain).toHaveBeenCalledWith(
      OUTLOOK_OAUTH.id, expect.objectContaining({ oauthClientSecret: 'rotated-secret' })
    ))
  })

  it('a brand-new OAuth domain cannot be saved without a secret', async () => {
    renderTab([OUTLOOK_OAUTH])
    await screen.findByText('Outlook (OAuth)')
    await userEvent.click(screen.getByRole('button', { name: /Add/ }))
    await userEvent.type(screen.getByLabelText('Display name'), 'Provider')
    await userEvent.type(screen.getByLabelText('IMAP host'), 'imap.provider.test')
    await userEvent.type(screen.getByLabelText('SMTP host'), 'smtp.provider.test')
    await userEvent.selectOptions(screen.getByLabelText('Authentication'), 'OAuth2')
    await userEvent.type(screen.getByLabelText('Authorization URL'), 'https://login.test/authorize')
    await userEvent.type(screen.getByLabelText('Token URL'), 'https://login.test/token')
    await userEvent.type(screen.getByLabelText('Scopes'), 'openid')
    await userEvent.type(screen.getByLabelText('Client id'), 'client-123')

    expect(screen.getByLabelText('Client secret')).not.toHaveAttribute('placeholder')
    expect(screen.getByRole('button', { name: 'Create domain' })).toBeDisabled()
  })
})

describe('ExternalDomainsTab — delete', () => {
  it('confirms before deleting', async () => {
    mocks.adminDeleteExternalDomain.mockResolvedValue(undefined)
    renderTab()
    await screen.findByText('Gmail')
    await userEvent.click(screen.getAllByTitle('Delete')[0]!)

    expect(screen.getByText('Confirm deletion')).toBeInTheDocument()
    expect(mocks.adminDeleteExternalDomain).not.toHaveBeenCalled()

    const deleteButtons = screen.getAllByRole('button', { name: 'Delete' })
    await userEvent.click(deleteButtons[deleteButtons.length - 1]!)
    await waitFor(() => expect(mocks.adminDeleteExternalDomain).toHaveBeenCalledWith(GMAIL.id))
  })

  // The row's own trash button is what opened the confirm and it goes with the row, so the region
  // AdminPage hands down is where focus lands. Named here rather than in AdminPage's file because
  // this tab is mounted on its own — the region is the prop, exactly as the page passes it.
  it('hands focus to the region when the deleted row takes its own button', async () => {
    const region = document.createElement('div')
    region.tabIndex = -1
    document.body.append(region)
    try {
      let live = [GMAIL, OUTLOOK]
      mocks.adminGetExternalDomains.mockImplementation(async () => live)
      mocks.adminDeleteExternalDomain.mockImplementation(async () => {
        live = live.filter(one => one.id !== GMAIL.id)
      })
      render(<ExternalDomainsTab addToast={addToast} returnFocusRef={{ current: region }} />,
        { wrapper })
      await screen.findByText('Gmail')
      await userEvent.click(screen.getAllByTitle('Delete')[0]!)

      const buttons = screen.getAllByRole('button', { name: 'Delete' })
      await userEvent.click(buttons[buttons.length - 1]!)

      await waitFor(() => expect(screen.queryByText('Gmail')).toBeNull())
      expect(region).toHaveFocus()
    } finally { region.remove() }
  })

  // The same hand-back with a real network between the two round trips: the confirm closes on the
  // DELETE while the row leaves only when the list refetch lands, a whole macrotask later.
  it('hands focus to the region when the list refetch lands after the confirm closed', async () => {
    const region = document.createElement('div')
    region.tabIndex = -1
    document.body.append(region)
    try {
      let live = [GMAIL, OUTLOOK]
      mocks.adminGetExternalDomains.mockImplementation(
        () => new Promise(resolve => setTimeout(() => resolve(live), 30)))
      mocks.adminDeleteExternalDomain.mockImplementation(async () => {
        live = live.filter(one => one.id !== GMAIL.id)
      })
      render(<ExternalDomainsTab addToast={addToast} returnFocusRef={{ current: region }} />,
        { wrapper })
      await screen.findByText('Gmail')
      await userEvent.click(screen.getAllByTitle('Delete')[0]!)

      const buttons = screen.getAllByRole('button', { name: 'Delete' })
      await userEvent.click(buttons[buttons.length - 1]!)

      await waitFor(() => expect(screen.queryByText('Confirm deletion')).toBeNull())
      await waitFor(() => expect(screen.queryByText('Gmail')).toBeNull())
      expect(region).toHaveFocus()
    } finally { region.remove() }
  })

  it('closing the confirm modal does not delete', async () => {
    renderTab()
    await screen.findByText('Gmail')
    await userEvent.click(screen.getAllByTitle('Delete')[0]!)
    await userEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(mocks.adminDeleteExternalDomain).not.toHaveBeenCalled()
    expect(screen.queryByText('Confirm deletion')).not.toBeInTheDocument()
  })

  // domain_in_use is a named stable code, not generic prose: the refusal must stay specific,
  // just translated instead of raw off the wire — see apiErrorMessage.
  it('surfaces the domain_in_use refusal message from the API rather than a generic one', async () => {
    mocks.adminDeleteExternalDomain.mockRejectedValue(
      Object.assign(new Error('domain_in_use'), { code: 'domain_in_use' }))
    renderTab()
    await screen.findByText('Gmail')
    await userEvent.click(screen.getAllByTitle('Delete')[0]!)
    const deleteButtons = screen.getAllByRole('button', { name: 'Delete' })
    await userEvent.click(deleteButtons[deleteButtons.length - 1]!)
    await waitFor(() => expect(addToast)
      .toHaveBeenCalledWith('Accounts are still connected to this domain.', 'error'))
  })
})
