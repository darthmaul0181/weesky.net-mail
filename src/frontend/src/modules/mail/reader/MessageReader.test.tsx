import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import MessageReader from './MessageReader'

const mocks = vi.hoisted(() => ({
  getMailMessage: vi.fn(),
  getPreferences: vi.fn(),
  requestBlob: vi.fn(),
  mailAttachmentUrl: vi.fn((folder: string, uid: number, part: string) =>
    `/api/Mail/Messages/Attachment?folder=${folder}&uid=${uid}&part=${part}`),
}))

vi.mock('../../../api.js', () => ({
  api: { getMailMessage: mocks.getMailMessage, getPreferences: mocks.getPreferences },
  requestBlob: mocks.requestBlob,
  mailAttachmentUrl: mocks.mailAttachmentUrl,
}))

vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({ activeAccount: { id: 'primary' } }),
}))

const theme = vi.hoisted(() => ({ isDark: false }))
vi.mock('../../../contexts/ThemeContext', () => ({ useTheme: () => theme }))

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

const detail = {
  uid: 2, folderPath: 'INBOX', uidValidity: 1,
  subject: 'Re: facture', fromName: 'Alice Martin', fromAddress: 'alice@x.be',
  to: [{ name: 'Mick', address: 'mick@weesky.be' }], cc: [],
  date: '2026-07-18T09:00:00Z', authentication: null,
  spamScore: null,
  mailingList: null, sentBy: null, signedBy: null, unsubscribeUrl: null, tlsReceived: null,
  htmlBody: '<p>Bonjour</p>', textBody: 'Bonjour', blockedImageCount: 0,
  attachments: [
    { part: '2', fileName: 'report.pdf', contentType: 'application/pdf', size: 2048, isInline: false },
  ],
}

const blocked = {
  ...detail,
  blockedImageCount: 2,
  htmlBody: '<img data-blocked-src="https://t.example/p.gif">',
}

describe('MessageReader', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '30', 'mail.alwaysShowImages': 'false' })
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

  it('hides inline parts from the attachment list', async () => {
    mocks.getMailMessage.mockResolvedValue({
      ...detail,
      attachments: [{ part: '3', fileName: 'logo.png', contentType: 'image/png', size: 10, isInline: true }],
    })

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')

    expect(screen.queryByRole('button', { name: /logo\.png/ })).not.toBeInTheDocument()
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
})
