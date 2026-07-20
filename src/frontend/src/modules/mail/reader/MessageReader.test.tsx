import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import MessageReader from './MessageReader'

const mocks = vi.hoisted(() => ({
  getMailMessage: vi.fn(),
  requestBlob: vi.fn(),
  mailAttachmentUrl: vi.fn((folder: string, uid: number, part: string) =>
    `/api/Mail/Messages/Attachment?folder=${folder}&uid=${uid}&part=${part}`),
}))

vi.mock('../../../api.js', () => ({
  api: { getMailMessage: mocks.getMailMessage },
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
  to: ['mick@weesky.be'], cc: [], date: '2026-07-18T09:00:00Z',
  htmlBody: '<p>Bonjour</p>', textBody: 'Bonjour', blockedImageCount: 0,
  attachments: [
    { part: '2', fileName: 'report.pdf', contentType: 'application/pdf', size: 2048, isInline: false },
  ],
}

describe('MessageReader', () => {
  beforeEach(() => vi.clearAllMocks())

  it('prompts when nothing is selected', () => {
    render(<MessageReader folderPath="INBOX" uid={null} />, { wrapper })

    expect(screen.getByText(/select a message/i)).toBeInTheDocument()
    expect(mocks.getMailMessage).not.toHaveBeenCalled()
  })

  it('renders the headers', async () => {
    mocks.getMailMessage.mockResolvedValue(detail)

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

    expect(await screen.findByText('Re: facture')).toBeInTheDocument()
    expect(screen.getByText(/Alice Martin/)).toBeInTheDocument()
    expect(screen.getByText(/mick@weesky.be/)).toBeInTheDocument()
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
    mocks.getMailMessage.mockResolvedValue({
      ...detail,
      blockedImageCount: 2,
      htmlBody: '<img data-blocked-src="https://t.example/p.gif">',
    })

    const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

    expect(await screen.findByText(/2 remote images were blocked/i)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /show images/i }))

    expect(container.querySelector('iframe')!.getAttribute('srcdoc'))
      .toContain('src="https://t.example/p.gif"')
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

  it('surfaces a load failure', async () => {
    mocks.getMailMessage.mockRejectedValue(new Error('boom'))

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

    expect(await screen.findByText(/could not load this message/i)).toBeInTheDocument()
  })
})
