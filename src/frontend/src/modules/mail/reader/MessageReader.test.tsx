import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, createMemoryRouter, RouterProvider, useLocation } from 'react-router-dom'
import type { ReactNode } from 'react'
import { mockViewport, resetViewport, settle } from '../../../test-utils'
import type { MailFolderNode } from '../api/mailTypes'
import type { Contact } from '../../contacts/contactTypes'
import MessageReader from './MessageReader'
import { formatReaderDateShort } from './formatReaderDate'

const mocks = vi.hoisted(() => ({
  getMailMessage: vi.fn(),
  getPreferences: vi.fn(),
  setMessageFlags: vi.fn(),
  getMailFolders: vi.fn(),
  moveMessages: vi.fn(),
  copyMessages: vi.fn(),
  deleteMessages: vi.fn(),
  requestBlob: vi.fn(),
  mailAttachmentUrl: vi.fn((folder: string, uid: number, part: string) =>
    `/api/Mail/Messages/Attachment?folder=${folder}&uid=${uid}&part=${part}`),
  getIdentities: vi.fn(),
  getAliases: vi.fn(),
  prepareQuote: vi.fn(),
  getTrustedSenders: vi.fn(),
  trustSender: vi.fn(),
  untrustSender: vi.fn(),
  getContacts: vi.fn(),
  // The same class queries.ts imports from the mocked module, so its `instanceof ApiError`
  // holds against what these tests throw. A locally-declared twin would fail that check and
  // silently route every case to the generic branch.
  ApiError: class ApiError extends Error {
    status: number
    code: string | null
    constructor(message: string, status: number, code: string | null) {
      super(message)
      this.name = 'ApiError'
      this.status = status
      this.code = code
    }
  },
}))

vi.mock('../../../api.js', () => ({
  api: {
    getMailMessage: mocks.getMailMessage, getPreferences: mocks.getPreferences,
    setMessageFlags: mocks.setMessageFlags, getMailFolders: mocks.getMailFolders,
    moveMessages: mocks.moveMessages, copyMessages: mocks.copyMessages,
    deleteMessages: mocks.deleteMessages,
    getIdentities: mocks.getIdentities, getAliases: mocks.getAliases,
    prepareQuote: mocks.prepareQuote,
    getTrustedSenders: mocks.getTrustedSenders,
    trustSender: mocks.trustSender,
    untrustSender: mocks.untrustSender,
    getContacts: mocks.getContacts,
  },
  ApiError: mocks.ApiError,
  requestBlob: mocks.requestBlob,
  mailAttachmentUrl: mocks.mailAttachmentUrl,
}))

// Mutable so a test can render the reader under a connected account.
const auth = vi.hoisted(() => ({ activeAccountId: 'primary', activeEmail: 'mick@weesky.be' }))

vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({
    activeAccount: { id: auth.activeAccountId, email: auth.activeEmail },
    activeAccountId: auth.activeAccountId,
    identity: { displayName: 'Mick Weesky', email: 'mick@weesky.be' },
  }),
}))

beforeEach(() => {
  auth.activeAccountId = 'primary'
  auth.activeEmail = 'mick@weesky.be'
})

const theme = vi.hoisted(() => ({ isDark: false }))
vi.mock('../../../contexts/ThemeContext', () => ({ useTheme: () => theme }))

function folderNode(partial: Partial<MailFolderNode>): MailFolderNode {
  return {
    path: 'X', name: 'X', specialUse: null, selectable: true, subscribed: true,
    total: 0, unread: 0, uidValidity: 1, uidNext: null, highestModSeq: null, children: [], ...partial,
  }
}

const roleTree: MailFolderNode[] = [
  folderNode({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox' }),
  folderNode({ path: 'Archives', name: 'Archives', specialUse: 'archive' }),
  folderNode({ path: 'Corbeille', name: 'Corbeille', specialUse: 'trash' }),
  folderNode({ path: 'Spam', name: 'Spam', specialUse: 'junk' }),
]
const noJunkTree = roleTree.filter(node => node.specialUse !== 'junk')
const noTrashTree = roleTree.filter(node => node.specialUse !== 'trash')

// Folders are seeded fresh (staleTime Infinity), so roles resolve synchronously instead of
// racing the message load — the reader reads them off the same cache the app does.
function makeClient(tree: MailFolderNode[] = roleTree) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } })
  client.setQueryData(['mail', 'primary', 'folders'], tree)
  return client
}

function wrapper({ children }: { children: ReactNode }) {
  return (
    <QueryClientProvider client={makeClient()}>
      <MemoryRouter>{children}</MemoryRouter>
    </QueryClientProvider>
  )
}

const detail = {
  uid: 2, folderPath: 'INBOX', uidValidity: 1,
  subject: 'Re: facture', fromName: 'Alice Martin', fromAddress: 'alice@x.be',
  to: [{ name: 'Mick', address: 'mick@weesky.be' }], cc: [],
  date: '2026-07-18T09:00:00Z', authentication: null,
  messageId: 'm@x.be', references: [], inReplyTo: null, replyTo: [], bcc: [],
  spamScore: null,
  mailingList: null, sentBy: null, signedBy: null, unsubscribeUrl: null, tlsReceived: null,
  htmlBody: '<p>Bonjour</p>', textBody: 'Bonjour', blockedImageCount: 0, truncated: false,
  attachments: [
    {
      part: '2', fileName: 'report.pdf', contentType: 'application/pdf', size: 2048,
      isInline: false, contentId: null,
    },
  ],
}

const blocked = {
  ...detail,
  blockedImageCount: 2,
  htmlBody: '<img data-blocked-src="https://t.example/p.gif">',
}

const blockedBackground = {
  ...detail,
  blockedImageCount: 1,
  htmlBody: '<div data-blocked-bg="https://cdn.example/hero.png" style="background-color: #ffffff">x</div>',
}

// Seeds the list cache the reader's flag labels read at render time — findCachedSummary's
// only source of seen/flagged, since MailMessageDetail carries neither.
function renderWithCachedSummary(
  summary: { seen: boolean; flagged: boolean }, onNotify?: (message: string) => void,
) {
  const client = makeClient()
  client.setQueryData(['mail', 'primary', 'messages', 'INBOX', 0, 30], {
    folderPath: 'INBOX', page: 0, pageSize: 30, total: 1,
    messages: [{
      uid: 2, subject: 'Re: facture', fromName: 'Alice Martin', fromAddress: 'alice@x.be',
      date: detail.date, answered: false, hasAttachments: true, size: 100, preview: '',
      ...summary,
    }],
  })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <MessageReader folderPath="INBOX" uid={2} onNotify={onNotify} />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

// Seeds the inputs of the revocation gate synchronously, the way makeClient seeds the folders.
// None is observable when several are on — a trusted sender under the global setting shows no
// banner either way — so an entry absent because a query is still in flight would pass for an
// entry absent because the gate works. Seeded, every side of the guard holds from the first
// render and the gate is what the assertion is reading.
function renderWithTrusted(
  addresses: string[], preferences?: Record<string, string>,
  onNotify?: (message: string) => void, contacts?: Contact[],
) {
  const client = makeClient()
  client.setQueryData(['mail', 'primary', 'trustedSenders'], addresses)
  if (preferences) client.setQueryData(['preferences'], preferences)
  if (contacts) client.setQueryData(['contacts', 'primary'], { contacts })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <MessageReader folderPath="INBOX" uid={2} onNotify={onNotify} />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('MessageReader', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '30', 'mail.alwaysShowImages': 'false' })
    mocks.setMessageFlags.mockResolvedValue(undefined)
    mocks.getMailFolders.mockResolvedValue(roleTree)
    mocks.moveMessages.mockResolvedValue(undefined)
    mocks.copyMessages.mockResolvedValue(undefined)
    mocks.deleteMessages.mockResolvedValue(undefined)
    mocks.getIdentities.mockResolvedValue({ identities: [] })
    mocks.getAliases.mockResolvedValue([])
    mocks.getTrustedSenders.mockResolvedValue([])
    mocks.trustSender.mockResolvedValue(undefined)
    mocks.untrustSender.mockResolvedValue(undefined)
    mocks.getContacts.mockResolvedValue({ contacts: [] })
  })

  it('prompts when nothing is selected', () => {
    render(<MessageReader folderPath="INBOX" uid={null} />, { wrapper })

    expect(screen.getByText(/select a message/i)).toBeInTheDocument()
    expect(mocks.getMailMessage).not.toHaveBeenCalled()
  })

  it('renders the headers', async () => {
    mocks.getMailMessage.mockResolvedValue(detail)

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

    expect(await screen.findByText('Re: facture')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Alice Martin' })).toBeInTheDocument()
    expect(screen.getByText('Mick')).toBeInTheDocument()
  })

  it('keeps the sender address one hover away', async () => {
    mocks.getMailMessage.mockResolvedValue(detail)

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')

    // getAllByRole, not getByRole: the named recipient carries a bubble of its own.
    const bubbles = screen.getAllByRole('tooltip').map(bubble => bubble.textContent)
    expect(bubbles).toContain('"Alice Martin" <alice@x.be>')
  })

  it('hides To and Cc when the message carries neither', async () => {
    mocks.getMailMessage.mockResolvedValue({ ...detail, to: [], cc: [] })

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')

    expect(screen.queryByText(/^To:/)).not.toBeInTheDocument()
    expect(screen.queryByText(/^Cc:/)).not.toBeInTheDocument()
  })

  it('lists the Cc recipients when there are any', async () => {
    mocks.getMailMessage.mockResolvedValue({
      ...detail,
      cc: [{ name: 'Bob', address: 'bob@x.be' }, { name: '', address: 'eve@x.be' }],
    })

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')

    expect(screen.getByText('Bob')).toBeInTheDocument()
    expect(screen.getByText('eve@x.be')).toBeInTheDocument()
  })

  describe('the authentication badge', () => {
    const authenticated = (spf: string | null, dkim: string | null) => ({
      ...detail,
      authentication: { spf, dkim, dmarc: null, raw: 'mx.weesky.net; spf=x; dkim=y' },
    })

    it('vouches for a message that passed both checks', async () => {
      mocks.getMailMessage.mockResolvedValue(authenticated('pass', 'pass'))

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

      expect(await screen.findByRole('img', { name: /passed spf and dkim/i })).toBeInTheDocument()
    })

    it('shows the headers behind its claim', async () => {
      mocks.getMailMessage.mockResolvedValue(authenticated('pass', 'pass'))

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByText('Re: facture')

      const bubbles = screen.getAllByRole('tooltip').map(bubble => bubble.textContent)
      expect(bubbles.some(text => text?.includes('SPF: pass · DKIM: pass'))).toBe(true)
      expect(bubbles.some(text => text?.includes('mx.weesky.net; spf=x; dkim=y'))).toBe(true)
    })

    it('warns about a message that failed one', async () => {
      mocks.getMailMessage.mockResolvedValue(authenticated('fail', 'pass'))

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

      expect(await screen.findByRole('img', { name: /failed spf or dkim/i })).toBeInTheDocument()
    })

    // Nothing at all rather than a reassuring or an alarming badge: the checks did not run.
    // Asserted on the badge element itself, not its accessible name: a relabel in AuthBadge
    // must not silently make this stop testing anything.
    it('says nothing when the message carries no authentication headers', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)

      const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByText('Re: facture')

      expect(container.querySelector('.auth-badge')).toBeNull()
    })

    it('says nothing about a softfail', async () => {
      mocks.getMailMessage.mockResolvedValue(authenticated('softfail', 'pass'))

      const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByText('Re: facture')

      expect(container.querySelector('.auth-badge')).toBeNull()
    })

    // The backend's shape for a message whose receiving server ran checks it never reported.
    it('says nothing when the header parsed but named neither method', async () => {
      mocks.getMailMessage.mockResolvedValue({
        ...detail,
        authentication: { spf: null, dkim: null, dmarc: null, raw: 'mx.weesky.net; dmarc=pass' },
      })

      const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByText('Re: facture')

      expect(container.querySelector('.auth-badge')).toBeNull()
    })
  })

  it('renders the body in a sandboxed iframe with no scripts and no same-origin', async () => {
    mocks.getMailMessage.mockResolvedValue(detail)

    const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')

    const iframe = container.querySelector('iframe')
    expect(iframe).toBeTruthy()

    const sandbox = iframe!.getAttribute('sandbox') ?? ''
    expect(sandbox).not.toContain('allow-scripts')
    expect(sandbox).not.toContain('allow-same-origin')
    expect(iframe!.getAttribute('srcdoc')).toContain('Bonjour')
  })

  // Regression, found against a live mailbox: the sandbox was fully empty, which withholds
  // navigation as well as scripting. Every link in every message did nothing on click, in a
  // mailbox largely made of links the user had sent themselves.
  it('lets links in the body open, without granting the body any capability', async () => {
    mocks.getMailMessage.mockResolvedValue(detail)

    const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')

    const sandbox = container.querySelector('iframe')!.getAttribute('sandbox') ?? ''
    expect(sandbox).toContain('allow-popups')
    // Without the escape, the opened tab inherits this sandbox and the destination is broken.
    expect(sandbox).toContain('allow-popups-to-escape-sandbox')
    expect(sandbox).not.toContain('allow-scripts')
    expect(sandbox).not.toContain('allow-same-origin')
  })

  it('shows the sent date in full rather than a raw timestamp', async () => {
    mocks.getMailMessage.mockResolvedValue(detail)

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')

    // Seconds were the giveaway that this was an unformatted toLocaleString().
    expect(screen.queryByText(/:\d\d:\d\d/)).not.toBeInTheDocument()
    expect(screen.getByText(/2026/)).toBeInTheDocument()
  })

  it('sanitises the body a second time before rendering it', async () => {
    mocks.getMailMessage.mockResolvedValue({
      ...detail,
      htmlBody: '<p>ok</p><script>alert(1)</script><img src="x" onerror="alert(2)">',
    })

    const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')

    const srcdoc = container.querySelector('iframe')!.getAttribute('srcdoc')!
    expect(srcdoc).toContain('ok')
    expect(srcdoc).not.toContain('script')
    expect(srcdoc).not.toContain('onerror')
  })

  // The backend cuts an oversized body before it ever parses it. Saying nothing leaves a message
  // ending mid-sentence, which reads as the sender's mistake rather than ours.
  it('says so when the backend truncated the body', async () => {
    mocks.getMailMessage.mockResolvedValue({ ...detail, truncated: true })

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

    expect(await screen.findByText(/too large to display in full/i)).toBeInTheDocument()
  })

  it('says nothing when the whole body came through', async () => {
    mocks.getMailMessage.mockResolvedValue(detail)

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')

    expect(screen.queryByText(/too large to display in full/i)).not.toBeInTheDocument()
  })

  // The two ship through separate skills, so an older API answers no such field at all. Absent
  // must read as "not truncated", never as a banner on every message.
  it('says nothing when the API predates the field', async () => {
    const withoutField: Partial<typeof detail> = { ...detail }
    delete withoutField.truncated
    mocks.getMailMessage.mockResolvedValue(withoutField)

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')

    expect(screen.queryByText(/too large to display in full/i)).not.toBeInTheDocument()
  })

  it('offers to show blocked images and reveals them on demand', async () => {
    mocks.getMailMessage.mockResolvedValue(blocked)

    const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

    expect(await screen.findByText(/2 remote images were blocked/i)).toBeInTheDocument()
    expect(container.querySelector('iframe')!.getAttribute('srcdoc')).toContain('data-blocked-src')
    fireEvent.click(screen.getByRole('button', { name: /show images/i }))

    // Absence of the attribute, not just a `src="…"` match: `data-blocked-src="…"` contains it.
    await waitFor(() => expect(container.querySelector('iframe')!.getAttribute('srcdoc'))
      .not.toContain('data-blocked-src'))
    expect(container.querySelector('iframe')!.getAttribute('srcdoc'))
      .toContain('src="https://t.example/p.gif"')
  })

  // A withheld background travels a different road from a withheld <img src>: consent hands it
  // to revealBlockedImages' CSSOM path, then to darkenColours, then to DOMPurify, and any of the
  // three can drop the restored declaration. Asserted on the iframe's own document, so nothing
  // short of the whole composition can satisfy it. Dark mode because that is the only arrangement
  // in which darkenColours sits in the chain at all.
  it('restores a withheld background image on consent, all the way to the iframe', async () => {
    theme.isDark = true
    mocks.getMailMessage.mockResolvedValue(blockedBackground)

    const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

    expect(await screen.findByText(/1 remote image was blocked/i)).toBeInTheDocument()
    expect(container.querySelector('iframe')!.getAttribute('srcdoc')).not.toContain('background-image')

    fireEvent.click(screen.getByRole('button', { name: /show images/i }))

    await waitFor(() => expect(container.querySelector('iframe')!.getAttribute('srcdoc'))
      .toContain('background-image: url(&quot;https://cdn.example/hero.png&quot;)'))
    theme.isDark = false
  })

  // The whole point of the setting: no banner, no button, nothing to click per message.
  it('shows the images and no banner when the account always shows them', async () => {
    mocks.getMailMessage.mockResolvedValue(blocked)
    mocks.getPreferences.mockResolvedValue({ 'mail.alwaysShowImages': 'true' })

    const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')

    // Asserted on the absence of the attribute, not on `src="…"`: `data-blocked-src="…"`
    // contains that substring verbatim, so a positive match alone proves nothing.
    await waitFor(() => expect(container.querySelector('iframe')!.getAttribute('srcdoc'))
      .not.toContain('data-blocked-src'))
    expect(container.querySelector('iframe')!.getAttribute('srcdoc'))
      .toContain('src="https://t.example/p.gif"')
    expect(screen.queryByText(/remote images were blocked/i)).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /show images/i })).not.toBeInTheDocument()
  })

  // A cached message can render before a cold preferences cache resolves; alwaysShowImagesOf
  // must never be called on that still-undefined value.
  it('blocks images while preferences are still loading, rather than throwing', async () => {
    mocks.getMailMessage.mockResolvedValue(blocked)
    mocks.getPreferences.mockReturnValue(new Promise(() => {}))

    const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

    expect(await screen.findByText(/2 remote images were blocked/i)).toBeInTheDocument()
    expect(container.querySelector('iframe')!.getAttribute('srcdoc')).toContain('data-blocked-src')
  })

  it('keeps blocking when the account has not asked for it', async () => {
    mocks.getMailMessage.mockResolvedValue(blocked)

    const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

    expect(await screen.findByText(/2 remote images were blocked/i)).toBeInTheDocument()
    expect(container.querySelector('iframe')!.getAttribute('srcdoc'))
      .toContain('data-blocked-src')
  })

  it('does not offer the prompt when nothing was blocked', async () => {
    mocks.getMailMessage.mockResolvedValue(detail)

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')

    expect(screen.queryByRole('button', { name: /show images/i })).not.toBeInTheDocument()
  })

  it('offers the chevron beside Show images', async () => {
    mocks.getMailMessage.mockResolvedValue(blocked)

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

    expect(await screen.findByRole('button', { name: 'Show images' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'More image options' })).toBeInTheDocument()
  })

  // The address is folded before it leaves, so an approved sender still matches the message it
  // was approved from whatever casing the server reported.
  it('trusts the sender from the chevron menu, canonicalised', async () => {
    mocks.getMailMessage.mockResolvedValue({ ...blocked, fromAddress: 'Alice@X.BE' })

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    fireEvent.click(await screen.findByRole('button', { name: 'More image options' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Always show images from this sender' }))

    await waitFor(() => expect(mocks.trustSender).toHaveBeenCalledWith('alice@x.be'))
  })

  // The whole point: no banner, no button, and the images actually restored in the document.
  it('shows a trusted sender images with no banner at all', async () => {
    mocks.getTrustedSenders.mockResolvedValue(['alice@x.be'])
    mocks.getMailMessage.mockResolvedValue(blocked)

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')

    await waitFor(() =>
      expect(screen.queryByRole('button', { name: 'Show images' })).not.toBeInTheDocument())
    expect(screen.queryByText(/remote image/i)).not.toBeInTheDocument()
    expect(screen.getByTitle('Message body').getAttribute('srcdoc'))
      .toContain('src="https://t.example/p.gif"')
  })

  it('offers the revocation in the kebab for a trusted sender', async () => {
    mocks.getTrustedSenders.mockResolvedValue(['alice@x.be'])
    mocks.getMailMessage.mockResolvedValue(blocked)

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')
    await waitFor(() =>
      expect(screen.queryByRole('button', { name: 'Show images' })).not.toBeInTheDocument())

    fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))
    fireEvent.click(screen.getByRole('menuitem', { name: "Block sender's images" }))

    await waitFor(() => expect(mocks.untrustSender).toHaveBeenCalledWith('alice@x.be'))
  })

  it('offers View source as a link to the message on its own tab', async () => {
    mocks.getMailMessage.mockResolvedValue(blocked)

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')
    fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))

    const link = screen.getByRole('menuitem', { name: 'View source' })
    expect(link).toHaveAttribute('href', '/mail/source?folder=INBOX&uid=2')
    expect(link).toHaveAttribute('target', '_blank')
  })

  it('percent-encodes a folder path carrying the hierarchy separator', async () => {
    mocks.getMailMessage.mockResolvedValue(blocked)

    render(<MessageReader folderPath="INBOX/Ops & Co" uid={7} />, { wrapper })
    await screen.findByText('Re: facture')
    fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))

    expect(screen.getByRole('menuitem', { name: 'View source' }))
      .toHaveAttribute('href', '/mail/source?folder=INBOX%2FOps%20%26%20Co&uid=7')
  })

  it('keeps the revocation out of the kebab for an untrusted sender', async () => {
    mocks.getMailMessage.mockResolvedValue(blocked)

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')
    fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))

    // Archive first: it proves the menu actually opened, so the absence below is the gate's
    // doing rather than a menu that never rendered a single item.
    expect(screen.getByRole('menuitem', { name: 'Archive' })).toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: "Block sender's images" })).not.toBeInTheDocument()
  })

  // With the global setting on, revoking changes nothing visible. An entry whose effect cannot
  // be seen misleads more than an absent one helps.
  it('hides the revocation while remote images always load', async () => {
    mocks.getPreferences.mockResolvedValue(
      { 'mail.pageSize': '30', 'mail.alwaysShowImages': 'true' })
    mocks.getTrustedSenders.mockResolvedValue(['alice@x.be'])
    mocks.getMailMessage.mockResolvedValue(blocked)

    renderWithTrusted(['alice@x.be'], { 'mail.pageSize': '30', 'mail.alwaysShowImages': 'true' })
    await screen.findByText('Re: facture')
    fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))

    expect(screen.getByRole('menuitem', { name: 'Archive' })).toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: "Block sender's images" })).not.toBeInTheDocument()
  })

  // useTrustSender(onNotify), not useTrustSender(): without the argument a refused grant leaves
  // the banner up with nothing explaining the no-op, unlike every other reader mutation.
  it('reports a failed grant through onNotify', async () => {
    const onNotify = vi.fn()
    mocks.getMailMessage.mockResolvedValue(blocked)
    mocks.trustSender.mockRejectedValue(new mocks.ApiError('Internal Server Error', 500, null))

    renderWithTrusted([], undefined, onNotify)
    fireEvent.click(await screen.findByRole('button', { name: 'More image options' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Always show images from this sender' }))

    await waitFor(() =>
      expect(onNotify).toHaveBeenCalledWith("Could not allow this sender's images"))
  })

  it('reports a failed revocation with the other direction wording', async () => {
    const onNotify = vi.fn()
    mocks.getMailMessage.mockResolvedValue(blocked)
    mocks.untrustSender.mockRejectedValue(new mocks.ApiError('Internal Server Error', 500, null))

    renderWithTrusted(['alice@x.be'], undefined, onNotify)
    await screen.findByText('Re: facture')
    fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))
    fireEvent.click(screen.getByRole('menuitem', { name: "Block sender's images" }))

    await waitFor(() =>
      expect(onNotify).toHaveBeenCalledWith("Could not block this sender's images"))
  })

  // A 400 is the server refusing in words meant to be read — the ceiling, or an unreadable
  // header. Swallowing it into the generic would leave the user re-clicking forever.
  it('surfaces the server wording on a refusal rather than the generic', async () => {
    const onNotify = vi.fn()
    mocks.getMailMessage.mockResolvedValue(blocked)
    mocks.trustSender.mockRejectedValue(new mocks.ApiError(
      'You have reached the maximum of 1000 senders whose images always load', 400, null))

    renderWithTrusted([], undefined, onNotify)
    fireEvent.click(await screen.findByRole('button', { name: 'More image options' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Always show images from this sender' }))

    await waitFor(() => expect(onNotify).toHaveBeenCalledWith(
      'You have reached the maximum of 1000 senders whose images always load'))
  })

  // api.js has already cleared the session and sent them to /login; a toast on top of a
  // redirect is noise.
  it('stays silent on a 401, which the redirect already answers', async () => {
    const onNotify = vi.fn()
    mocks.getMailMessage.mockResolvedValue(blocked)
    mocks.trustSender.mockRejectedValue(new mocks.ApiError('Unauthorized', 401, null))

    renderWithTrusted([], undefined, onNotify)
    fireEvent.click(await screen.findByRole('button', { name: 'More image options' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Always show images from this sender' }))

    await waitFor(() => expect(mocks.trustSender).toHaveBeenCalled())
    await settle()
    expect(onNotify).not.toHaveBeenCalled()
  })

  describe('images of a contact', () => {
    // Seeded uncanonical on purpose: the API answers canonical addresses, so the membership test
    // has to canonicalise what it reads rather than trust the cache to hold that form.
    const inBook: Contact[] = [{
      id: 'c1', firstName: 'Alice', lastName: null, nickname: null,
      isFavorite: false, addresses: [detail.fromAddress.toUpperCase()],
    }]

    beforeEach(() => { mocks.getMailMessage.mockResolvedValue(blocked) })

    it('shows their images when the setting is on', async () => {
      renderWithTrusted([], { 'mail.trustContacts': 'true' }, undefined, inBook)
      await screen.findByText('Re: facture')

      expect(screen.queryByText(/blocked/)).toBeNull()
      expect(screen.getByTitle('Message body').getAttribute('srcdoc'))
        .toContain('src="https://t.example/p.gif"')
    })

    it('blocks the same sender when the setting is off', async () => {
      renderWithTrusted([], { 'mail.trustContacts': 'false' }, undefined, inBook)

      expect(await screen.findByText(/blocked/)).toBeInTheDocument()
    })

    it('blocks a sender the book does not hold', async () => {
      renderWithTrusted([], { 'mail.trustContacts': 'true' }, undefined, [])

      expect(await screen.findByText(/blocked/)).toBeInTheDocument()
    })

    // Revoking changes nothing on screen while the book is trusting, and the reader already
    // withholds this entry whenever something else is doing the trusting.
    it("offers no \"Block sender's images\" for a sender trusted only by the book", async () => {
      renderWithTrusted([], { 'mail.trustContacts': 'true' }, undefined, inBook)
      await screen.findByText('Re: facture')
      fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))

      expect(screen.getByRole('menuitem', { name: 'Archive' })).toBeInTheDocument()
      expect(screen.queryByRole('menuitem', { name: "Block sender's images" })).toBeNull()
    })

    // What the guard actually decides: the entry acts on the approval, which is there to revoke,
    // but the book is already showing the images so revoking would change nothing on screen.
    it('withholds it from an approved sender the book also holds', async () => {
      renderWithTrusted([detail.fromAddress], { 'mail.trustContacts': 'true' }, undefined, inBook)
      await screen.findByText('Re: facture')
      fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))

      expect(screen.getByRole('menuitem', { name: 'Archive' })).toBeInTheDocument()
      expect(screen.queryByRole('menuitem', { name: "Block sender's images" })).toBeNull()
    })

    it('still offers it for an explicitly approved sender', async () => {
      renderWithTrusted([detail.fromAddress], { 'mail.trustContacts': 'false' }, undefined, [])
      await screen.findByText('Re: facture')
      fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))

      expect(screen.getByRole('menuitem', { name: "Block sender's images" })).toBeInTheDocument()
    })

    // No book seeded on purpose: `enabled: false` stops the fetch, not a cache read.
    it('does not fetch the book when the setting is off', async () => {
      renderWithTrusted([], { 'mail.trustContacts': 'false' })

      expect(await screen.findByText(/blocked/)).toBeInTheDocument()
      expect(mocks.getContacts).not.toHaveBeenCalled()
    })
  })

  it('falls back to the text body when there is no HTML', async () => {
    mocks.getMailMessage.mockResolvedValue({ ...detail, htmlBody: '', textBody: 'plain only' })

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

    expect(await screen.findByText('plain only')).toBeInTheDocument()
  })

  it('lists attachments with their size and downloads on click', async () => {
    mocks.getMailMessage.mockResolvedValue(detail)
    mocks.requestBlob.mockResolvedValue({ blob: new Blob(['x']), fileName: 'report.pdf' })
    globalThis.URL.createObjectURL = vi.fn(() => 'blob:x')
    globalThis.URL.revokeObjectURL = vi.fn()

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

    expect(await screen.findByText('2 KB')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /report\.pdf/ }))

    await waitFor(() =>
      expect(mocks.requestBlob).toHaveBeenCalledWith(expect.stringContaining('part=2')))
  })

  // An attachment belongs to the mailbox on screen. Unscoped, the URL resolves against the
  // primary: a 404, or a file out of a mailbox the user was not looking at.
  it('builds the download URL for the account the reader is rendered under', async () => {
    auth.activeAccountId = 'linked-1'
    mocks.getMailMessage.mockResolvedValue(detail)
    mocks.requestBlob.mockResolvedValue({ blob: new Blob(['x']), fileName: 'report.pdf' })
    globalThis.URL.createObjectURL = vi.fn(() => 'blob:x')
    globalThis.URL.revokeObjectURL = vi.fn()

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    fireEvent.click(await screen.findByRole('button', { name: /report\.pdf/ }))

    await waitFor(() =>
      expect(mocks.mailAttachmentUrl).toHaveBeenCalledWith('INBOX', 2, '2', 'linked-1'))
  })

  // Server prose never reaches the screen; the local fallback does — see apiErrorMessage.
  it('surfaces a download failure instead of failing silently', async () => {
    mocks.getMailMessage.mockResolvedValue(detail)
    mocks.requestBlob.mockRejectedValue(new Error('Attachment not found'))

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    fireEvent.click(await screen.findByRole('button', { name: /report\.pdf/ }))

    expect(await screen.findByText('Could not download the attachment')).toBeInTheDocument()
  })

  describe('the image attachment split control', () => {
    const imageAttachment = {
      ...detail,
      attachments: [
        { part: '4', fileName: 'photo.png', contentType: 'image/png', size: 2048, isInline: false, contentId: null },
      ],
    }

    // These two tests stub URL.createObjectURL/revokeObjectURL (jsdom has neither); restored
    // so the stubs don't leak into later tests in the file.
    const originalCreateObjectURL = globalThis.URL.createObjectURL
    const originalRevokeObjectURL = globalThis.URL.revokeObjectURL

    afterEach(() => {
      globalThis.URL.createObjectURL = originalCreateObjectURL
      globalThis.URL.revokeObjectURL = originalRevokeObjectURL
    })

    it('gives an image attachment the split control with Download and View', async () => {
      mocks.getMailMessage.mockResolvedValue(imageAttachment)
      mocks.requestBlob.mockResolvedValue({ blob: new Blob(['x']), fileName: 'photo.png' })
      globalThis.URL.createObjectURL = vi.fn(() => 'blob:x')
      globalThis.URL.revokeObjectURL = vi.fn()

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

      fireEvent.click(await screen.findByRole('button', { name: /^photo\.png/ }))
      await waitFor(() =>
        expect(mocks.requestBlob).toHaveBeenCalledWith(expect.stringContaining('part=4')))

      fireEvent.click(screen.getByRole('button', { name: 'More actions for photo.png' }))
      expect(screen.getByRole('menuitem', { name: 'Download' })).toBeInTheDocument()
      expect(screen.getByRole('menuitem', { name: 'View' })).toBeInTheDocument()
    })

    // The viewer fetches the caller's src, so the account has to be in the URL the reader built.
    it('builds the viewer src for the account the reader is rendered under', async () => {
      auth.activeAccountId = 'linked-1'
      mocks.getMailMessage.mockResolvedValue(imageAttachment)
      mocks.requestBlob.mockResolvedValue({ blob: new Blob(['x'], { type: 'image/png' }), fileName: 'photo.png' })
      globalThis.URL.createObjectURL = vi.fn(() => 'blob:x')
      globalThis.URL.revokeObjectURL = vi.fn()

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByRole('button', { name: /^photo\.png/ })

      fireEvent.click(screen.getByRole('button', { name: 'More actions for photo.png' }))
      fireEvent.click(screen.getByRole('menuitem', { name: 'View' }))

      await screen.findByRole('img', { name: 'photo.png' })
      expect(mocks.mailAttachmentUrl).toHaveBeenCalledWith('INBOX', 2, '4', 'linked-1')
    })

    it('opens the viewer from the menu and closes it with the cross', async () => {
      mocks.getMailMessage.mockResolvedValue(imageAttachment)
      mocks.requestBlob.mockResolvedValue({ blob: new Blob(['x'], { type: 'image/png' }), fileName: 'photo.png' })
      globalThis.URL.createObjectURL = vi.fn(() => 'blob:x')
      globalThis.URL.revokeObjectURL = vi.fn()

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByRole('button', { name: /^photo\.png/ })

      fireEvent.click(screen.getByRole('button', { name: 'More actions for photo.png' }))
      fireEvent.click(screen.getByRole('menuitem', { name: 'View' }))

      expect(await screen.findByRole('img', { name: 'photo.png' })).toBeInTheDocument()

      fireEvent.click(screen.getByRole('button', { name: 'Close' }))
      expect(screen.queryByRole('img', { name: 'photo.png' })).not.toBeInTheDocument()
    })

    // Regression pin: only image/* attachments get the split control.
    it('keeps the plain chip on a non-image attachment', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByRole('button', { name: /report\.pdf/ })

      expect(screen.queryByRole('button', { name: /More actions for/ })).not.toBeInTheDocument()
    })

    // Pins the toLowerCase() in MessageReader's split-control check against a server that
    // reports the content type upper-cased.
    it('gives the split control to an upper-cased image content type too', async () => {
      mocks.getMailMessage.mockResolvedValue({
        ...imageAttachment,
        attachments: [{ ...imageAttachment.attachments[0], contentType: 'IMAGE/PNG' }],
      })

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByRole('button', { name: /^photo\.png/ })

      expect(screen.getByRole('button', { name: 'More actions for photo.png' })).toBeInTheDocument()
    })
  })

  it('hides inline parts from the attachment list', async () => {
    mocks.getMailMessage.mockResolvedValue({
      ...detail,
      attachments: [{
        part: '3', fileName: 'logo.png', contentType: 'image/png', size: 10,
        isInline: true, contentId: null,
      }],
    })

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')

    expect(screen.queryByRole('button', { name: /logo\.png/ })).not.toBeInTheDocument()
  })

  // A part the body already displays is not an attachment to offer, whatever its disposition
  // says — servers do send a cid-referenced logo as Content-Disposition: attachment.
  it('hides a body-referenced image the disposition calls an attachment', async () => {
    mocks.getMailMessage.mockResolvedValue({
      ...detail,
      htmlBody: '<p>Bonjour</p><img src="cid:logo@mail">',
      attachments: [
        {
          part: '3', fileName: 'logo.png', contentType: 'IMAGE/png', size: 10,
          isInline: false, contentId: 'logo@mail',
        },
        {
          part: '4', fileName: 'joint.png', contentType: 'image/png', size: 10,
          isInline: false, contentId: null,
        },
      ],
    })

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByRole('button', { name: /^joint\.png/ })

    expect(screen.queryByRole('button', { name: /logo\.png/ })).not.toBeInTheDocument()
  })

  // A part used only as a CSS background is displayed by the body, so it is not a chip either —
  // the rule the attachment row already applies to a cid image, reaching its second producer.
  it('hides an attachment used only as a css background', async () => {
    mocks.getMailMessage.mockResolvedValue({
      ...detail,
      htmlBody: '<div style="background-image: url(cid:logo@mail)">Bonjour</div>',
      attachments: [
        {
          part: '3', fileName: 'logo.png', contentType: 'image/png', size: 10,
          isInline: false, contentId: 'logo@mail',
        },
        {
          part: '4', fileName: 'joint.pdf', contentType: 'application/pdf', size: 10,
          isInline: false, contentId: null,
        },
      ],
    })

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByRole('button', { name: /joint\.pdf/ })

    expect(screen.queryByRole('button', { name: /logo\.png/ })).not.toBeInTheDocument()
  })

  // A cid the body references but nothing can resolve stays a broken image: the file must stay
  // reachable from the attachment row rather than disappear from both places.
  it('keeps a body-referenced part the message does not carry as an image', async () => {
    mocks.getMailMessage.mockResolvedValue({
      ...detail,
      htmlBody: '<p>Bonjour</p><img src="cid:doc@mail">',
      attachments: [{
        part: '3', fileName: 'doc.pdf', contentType: 'application/pdf', size: 10,
        isInline: false, contentId: 'doc@mail',
      }],
    })

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

    expect(await screen.findByRole('button', { name: /doc\.pdf/ })).toBeInTheDocument()
  })

  // The iframe is sandboxed opaque-origin, so no authenticated URL can load in there: the SPA
  // fetches the part itself and the body reaches the iframe with the bytes already inlined.
  it('inlines a cid image as a data URI', async () => {
    mocks.getMailMessage.mockResolvedValue({
      ...detail,
      htmlBody: '<p>Bonjour</p><img src="cid:logo@mail">',
      attachments: [{
        part: '3', fileName: 'logo.png', contentType: 'image/png', size: 10,
        isInline: true, contentId: 'logo@mail',
      }],
    })
    mocks.requestBlob.mockResolvedValue({
      blob: new Blob(['x'], { type: 'image/png' }), fileName: 'logo.png',
    })

    const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')

    await waitFor(() => expect(container.querySelector('iframe')!.getAttribute('srcdoc'))
      .toContain('src="data:image/png;base64,'))
    const srcdoc = container.querySelector('iframe')!.getAttribute('srcdoc')!
    expect(srcdoc).not.toContain('cid:')
    expect(srcdoc).toContain('Bonjour')
    expect(mocks.requestBlob).toHaveBeenCalledWith(expect.stringContaining('part=3'))
  })

  // Non-fatal by design: the body still renders, the image just stays broken.
  it('renders the body when an inline image cannot be fetched', async () => {
    mocks.getMailMessage.mockResolvedValue({
      ...detail,
      htmlBody: '<p>Bonjour</p><img src="cid:logo@mail">',
      attachments: [{
        part: '3', fileName: 'logo.png', contentType: 'image/png', size: 10,
        isInline: true, contentId: 'logo@mail',
      }],
    })
    mocks.requestBlob.mockRejectedValue(new Error('boom'))

    const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')
    await settle()

    expect(container.querySelector('iframe')!.getAttribute('srcdoc')).toContain('src="cid:logo@mail"')
    expect(screen.queryByText(/could not load this message/i)).not.toBeInTheDocument()
  })

  it('fetches nothing for a message that references no cid', async () => {
    mocks.getMailMessage.mockResolvedValue(detail)

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')
    await settle()

    expect(mocks.requestBlob).not.toHaveBeenCalled()
  })

  describe('dark mode', () => {
    // Recolouring is a rendering choice, not a fidelity one: it has to be reversible per
    // message, because a mail whose own palette recolours badly needs an escape hatch.
    it('recolours the body when the resolved theme is dark', async () => {
      theme.isDark = true
      mocks.getMailMessage.mockResolvedValue({ ...detail, htmlBody: '<p style="color: #000000">x</p>' })

      const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByTitle('Message body')

      const srcdoc = container.querySelector('iframe')!.getAttribute('srcdoc')!
      expect(srcdoc).toContain('color-scheme: dark')
      expect(srcdoc).not.toContain('#000000')
      theme.isDark = false
    })

    it('leaves the sender colours alone in light mode, and offers no way back', async () => {
      mocks.getMailMessage.mockResolvedValue({ ...detail, htmlBody: '<p style="color: #000000">x</p>' })

      const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByTitle('Message body')

      const srcdoc = container.querySelector('iframe')!.getAttribute('srcdoc')!
      expect(srcdoc).toContain('color-scheme: light')
      expect(srcdoc).toContain('#000000')
      expect(screen.queryByRole('button', { name: /original colours/i })).not.toBeInTheDocument()
    })

    it('restores the original colours on demand', async () => {
      theme.isDark = true
      mocks.getMailMessage.mockResolvedValue(detail)

      const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByTitle('Message body')

      fireEvent.click(screen.getByRole('button', { name: /original colours/i }))

      await waitFor(() =>
        expect(container.querySelector('iframe')!.getAttribute('srcdoc')).toContain('color-scheme: light'))
      theme.isDark = false
    })
  })

  describe('the spam gauge', () => {
    const scored = {
      ...detail,
      spamScore: { score: 7, threshold: 16, raw: 'X-Spamd-Result: default: False [7.00 / 16.00];' },
    }

    it('shows the gauge when the message carries a score', async () => {
      mocks.getMailMessage.mockResolvedValue(scored)

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

      expect(await screen.findByText('7.0 / 16.0')).toBeInTheDocument()
    })

    it('shows nothing when the message carries none', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByText('Re: facture')

      expect(screen.queryByText(/^Spam score:/)).not.toBeInTheDocument()
    })

    it('honours the setting that turns it off', async () => {
      mocks.getMailMessage.mockResolvedValue(scored)
      mocks.getPreferences.mockResolvedValue({ 'mail.showSpamScore': 'false' })

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByText('Re: facture')

      expect(screen.queryByText(/^Spam score:/)).not.toBeInTheDocument()
    })
  })

  it('surfaces a load failure', async () => {
    mocks.getMailMessage.mockRejectedValue(new Error('boom'))

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

    expect(await screen.findByText(/could not load this message/i)).toBeInTheDocument()
  })

  describe('the declared priority', () => {
    it('shows a chip for a high-priority message', async () => {
      mocks.getMailMessage.mockResolvedValue({ ...detail, priority: 'high' })

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

      expect(await screen.findByText('High priority')).toBeInTheDocument()
    })

    it('shows no chip at normal priority', async () => {
      mocks.getMailMessage.mockResolvedValue({ ...detail, priority: 'normal' })

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByText('Re: facture')

      expect(screen.queryByText(/priority/i)).not.toBeInTheDocument()
    })
  })

  // A stale deep link in the no-split mode must not strand the user on an error with no way
  // back — Escape works, but there is nothing on screen suggesting it.
  it('offers a back button beside a load failure when a handler is given', async () => {
    mocks.getMailMessage.mockRejectedValue(new Error('boom'))
    const onBack = vi.fn()

    render(<MessageReader folderPath="INBOX" uid={2} onBack={onBack} />, { wrapper })

    await screen.findByText(/could not load this message/i)
    fireEvent.click(screen.getByRole('button', { name: 'Back to the message list' }))
    expect(onBack).toHaveBeenCalled()
  })

  it('shows the load failure with no back button when there is no handler', async () => {
    mocks.getMailMessage.mockRejectedValue(new Error('boom'))

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

    expect(await screen.findByText(/could not load this message/i)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Back to the message list' })).toBeNull()
  })

  describe('the actions zone', () => {
    it('offers no colour toggle in light theme', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByText('Re: facture')

      expect(screen.queryByRole('button', { name: /original colours/i })).not.toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Message actions' })).toBeInTheDocument()
    })

    // Recolouring a text-only message means nothing — the same guard the banner had.
    it('offers no colour toggle for a text-only message, even in dark', async () => {
      theme.isDark = true
      mocks.getMailMessage.mockResolvedValue({ ...detail, htmlBody: '', textBody: 'plain only' })

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByText('plain only')

      expect(screen.queryByRole('button', { name: /original colours/i })).not.toBeInTheDocument()
      theme.isDark = false
    })

    it('swaps the sun for a moon once the sender colours are shown', async () => {
      theme.isDark = true
      mocks.getMailMessage.mockResolvedValue(detail)

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      fireEvent.click(await screen.findByRole('button', { name: 'Original colours' }))

      expect(await screen.findByRole('button', { name: 'Match my theme' })).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Original colours' })).not.toBeInTheDocument()
      theme.isDark = false
    })

    it('no longer shows the colour banner', async () => {
      theme.isDark = true
      mocks.getMailMessage.mockResolvedValue(detail)

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByText('Re: facture')

      expect(screen.queryByText(/colours are adapted to your dark theme/i)).not.toBeInTheDocument()
      theme.isDark = false
    })
  })

  describe('the kebab flag menu', () => {
    it('reads the cached seen/flagged state and marks unread on demand', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)
      renderWithCachedSummary({ seen: true, flagged: false })
      await screen.findByText('Re: facture')

      fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))
      expect(screen.getByRole('menuitem', { name: 'Mark as unread' })).toBeInTheDocument()
      expect(screen.getByRole('menuitem', { name: 'Star' })).toBeInTheDocument()

      fireEvent.click(screen.getByRole('menuitem', { name: 'Mark as unread' }))

      await waitFor(() =>
        expect(mocks.setMessageFlags).toHaveBeenCalledWith('INBOX', [2], 'seen', false, { accountId: 'primary' }))
    })

    // No cached summary at all (a deep link): read and unstarred, since the opening itself
    // just marked it read.
    it('falls back to read and unstarred when nothing is cached', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByText('Re: facture')

      fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))
      expect(screen.getByRole('menuitem', { name: 'Mark as unread' })).toBeInTheDocument()
      expect(screen.getByRole('menuitem', { name: 'Star' })).toBeInTheDocument()
    })

    // Proves the label actually flips off the optimistic patch, rather than assuming it: the
    // reader owns the mutation, so its pending→settled transition re-renders it, by which
    // point the patch — applied synchronously inside onMutate — is already in the cache.
    it('re-renders the label once the optimistic patch lands', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)
      renderWithCachedSummary({ seen: true, flagged: false })
      await screen.findByText('Re: facture')

      fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))
      fireEvent.click(screen.getByRole('menuitem', { name: 'Mark as unread' }))
      await settle()

      fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))
      expect(screen.getByRole('menuitem', { name: 'Mark as read' })).toBeInTheDocument()
    })

    it('reads a cached flagged message as Unstar', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)
      renderWithCachedSummary({ seen: true, flagged: true })
      await screen.findByText('Re: facture')

      fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))
      expect(screen.getByRole('menuitem', { name: 'Unstar' })).toBeInTheDocument()
    })

    it('stars the message on demand, with the full mutation payload', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)
      renderWithCachedSummary({ seen: true, flagged: false })
      await screen.findByText('Re: facture')

      fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))
      fireEvent.click(screen.getByRole('menuitem', { name: 'Star' }))

      await waitFor(() =>
        expect(mocks.setMessageFlags).toHaveBeenCalledWith('INBOX', [2], 'flagged', true, { accountId: 'primary' }))
    })

    // useSetFlags(onNotify), not useSetFlags(): without the argument, onError never reaches
    // the reader's own onNotify prop and a failed mutation would fail silently.
    it('reports a failed mutation through onNotify', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)
      mocks.setMessageFlags.mockRejectedValueOnce(new Error('boom'))
      const onNotify = vi.fn()

      renderWithCachedSummary({ seen: true, flagged: false }, onNotify)
      await screen.findByText('Re: facture')

      fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))
      fireEvent.click(screen.getByRole('menuitem', { name: 'Star' }))

      await waitFor(() => expect(onNotify).toHaveBeenCalledWith('Could not update the message'))
    })
  })

  describe('the expanded details', () => {
    const detailed = {
      ...detail,
      mailingList: '<news.weesky.net>',
      sentBy: 'a547955.bnc3.mailjet.com',
      signedBy: 'weesky.net',
      unsubscribeUrl: 'https://news.weesky.net/unsub',
      tlsReceived: true,
    }

    it('starts collapsed, showing exactly the compact header', async () => {
      mocks.getMailMessage.mockResolvedValue(detailed)

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByText('Re: facture')

      expect(screen.getByRole('button', { name: 'Show details' })).toHaveAttribute('aria-expanded', 'false')
      expect(screen.getByText(/^To:/)).toBeInTheDocument()
      expect(screen.queryByText('Mailing list:')).not.toBeInTheDocument()
    })

    it('expands into the details grid, replacing the compact lines', async () => {
      mocks.getMailMessage.mockResolvedValue(detailed)

      const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      fireEvent.click(await screen.findByRole('button', { name: 'Show details' }))

      expect(screen.getByText('Mailing list:')).toBeInTheDocument()
      expect(screen.getByText('a547955.bnc3.mailjet.com')).toBeInTheDocument()
      // Named in full: the header carries its own shorter "Unsubscribe" link alongside.
      expect(screen.getByRole('link', { name: 'Unsubscribe from this mailing list' })).toBeInTheDocument()
      expect(container.querySelector('.reader-recipients')).toBeNull()
      // Subject and spam are phone-only rows: the desktop header shows both in full already.
      expect(screen.queryByText('Subject:')).not.toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Hide details' })).toHaveAttribute('aria-expanded', 'true')
    })

    it('collapses back on a second click', async () => {
      mocks.getMailMessage.mockResolvedValue(detailed)

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      fireEvent.click(await screen.findByRole('button', { name: 'Show details' }))
      fireEvent.click(screen.getByRole('button', { name: 'Hide details' }))

      expect(screen.queryByText('Mailing list:')).not.toBeInTheDocument()
      expect(screen.getByText(/^To:/)).toBeInTheDocument()
    })

    // One-shot per message, like the image consent and the colour choice.
    it('resets to collapsed when another message is opened', async () => {
      mocks.getMailMessage.mockResolvedValue(detailed)

      const { rerender } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      fireEvent.click(await screen.findByRole('button', { name: 'Show details' }))

      mocks.getMailMessage.mockResolvedValue({ ...detailed, uid: 3, subject: 'Autre' })
      rerender(<MessageReader folderPath="INBOX" uid={3} />)
      await screen.findByText('Autre')

      expect(screen.getByRole('button', { name: 'Show details' })).toBeInTheDocument()
      expect(screen.queryByText('Mailing list:')).not.toBeInTheDocument()
    })
  })

  describe('the unsubscribe link', () => {
    it('offers a web unsubscribe without expanding the header', async () => {
      mocks.getMailMessage.mockResolvedValue({ ...detail, unsubscribeUrl: 'https://news.x.be/unsub' })

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

      const link = await screen.findByRole('link', { name: 'Unsubscribe' })
      expect(link).toHaveAttribute('href', 'https://news.x.be/unsub')
      expect(link).toHaveAttribute('target', '_blank')
      expect(link).toHaveAttribute('rel', 'noopener noreferrer')
    })

    // Following one would leave the webmail for the OS mail client; the details grid keeps it.
    it('offers nothing for a mailto-only unsubscribe', async () => {
      mocks.getMailMessage.mockResolvedValue({ ...detail, unsubscribeUrl: 'mailto:unsub@x.be' })

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByText('Re: facture')

      expect(screen.queryByRole('link', { name: 'Unsubscribe' })).not.toBeInTheDocument()
    })

    it('offers nothing when the message carries no unsubscribe link', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByText('Re: facture')

      expect(screen.queryByRole('link', { name: 'Unsubscribe' })).not.toBeInTheDocument()
    })
  })

  describe('back navigation', () => {
    it('shows a back button only when a handler is given', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)
      const onBack = vi.fn()

      render(<MessageReader folderPath="INBOX" uid={2} onBack={onBack} />, { wrapper })

      fireEvent.click(await screen.findByRole('button', { name: 'Back to the message list' }))
      expect(onBack).toHaveBeenCalled()
    })

    it('shows no back button without a handler', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByText('Re: facture')

      expect(screen.queryByRole('button', { name: 'Back to the message list' })).toBeNull()
    })

    it('goes back on Escape', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)
      const onBack = vi.fn()

      render(<MessageReader folderPath="INBOX" uid={2} onBack={onBack} />, { wrapper })
      await screen.findByText('Re: facture')
      fireEvent.keyDown(window, { key: 'Escape' })

      expect(onBack).toHaveBeenCalled()
    })

    // An Escape dispatched below both listeners reaches the picker's document handler and then
    // the reader's window handler. The picker closing is fine; backing the message out under it
    // is the double-fire this gate exists to stop.
    it('does not back out on Escape while the folder picker is open', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)
      const onBack = vi.fn()

      render(<MessageReader folderPath="INBOX" uid={2} onBack={onBack} />, { wrapper })
      await screen.findByText('Re: facture')

      fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))
      fireEvent.click(screen.getByRole('menuitem', { name: 'Move to…' }))
      fireEvent.keyDown(document, { key: 'Escape' })

      await settle()
      expect(onBack).not.toHaveBeenCalled()
    })

    // DeleteConfirmModal has no Escape handler of its own, so an unguarded reader would exit
    // the message out from under the open confirm.
    it('does not back out on Escape while the confirm-delete modal is open', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)
      const onBack = vi.fn()

      render(
        <MessageReader folderPath="Corbeille" uid={2} folderRole="trash" onBack={onBack} />,
        { wrapper })
      await screen.findByText('Re: facture')

      fireEvent.click(screen.getByRole('button', { name: 'Delete permanently' }))
      fireEvent.keyDown(document, { key: 'Escape' })

      await settle()
      expect(onBack).not.toHaveBeenCalled()
    })

    // The viewer has no Escape handler of its own to suppress the reader's ← mirror, so an
    // unguarded reader would close the picture AND navigate back on the same keypress.
    it('does not back out on Escape while the attachment viewer is open, and closes the viewer', async () => {
      const imageAttachment = {
        ...detail,
        attachments: [
          { part: '4', fileName: 'photo.png', contentType: 'image/png', size: 2048, isInline: false, contentId: null },
        ],
      }
      mocks.getMailMessage.mockResolvedValue(imageAttachment)
      mocks.requestBlob.mockResolvedValue({ blob: new Blob(['x'], { type: 'image/png' }), fileName: 'photo.png' })
      const originalCreateObjectURL = globalThis.URL.createObjectURL
      const originalRevokeObjectURL = globalThis.URL.revokeObjectURL
      globalThis.URL.createObjectURL = vi.fn(() => 'blob:x')
      globalThis.URL.revokeObjectURL = vi.fn()
      const onBack = vi.fn()

      render(<MessageReader folderPath="INBOX" uid={2} onBack={onBack} />, { wrapper })
      fireEvent.click(await screen.findByRole('button', { name: 'More actions for photo.png' }))
      fireEvent.click(screen.getByRole('menuitem', { name: 'View' }))
      expect(await screen.findByRole('img', { name: 'photo.png' })).toBeInTheDocument()

      fireEvent.keyDown(document, { key: 'Escape' })

      await waitFor(() => expect(screen.queryByRole('img', { name: 'photo.png' })).not.toBeInTheDocument())
      expect(onBack).not.toHaveBeenCalled()

      globalThis.URL.createObjectURL = originalCreateObjectURL
      globalThis.URL.revokeObjectURL = originalRevokeObjectURL
    })
  })

  describe('the message actions', () => {
    type ReaderProps = Parameters<typeof MessageReader>[0]

    function renderReader(props: Partial<ReaderProps> = {}, tree: MailFolderNode[] = roleTree) {
      render(
        <QueryClientProvider client={makeClient(tree)}>
          <MemoryRouter>
            <MessageReader folderPath="INBOX" uid={2} {...props} />
          </MemoryRouter>
        </QueryClientProvider>,
      )
    }

    // The modal's confirm button is named "Delete"; the header's own is "Delete permanently".
    const modal = () => within(document.querySelector('.modal') as HTMLElement)
    const openKebab = () => fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))

    it('deletes outside the trash by moving to the trash folder, and departs', async () => {
      const onDeparted = vi.fn()
      mocks.getMailMessage.mockResolvedValue(detail)
      renderReader({ onDeparted })
      await screen.findByText('Re: facture')

      fireEvent.click(screen.getByRole('button', { name: 'Delete' }))

      await waitFor(() =>
        expect(mocks.moveMessages).toHaveBeenCalledWith('INBOX', [2], 'Corbeille', { accountId: 'primary' }))
      expect(onDeparted).toHaveBeenCalledWith(2)
      await settle()
      expect(screen.queryByText(/confirm deletion/i)).not.toBeInTheDocument()
    })

    it('disables delete with its reason when no folder holds the trash role', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)
      renderReader({}, noTrashTree)
      await screen.findByText('Re: facture')

      const button = screen.getByRole('button', { name: 'Delete' })
      expect(button).toBeDisabled()
      expect(button).toHaveAttribute('title', 'Assign the trash folder in Settings → Folders')
    })

    it('confirms before a permanent expunge inside the trash, then departs', async () => {
      const onDeparted = vi.fn()
      mocks.getMailMessage.mockResolvedValue(detail)
      renderReader({ folderPath: 'Corbeille', folderRole: 'trash', onDeparted })
      await screen.findByText('Re: facture')

      fireEvent.click(screen.getByRole('button', { name: 'Delete permanently' }))
      expect(modal().getByText('Re: facture')).toBeInTheDocument()
      await settle()
      expect(mocks.deleteMessages).not.toHaveBeenCalled()
      expect(onDeparted).not.toHaveBeenCalled()

      fireEvent.click(modal().getByRole('button', { name: 'Delete' }))

      await waitFor(() =>
        expect(mocks.deleteMessages).toHaveBeenCalledWith('Corbeille', [2], { accountId: 'primary' }))
      expect(onDeparted).toHaveBeenCalledWith(2)
    })

    it('expunges nothing when the confirmation is dismissed', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)
      renderReader({ folderPath: 'Corbeille', folderRole: 'trash' })
      await screen.findByText('Re: facture')

      fireEvent.click(screen.getByRole('button', { name: 'Delete permanently' }))
      fireEvent.click(modal().getByRole('button', { name: '✕' }))

      await settle()
      expect(mocks.deleteMessages).not.toHaveBeenCalled()
      expect(screen.queryByText(/confirm deletion/i)).not.toBeInTheDocument()
    })

    it('archives to the folder holding the role, and departs', async () => {
      const onDeparted = vi.fn()
      mocks.getMailMessage.mockResolvedValue(detail)
      renderReader({ onDeparted })
      await screen.findByText('Re: facture')

      openKebab()
      fireEvent.click(screen.getByRole('menuitem', { name: 'Archive' }))

      await waitFor(() =>
        expect(mocks.moveMessages).toHaveBeenCalledWith('INBOX', [2], 'Archives', { accountId: 'primary' }))
      expect(onDeparted).toHaveBeenCalledWith(2)
    })

    it('offers "Report as junk" disabled with its reason when no folder holds the junk role', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)
      renderReader({}, noJunkTree)
      await screen.findByText('Re: facture')

      openKebab()
      const junk = screen.getByRole('menuitem', { name: 'Report as junk' })
      expect(junk).toBeDisabled()
      expect(junk).toHaveAttribute('title', 'Assign the junk folder in Settings → Folders')
    })

    it('reports as junk by moving to the folder holding the role', async () => {
      const onDeparted = vi.fn()
      mocks.getMailMessage.mockResolvedValue(detail)
      renderReader({ onDeparted })
      await screen.findByText('Re: facture')

      openKebab()
      fireEvent.click(screen.getByRole('menuitem', { name: 'Report as junk' }))

      await waitFor(() =>
        expect(mocks.moveMessages).toHaveBeenCalledWith('INBOX', [2], 'Spam', { accountId: 'primary' }))
      expect(onDeparted).toHaveBeenCalledWith(2)
    })

    it('moves through the picker to the chosen folder, and departs', async () => {
      const onDeparted = vi.fn()
      mocks.getMailMessage.mockResolvedValue(detail)
      renderReader({ onDeparted })
      await screen.findByText('Re: facture')

      openKebab()
      fireEvent.click(screen.getByRole('menuitem', { name: 'Move to…' }))
      fireEvent.click(screen.getByText('Archives'))
      fireEvent.click(screen.getByRole('button', { name: 'Move to Archives' }))

      await waitFor(() =>
        expect(mocks.moveMessages).toHaveBeenCalledWith('INBOX', [2], 'Archives', { accountId: 'primary' }))
      expect(onDeparted).toHaveBeenCalledWith(2)
    })

    // A copy leaves the source untouched, so nothing departs.
    it('copies through the picker without departing', async () => {
      const onDeparted = vi.fn()
      mocks.getMailMessage.mockResolvedValue(detail)
      renderReader({ onDeparted })
      await screen.findByText('Re: facture')

      openKebab()
      fireEvent.click(screen.getByRole('menuitem', { name: 'Copy to…' }))
      fireEvent.click(screen.getByText('Archives'))
      fireEvent.click(screen.getByRole('button', { name: 'Copy to Archives' }))

      await waitFor(() =>
        expect(mocks.copyMessages).toHaveBeenCalledWith('INBOX', [2], 'Archives', { accountId: 'primary' }))
      await settle()
      expect(onDeparted).not.toHaveBeenCalled()
      expect(mocks.moveMessages).not.toHaveBeenCalled()
    })

    it('reports a failed move through onNotify', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)
      mocks.moveMessages.mockRejectedValueOnce(new Error('boom'))
      const onNotify = vi.fn()
      renderReader({ onNotify })
      await screen.findByText('Re: facture')

      fireEvent.click(screen.getByRole('button', { name: 'Delete' }))

      await waitFor(() => expect(onNotify).toHaveBeenCalledWith('Could not move the message'))
    })
  })

  describe('the quote actions', () => {
    function ComposeProbe() {
      const location = useLocation()
      return <pre data-testid="compose-state">{JSON.stringify(location.state)}</pre>
    }

    function renderWithRouter(props: Partial<Parameters<typeof MessageReader>[0]> = {}) {
      const client = makeClient()
      const router = createMemoryRouter(
        [
          { path: '/mail', element: <MessageReader folderPath="INBOX" uid={2} {...props} /> },
          { path: '/mail/compose', element: <ComposeProbe /> },
        ],
        { initialEntries: ['/mail'] },
      )
      render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>)
    }

    it('prepares and navigates seeded on Reply', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)
      mocks.prepareQuote.mockResolvedValue({ quotableHtml: '<p>o</p>', attachments: [] })

      renderWithRouter()
      await screen.findByText('Re: facture')

      fireEvent.click(screen.getByRole('button', { name: 'Reply' }))

      await waitFor(() => expect(mocks.prepareQuote).toHaveBeenCalledWith('INBOX', 2, 'reply', { accountId: 'primary' }))
      const state = await screen.findByTestId('compose-state')
      const parsed = JSON.parse(state.textContent ?? '{}')
      expect(parsed.seed.subject).toMatch(/^Re:/)
    })

    // "My addresses" is the active mailbox's *and* the primary's: on a connected account a
    // reply-all was leaving the connected address in, and dropping the primary would only move
    // the same defect one address along — both mail the user themselves.
    it('leaves the active account and the primary out of a reply-all', async () => {
      auth.activeAccountId = 'linked-1'
      auth.activeEmail = 'shared@ext.example'
      mocks.getMailMessage.mockResolvedValue({
        ...detail,
        to: [{ name: '', address: 'shared@ext.example' }, { name: '', address: 'mick@weesky.be' },
          { name: '', address: 'bob@x.be' }],
        cc: [{ name: '', address: 'carol@x.be' }],
      })
      mocks.prepareQuote.mockResolvedValue({ quotableHtml: '<p>o</p>', attachments: [] })

      renderWithRouter()
      await screen.findByText('Re: facture')

      fireEvent.click(screen.getByRole('button', { name: 'Reply all' }))

      const state = await screen.findByTestId('compose-state')
      const seed = JSON.parse(state.textContent ?? '{}').seed
      expect(seed.to).toEqual(['alice@x.be', 'bob@x.be'])
      expect(seed.cc).toEqual(['carol@x.be'])
    })

    it('lets "Edit as new" live in the kebab', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)
      mocks.prepareQuote.mockResolvedValue({ quotableHtml: '<p>o</p>', attachments: [] })

      renderWithRouter()
      await screen.findByText('Re: facture')

      fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))
      fireEvent.click(screen.getByRole('menuitem', { name: 'Edit as new' }))

      await waitFor(() => expect(mocks.prepareQuote).toHaveBeenCalledWith('INBOX', 2, 'editAsNew', { accountId: 'primary' }))
    })

    it('notifies and stays when a preparation fails', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)
      mocks.prepareQuote.mockRejectedValue(new Error('over the cap'))
      const onNotify = vi.fn()

      renderWithRouter({ onNotify })
      await screen.findByText('Re: facture')

      fireEvent.click(screen.getByRole('button', { name: 'Forward' }))

      await waitFor(() => expect(onNotify).toHaveBeenCalledWith('Could not prepare the message'))
      expect(screen.queryByTestId('compose-state')).not.toBeInTheDocument()
    })
  })

  describe('the phone header', () => {
    const scored = {
      ...detail,
      spamScore: { score: 7, threshold: 16, raw: 'X-Spamd-Result: default: False [7.00 / 16.00];' },
    }

    beforeEach(() => { mockViewport('phone') })
    afterEach(() => { resetViewport() })

    it('moves the date onto the recipients line, in its short form', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)

      const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByText('Re: facture')

      const row = container.querySelector('.reader-to-row') as HTMLElement
      expect(row).toHaveTextContent('Mick')
      expect(row.textContent).toContain(formatReaderDateShort(detail.date))
      // The long form is what wrapped the sender line in two; the month name is its tell, and it
      // reads July in every timezone this date can land in.
      expect(container.querySelector('.reader-from .reader-date')).toBeNull()
      expect(screen.queryByText(/July/)).not.toBeInTheDocument()
    })

    it('keeps the date on screen when the message names no recipient', async () => {
      mocks.getMailMessage.mockResolvedValue({ ...detail, to: [] })

      const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByText('Re: facture')

      expect(container.querySelector('.reader-to-row')!.textContent)
        .toBe(formatReaderDateShort(detail.date))
    })

    it('folds the spam gauge behind the chevron', async () => {
      mocks.getMailMessage.mockResolvedValue(scored)

      const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByText('Re: facture')

      expect(screen.queryByText('7.0 / 16.0')).not.toBeInTheDocument()

      fireEvent.click(screen.getByRole('button', { name: 'Show details' }))

      expect(container.querySelector('.reader-details')).toHaveTextContent('7.0 / 16.0')
    })

    // The h1 keeps the back button and the priority badge as children, so the truncation has to
    // go on a leaf of its own — the stylesheet holds the rule, this holds the element it needs.
    it('wraps the subject in its own element, and repeats it in the details', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)

      const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByText('Re: facture')

      expect(container.querySelector('.reader-subject-text')).toHaveTextContent('Re: facture')

      fireEvent.click(screen.getByRole('button', { name: 'Show details' }))

      expect(container.querySelector('.reader-details')).toHaveTextContent('Subject:')
    })

    // The pill cost a whole line: it never fitted beside a mailing list's own name, and the grid
    // it moves behind already listed it.
    it('drops the unsubscribe pill, keeping the details row it duplicated', async () => {
      mocks.getMailMessage.mockResolvedValue({ ...detail, unsubscribeUrl: 'https://news.x.be/unsub' })

      const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByText('Re: facture')

      expect(container.querySelector('.unsub-btn')).toBeNull()

      fireEvent.click(screen.getByRole('button', { name: 'Show details' }))

      expect(screen.getByRole('link', { name: 'Unsubscribe from this mailing list' }))
        .toHaveAttribute('href', 'https://news.x.be/unsub')
    })

    it('honours the setting that turns the gauge off', async () => {
      mocks.getMailMessage.mockResolvedValue(scored)
      mocks.getPreferences.mockResolvedValue({ 'mail.showSpamScore': 'false' })

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      fireEvent.click(await screen.findByRole('button', { name: 'Show details' }))

      expect(screen.queryByText('Spam score:')).not.toBeInTheDocument()
    })
  })
})
