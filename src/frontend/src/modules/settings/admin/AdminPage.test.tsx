import { screen, waitFor, fireEvent } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import Toasts from '../../../components/Toasts'
import { useToasts } from '../../../hooks/useToasts'
import enAdmin from '../../../locales/en/admin.json'
import frAdmin from '../../../locales/fr/admin.json'
import { createTestQueryClient, settle, setupUser } from '../../../test-utils'
import AdminPage from './AdminPage'
import { AccountsTab } from './AccountsTab'
import { DomainsTab } from './DomainsTab'
import { VirtualDomainsTab } from './VirtualDomainsTab'
import ExternalDomainsTab from './ExternalDomainsTab'
import { MB, MOCK_DOMAINS, MOCK_USERS, MOCK_VIRTUAL_DOMAINS, confirmButton, render } from './adminTestFixtures'

const mocks = vi.hoisted(() => ({
  adminGetUsers: vi.fn(),
  adminDeleteUser: vi.fn(),
  adminGetDomains: vi.fn(),
  adminGetUserQuota: vi.fn(),
  adminGetVirtualDomains: vi.fn(),
  adminGetExternalDomains: vi.fn(),
  getAppSettings: vi.fn(),
  setAppSetting: vi.fn(),
}))

vi.mock('../../../api.js', () => ({ api: mocks, clearSession: vi.fn() }))
// The Application tab's three sections each have their own test file and their own API calls.
vi.mock('./LogoSection', () => ({ default: () => null }))
vi.mock('./SchedulingAccountSection', () => ({ default: () => null }))
vi.mock('./DeliveryRepliesSection', () => ({ default: () => null }))

const GMAIL = {
  id: '1', name: 'Gmail', imapHost: 'imap.gmail.com', imapPort: 993, imapSecurity: 'SslOnConnect',
  smtpHost: 'smtp.gmail.com', smtpPort: 587, smtpSecurity: 'StartTls',
}

let user: ReturnType<typeof setupUser>
beforeEach(() => {
  vi.clearAllMocks()
  mocks.adminGetUsers.mockResolvedValue(MOCK_USERS)
  mocks.adminGetVirtualDomains.mockResolvedValue(MOCK_VIRTUAL_DOMAINS)
  mocks.adminGetDomains.mockResolvedValue(MOCK_DOMAINS)
  mocks.adminGetExternalDomains.mockResolvedValue([GMAIL])
  mocks.adminGetUserQuota.mockRejectedValue(new Error('unavailable'))
  mocks.getAppSettings.mockResolvedValue({
    'app.installable': 'true', 'app.name': 'Weesky Mail', 'app.shortName': 'Weesky',
  })
  mocks.setAppSetting.mockResolvedValue(undefined)
  user = setupUser()
})

function renderAdminPage() {
  return render(<AdminPage />)
}

describe('AdminPage', () => {
  it.each([
    ['Domains', () => screen.findByText('WSY')],
    ['Virtual domains', () => screen.findByText('extra.com')],
    ['External domains', () => screen.findByText('Gmail')],
    ['Application', async () => expect(await screen.findByLabelText('Application name')).toHaveValue('Weesky Mail')],
  ])('switches to the %s tab and shows its content', async (tab, content) => {
    renderAdminPage()
    await user.click(screen.getByRole('button', { name: tab }))
    expect(screen.getByRole('button', { name: tab })).toHaveClass('is-active')
    await content()
  })

  // R2: the list the tab already showed comes back from the cache, not from behind a spinner.
  it('shows the cached accounts at once when the admin returns to the tab', async () => {
    renderAdminPage()
    await screen.findByText('alice@weesky.be')
    await user.click(screen.getByRole('button', { name: 'Domains' }))
    await screen.findByText('WSY')

    fireEvent.click(screen.getByRole('button', { name: 'Accounts' }))

    expect(screen.getByRole('button', { name: 'Accounts' })).toHaveClass('is-active')
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
    render(<AdminPage />, createTestQueryClient({ queries: { staleTime: 30_000 } }))
    const calls = () => [mocks.adminGetUsers, mocks.adminGetDomains, mocks.adminGetVirtualDomains, mocks.adminGetUserQuota]
      .map(f => f.mock.calls.length)
    await screen.findByText('alice@weesky.be')
    await settle()
    expect(calls()).toEqual([1, 1, 0, 1])

    await user.click(screen.getByRole('button', { name: 'Domains' }))
    await screen.findByText('WSY')
    await user.click(screen.getByRole('button', { name: 'Accounts' }))
    await screen.findByText('alice@weesky.be')
    await settle()

    expect(calls()).toEqual([1, 1, 0, 1])
  })

  // Accounts offers no help, and that is the decision rather than the missing key it looked like:
  // the tab explains itself, and a "?" with nothing behind it is worse than none.
  it('offers no help on the Accounts tab, active by default', async () => {
    renderAdminPage()
    await waitFor(() => expect(screen.getByRole('button', { name: 'Accounts' })).toHaveClass('is-active'))

    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument()
  })

  it('shows the matching help text on every tab that has some', async () => {
    renderAdminPage()

    await user.click(screen.getByRole('button', { name: 'Domains' }))
    expect(screen.getByRole('tooltip')).toHaveTextContent('A domain is a mail domain hosted directly')

    await user.click(screen.getByRole('button', { name: 'Virtual domains' }))
    expect(screen.getByRole('tooltip')).toHaveTextContent('A virtual alias domain is a domain with no mailboxes')

    await user.click(screen.getByRole('button', { name: 'External domains' }))
    expect(screen.getByRole('tooltip')).toHaveTextContent('Define the external mail providers')

    await user.click(screen.getByRole('button', { name: 'Application' }))
    expect(screen.getByRole('tooltip')).toHaveTextContent('Offers the webmail for installation')
  })

  it('states the Application help’s consequence, with a verb, in both languages', () => {
    expect(enAdmin.help.application).toMatch(/does not notify the guests/)
    expect(frAdmin.help.application).toMatch(/ne prévient pas les invités/)
  })

  // The deleted row's own button leaves with the refetched list, after the confirm has closed:
  // the page's own region is what takes the focus back.
  it('hands focus to the tab content when the deleted account goes with the reload', async () => {
    mocks.adminDeleteUser.mockResolvedValue(null)
    const { container } = renderAdminPage()
    await screen.findByText('alice@weesky.be')
    await user.click(screen.getByTitle('Delete'))

    await user.click(confirmButton())

    await waitFor(() => expect(mocks.adminDeleteUser).toHaveBeenCalledWith(1))
    expect(container.querySelector('.admin-tab-content')).toHaveFocus()
  })
})

describe('Admin tabs — load failures', () => {
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
})
