import { describe, it, expect, vi, beforeEach } from 'vitest'
import { act, render, screen, waitFor } from '@testing-library/react'
import { QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { createTestQueryClient, holdNextCall, optionsOf, pickOption, pasteInto, settle, setupUser } from '../../../test-utils'
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

let user: ReturnType<typeof setupUser>
beforeEach(() => {
  vi.clearAllMocks()
  user = setupUser()
})

const fill = (label: string, value: string) => pasteInto(user, screen.getByLabelText(label), value)

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
  it('posts the full DTO on create, then confirms it', async () => {
    mocks.adminCreateExternalDomain.mockResolvedValue({ ...GMAIL, id: '3' })
    renderTab()
    await screen.findByText('Gmail')

    await user.click(screen.getByRole('button', { name: /Add/ }))
    await fill('Display name', 'Yahoo')
    await fill('IMAP host', 'imap.mail.yahoo.com')
    await user.clear(screen.getByLabelText('IMAP port'))
    await fill('IMAP port', '993')
    await pickOption(screen.getByLabelText('IMAP security'), 'SSL/TLS')
    await fill('SMTP host', 'smtp.mail.yahoo.com')
    await user.clear(screen.getByLabelText('SMTP port'))
    await fill('SMTP port', '465')
    await pickOption(screen.getByLabelText('SMTP security'), 'SSL/TLS')

    await user.click(screen.getByRole('button', { name: 'Create domain' }))

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
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('External domain created'))
  })

  it('allows submit once both sieve fields are filled, and sends them in the DTO', async () => {
    mocks.adminCreateExternalDomain.mockResolvedValue({ ...GMAIL, id: '3' })
    renderTab()
    await screen.findByText('Gmail')

    await user.click(screen.getByRole('button', { name: /Add/ }))
    await fill('Display name', 'Yahoo')
    await fill('IMAP host', 'imap.mail.yahoo.com')
    await fill('SMTP host', 'smtp.mail.yahoo.com')
    await fill('Sieve host', 'sieve.mail.yahoo.com')
    await fill('Sieve port', '4190')
    expect(screen.queryByText('Sieve host and port must both be present or both be absent'))
      .not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Create domain' })).not.toBeDisabled()

    await user.click(screen.getByRole('button', { name: 'Create domain' }))

    await waitFor(() => expect(mocks.adminCreateExternalDomain).toHaveBeenCalledWith(
      expect.objectContaining({ sieveHost: 'sieve.mail.yahoo.com', sievePort: 4190 })
    ))
  })

  // Server prose never reaches the screen; the local fallback does — see apiErrorMessage.
  it('shows the local fallback instead of the server message', async () => {
    mocks.adminCreateExternalDomain.mockRejectedValue(new Error('Name is already taken'))
    renderTab()
    await screen.findByText('Gmail')
    await user.click(screen.getByRole('button', { name: /Add/ }))
    await fill('Display name', 'Gmail')
    await fill('IMAP host', 'imap.mail.yahoo.com')
    await fill('SMTP host', 'smtp.mail.yahoo.com')
    await user.click(screen.getByRole('button', { name: 'Create domain' }))
    await waitFor(() => expect(screen.getByText('An error occurred')).toBeInTheDocument())
  })

  it('blocks Escape while the create request is in flight', async () => {
    let resolveCreate: (value: ExternalDomain) => void = () => {}
    mocks.adminCreateExternalDomain.mockReturnValue(new Promise(resolve => { resolveCreate = resolve }))
    renderTab()
    await screen.findByText('Gmail')
    await user.click(screen.getByRole('button', { name: /Add/ }))
    await fill('Display name', 'Yahoo')
    await fill('IMAP host', 'imap.mail.yahoo.com')
    await fill('SMTP host', 'smtp.mail.yahoo.com')
    await user.click(screen.getByRole('button', { name: 'Create domain' }))

    await user.keyboard('{Escape}')
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
    await user.click(editButtons[1]!)

    expect(screen.getByLabelText('Display name')).toHaveValue('Outlook')
    expect(screen.getByLabelText('IMAP host')).toHaveValue('outlook.office365.com')
    expect(screen.getByLabelText('IMAP port')).toHaveValue(993)
    expect(screen.getByLabelText('IMAP security')).toHaveTextContent('SSL/TLS')
    expect(screen.getByLabelText('SMTP host')).toHaveValue('smtp.office365.com')
    expect(screen.getByLabelText('SMTP port')).toHaveValue(587)
    expect(screen.getByLabelText('SMTP security')).toHaveTextContent('STARTTLS')
    expect(screen.getByLabelText('Sieve host')).toHaveValue('sieve.office365.com')
    expect(screen.getByLabelText('Sieve port')).toHaveValue(4190)
  })

  it('labels the security options None / STARTTLS / SSL/TLS while sending the exact literals', async () => {
    renderTab()
    await screen.findByText('Gmail')
    await user.click(screen.getAllByTitle('Edit')[0]!)

    const select = screen.getByLabelText('IMAP security')
    expect(await optionsOf(select)).toEqual(['None', 'STARTTLS', 'SSL/TLS'])
  })

  it('sends the update with the edited domain id', async () => {
    mocks.adminUpdateExternalDomain.mockResolvedValue(undefined)
    renderTab()
    await screen.findByText('Gmail')
    await user.click(screen.getAllByTitle('Edit')[0]!)
    await user.clear(screen.getByLabelText('Display name'))
    await fill('Display name', 'Gmail (personal)')
    await user.click(screen.getByRole('button', { name: 'Save changes' }))

    await waitFor(() => expect(mocks.adminUpdateExternalDomain).toHaveBeenCalledWith(
      GMAIL.id, expect.objectContaining({ name: 'Gmail (personal)' })
    ))
  })

  it('shows a success toast after an edit is saved', async () => {
    mocks.adminUpdateExternalDomain.mockResolvedValue(undefined)
    renderTab()
    await screen.findByText('Gmail')
    await user.click(screen.getAllByTitle('Edit')[0]!)
    await user.click(screen.getByRole('button', { name: 'Save changes' }))
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('External domain updated'))
  })
})

describe('ExternalDomainsTab — sieve both-or-neither', () => {
  it.each([
    ['host', 'Sieve host', 'sieve.mail.yahoo.com'],
    ['port', 'Sieve port', '4190'],
  ])('shows the refusal inline when only the sieve %s is filled', async (_which, label, value) => {
    renderTab()
    await screen.findByText('Gmail')
    await user.click(screen.getByRole('button', { name: /Add/ }))
    await fill('Display name', 'Yahoo')
    await fill('IMAP host', 'imap.mail.yahoo.com')
    await fill('SMTP host', 'smtp.mail.yahoo.com')
    await fill(label, value)

    expect(await screen.findByText('Sieve host and port must both be present or both be absent'))
      .toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Create domain' })).toBeDisabled()
  })
})

describe('ExternalDomainsTab — OAuth provider configuration', () => {
  async function fillBaseFields() {
    await user.click(screen.getByRole('button', { name: /Add/ }))
    await fill('Display name', 'Outlook')
    await fill('IMAP host', 'outlook.office365.com')
    await fill('SMTP host', 'smtp.office365.com')
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
    await pickOption(screen.getByLabelText('Authentication'), 'OAuth 2.0')
    expect(screen.getByRole('button', { name: 'Create domain' })).toBeDisabled()

    await fill('Authorization URL', 'https://login.test/authorize')
    await fill('Token URL', 'https://login.test/token')
    await fill('Scopes', 'offline_access openid email')
    await fill('Client id', 'client-123')
    expect(screen.getByLabelText('Client secret')).not.toHaveAttribute('placeholder')
    expect(screen.getByRole('button', { name: 'Create domain' })).toBeDisabled()
    await fill('Client secret', 'shh-secret')
    expect(screen.getByRole('button', { name: 'Create domain' })).not.toBeDisabled()

    mocks.adminCreateExternalDomain.mockResolvedValue({ ...OUTLOOK_OAUTH })
    await user.click(screen.getByRole('button', { name: 'Create domain' }))
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
    await pickOption(screen.getByLabelText('Authentication'), 'OAuth 2.0')

    await fill('Authorization URL', 'http://login.test/authorize')
    await fill('Token URL', 'https://login.test/token')
    await fill('Scopes', 'openid')
    await fill('Client id', 'client-123')
    await fill('Client secret', 'shh')

    expect(screen.getByLabelText('Authorization URL')).toHaveClass('is-error')
    expect(screen.getByRole('button', { name: 'Create domain' })).toBeDisabled()
  })

  it('never echoes the stored secret and keeps it when the field stays empty on an edit', async () => {
    mocks.adminUpdateExternalDomain.mockResolvedValue(undefined)
    renderTab([OUTLOOK_OAUTH])
    await screen.findByText('Outlook (OAuth)')
    await user.click(screen.getByTitle('Edit'))

    expect(screen.getByLabelText('Authentication')).toHaveTextContent('OAuth 2.0')
    expect(screen.getByLabelText('Authorization URL'))
      .toHaveValue('https://login.microsoftonline.com/common/oauth2/v2.0/authorize')
    const secret = screen.getByLabelText('Client secret')
    expect(secret).toHaveValue('')
    expect(secret).toHaveAttribute('placeholder', 'Unchanged')

    await user.click(screen.getByRole('button', { name: 'Save changes' }))
    await waitFor(() => expect(mocks.adminUpdateExternalDomain).toHaveBeenCalledWith(
      OUTLOOK_OAUTH.id, expect.objectContaining({ authMode: 'OAuth2', oauthClientSecret: null })
    ))
  })

  it('sends a newly typed secret on an edit', async () => {
    mocks.adminUpdateExternalDomain.mockResolvedValue(undefined)
    renderTab([OUTLOOK_OAUTH])
    await screen.findByText('Outlook (OAuth)')
    await user.click(screen.getByTitle('Edit'))
    await fill('Client secret', 'rotated-secret')
    await user.click(screen.getByRole('button', { name: 'Save changes' }))
    await waitFor(() => expect(mocks.adminUpdateExternalDomain).toHaveBeenCalledWith(
      OUTLOOK_OAUTH.id, expect.objectContaining({ oauthClientSecret: 'rotated-secret' })
    ))
  })
})

describe('ExternalDomainsTab — delete', () => {
  it('confirms before deleting', async () => {
    mocks.adminDeleteExternalDomain.mockResolvedValue(undefined)
    renderTab()
    await screen.findByText('Gmail')
    await user.click(screen.getAllByTitle('Delete')[0]!)

    expect(screen.getByText('Confirm deletion')).toBeInTheDocument()
    expect(mocks.adminDeleteExternalDomain).not.toHaveBeenCalled()

    const deleteButtons = screen.getAllByRole('button', { name: 'Delete' })
    await user.click(deleteButtons[deleteButtons.length - 1]!)
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
      await user.click(screen.getAllByTitle('Delete')[0]!)

      const buttons = screen.getAllByRole('button', { name: 'Delete' })
      await user.click(buttons[buttons.length - 1]!)

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
      await user.click(screen.getAllByTitle('Delete')[0]!)

      const buttons = screen.getAllByRole('button', { name: 'Delete' })
      await user.click(buttons[buttons.length - 1]!)

      await waitFor(() => expect(screen.queryByText('Confirm deletion')).toBeNull())
      await waitFor(() => expect(screen.queryByText('Gmail')).toBeNull())
      expect(region).toHaveFocus()
    } finally { region.remove() }
  })

  it('closing the confirm modal does not delete', async () => {
    renderTab()
    await screen.findByText('Gmail')
    await user.click(screen.getAllByTitle('Delete')[0]!)
    await user.click(screen.getByRole('button', { name: 'Close' }))
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
    await user.click(screen.getAllByTitle('Delete')[0]!)
    const deleteButtons = screen.getAllByRole('button', { name: 'Delete' })
    await user.click(deleteButtons[deleteButtons.length - 1]!)
    await waitFor(() => expect(addToast)
      .toHaveBeenCalledWith('Accounts are still connected to this domain.', 'error'))
  })
})
