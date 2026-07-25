import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, createMemoryRouter, RouterProvider, useLocation } from 'react-router-dom'
import type { ReactNode } from 'react'
import { settle } from '../../../test-utils'
import type { MailFolderNode } from '../api/mailTypes'
import MessageReader from './MessageReader'

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
}))

vi.mock('../../../api.js', () => ({
  api: {
    getMailMessage: mocks.getMailMessage, getPreferences: mocks.getPreferences,
    setMessageFlags: mocks.setMessageFlags, getMailFolders: mocks.getMailFolders,
    moveMessages: mocks.moveMessages, copyMessages: mocks.copyMessages,
    deleteMessages: mocks.deleteMessages,
    getIdentities: mocks.getIdentities, getAliases: mocks.getAliases,
    prepareQuote: mocks.prepareQuote,
  },
  requestBlob: mocks.requestBlob,
  mailAttachmentUrl: mocks.mailAttachmentUrl,
}))

vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({
    activeAccount: { id: 'primary' },
    identity: { displayName: 'Mick Weesky', email: 'mick@weesky.be' },
  }),
}))

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
  htmlBody: '<p>Bonjour</p>', textBody: 'Bonjour', blockedImageCount: 0,
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
      authentication: { spf, dkim, raw: 'mx.weesky.net; spf=x; dkim=y' },
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
        authentication: { spf: null, dkim: null, raw: 'mx.weesky.net; dmarc=pass' },
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

  it('surfaces a download failure instead of failing silently', async () => {
    mocks.getMailMessage.mockResolvedValue(detail)
    mocks.requestBlob.mockRejectedValue(new Error('Attachment not found'))

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    fireEvent.click(await screen.findByRole('button', { name: /report\.pdf/ }))

    expect(await screen.findByText('Attachment not found')).toBeInTheDocument()
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
        expect(mocks.setMessageFlags).toHaveBeenCalledWith('INBOX', [2], 'seen', false))
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
        expect(mocks.setMessageFlags).toHaveBeenCalledWith('INBOX', [2], 'flagged', true))
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
        expect(mocks.moveMessages).toHaveBeenCalledWith('INBOX', [2], 'Corbeille'))
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
        expect(mocks.deleteMessages).toHaveBeenCalledWith('Corbeille', [2]))
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
        expect(mocks.moveMessages).toHaveBeenCalledWith('INBOX', [2], 'Archives'))
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
        expect(mocks.moveMessages).toHaveBeenCalledWith('INBOX', [2], 'Spam'))
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
      fireEvent.click(screen.getByRole('button', { name: 'Move' }))

      await waitFor(() =>
        expect(mocks.moveMessages).toHaveBeenCalledWith('INBOX', [2], 'Archives'))
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
      fireEvent.click(screen.getByRole('button', { name: 'Copy' }))

      await waitFor(() =>
        expect(mocks.copyMessages).toHaveBeenCalledWith('INBOX', [2], 'Archives'))
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

      await waitFor(() => expect(mocks.prepareQuote).toHaveBeenCalledWith('INBOX', 2, 'reply'))
      const state = await screen.findByTestId('compose-state')
      const parsed = JSON.parse(state.textContent ?? '{}')
      expect(parsed.seed.subject).toMatch(/^Re:/)
    })

    it('lets "Edit as new" live in the kebab', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)
      mocks.prepareQuote.mockResolvedValue({ quotableHtml: '<p>o</p>', attachments: [] })

      renderWithRouter()
      await screen.findByText('Re: facture')

      fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))
      fireEvent.click(screen.getByRole('menuitem', { name: 'Edit as new' }))

      await waitFor(() => expect(mocks.prepareQuote).toHaveBeenCalledWith('INBOX', 2, 'editAsNew'))
    })

    it('notifies and stays when a preparation fails', async () => {
      mocks.getMailMessage.mockResolvedValue(detail)
      mocks.prepareQuote.mockRejectedValue(new Error('over the cap'))
      const onNotify = vi.fn()

      renderWithRouter({ onNotify })
      await screen.findByText('Re: facture')

      fireEvent.click(screen.getByRole('button', { name: 'Forward' }))

      await waitFor(() => expect(onNotify).toHaveBeenCalledWith('over the cap'))
      expect(screen.queryByTestId('compose-state')).not.toBeInTheDocument()
    })
  })
})
