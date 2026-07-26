import { describe, it, expect, vi, beforeEach } from 'vitest'
import { act, render, screen, waitFor } from '@testing-library/react'
import { focusManager, QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { settle } from '../../../test-utils'
import type { MailAttachmentInfo } from '../api/mailTypes'
import { useInlineImages } from './useInlineImages'

const mocks = vi.hoisted(() => ({
  requestBlob: vi.fn(),
  mailAttachmentUrl: vi.fn((folder: string, uid: number, part: string) =>
    `/api/Mail/Messages/Attachment?folder=${folder}&uid=${uid}&part=${part}`),
}))

vi.mock('../../../api.js', () => ({
  api: {}, requestBlob: mocks.requestBlob, mailAttachmentUrl: mocks.mailAttachmentUrl,
}))

vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({ activeAccount: { id: 'primary' } }),
}))

let client: QueryClient
function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

const part = (over: Partial<MailAttachmentInfo> = {}): MailAttachmentInfo => ({
  part: '2', fileName: 'logo.png', contentType: 'image/png', size: 10,
  isInline: true, contentId: 'logo@mail', ...over,
})

const png = (byte: string) => ({ blob: new Blob([byte], { type: 'image/png' }), fileName: 'logo.png' })

interface HostProps {
  folder?: string | null
  uid?: number | null
  attachments?: MailAttachmentInfo[]
  html?: string
}

function Host({ folder = 'INBOX', uid = 2, attachments, html = '' }: HostProps) {
  const inline = useInlineImages(folder, uid, attachments, html)
  return <pre data-testid="inline">{JSON.stringify(inline)}</pre>
}

const inlined = () => JSON.parse(screen.getByTestId('inline').textContent || '{}')

describe('useInlineImages', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  })

  it('answers the referenced part as a data URI keyed by its cid', async () => {
    mocks.requestBlob.mockResolvedValue(png('x'))

    render(<Host attachments={[part()]} html='<img src="cid:logo@mail">' />, { wrapper })

    await waitFor(() => expect(inlined()['logo@mail']).toMatch(/^data:image\/png;base64,/))
    expect(mocks.requestBlob).toHaveBeenCalledWith(expect.stringContaining('part=2'))
  })

  it('answers nothing while the fetch is in flight', async () => {
    mocks.requestBlob.mockReturnValue(new Promise(() => {}))

    render(<Host attachments={[part()]} html='<img src="cid:logo@mail">' />, { wrapper })
    await settle()

    expect(inlined()).toEqual({})
  })

  it('fetches nothing when the body references no cid', async () => {
    render(<Host attachments={[part()]} html='<p>Bonjour</p>' />, { wrapper })
    await settle()

    expect(inlined()).toEqual({})
    expect(mocks.requestBlob).not.toHaveBeenCalled()
  })

  // The body is empty until the detail lands, and the reader calls the hook on every render.
  it('fetches nothing before the detail has loaded', async () => {
    render(<Host attachments={undefined} html="" />, { wrapper })
    await settle()

    expect(mocks.requestBlob).not.toHaveBeenCalled()
  })

  it('fetches nothing without a folder or a uid', async () => {
    render(<Host folder={null} uid={null} attachments={[part()]} html='<img src="cid:logo@mail">' />,
      { wrapper })
    await settle()

    expect(mocks.requestBlob).not.toHaveBeenCalled()
  })

  it('fetches nothing when no part carries the referenced cid', async () => {
    render(<Host attachments={[part({ contentId: 'other@mail' })]} html='<img src="cid:logo@mail">' />,
      { wrapper })
    await settle()

    expect(inlined()).toEqual({})
    expect(mocks.requestBlob).not.toHaveBeenCalled()
  })

  // Same rule the compose side applies: a cid pointing at a non-image stays broken.
  it('leaves a cid pointing at a non-image part alone', async () => {
    render(
      <Host
        attachments={[part({ contentType: 'application/pdf', fileName: 'doc.pdf' })]}
        html='<img src="cid:logo@mail">'
      />, { wrapper })
    await settle()

    expect(inlined()).toEqual({})
    expect(mocks.requestBlob).not.toHaveBeenCalled()
  })

  // RFC 2045 makes a MIME type case-insensitive, and this mailbox's server reports "IMAGE/jpeg".
  it('resolves a part whose content type is upper-cased', async () => {
    mocks.requestBlob.mockResolvedValue(png('x'))

    render(<Host attachments={[part({ contentType: 'IMAGE/JPEG' })]} html='<img src="cid:logo@mail">' />,
      { wrapper })

    await waitFor(() => expect(inlined()['logo@mail']).toBeTruthy())
  })

  it('never fetches an inline part the body does not reference', async () => {
    mocks.requestBlob.mockResolvedValue(png('x'))

    render(
      <Host
        attachments={[part(), part({ part: '3', contentId: 'unused@mail' })]}
        html='<img src="cid:logo@mail">'
      />, { wrapper })

    await waitFor(() => expect(inlined()['logo@mail']).toBeTruthy())
    expect(mocks.requestBlob).toHaveBeenCalledTimes(1)
  })

  // Non-fatal by design: one broken image must not cost the reader the others, or the body.
  it('keeps the parts that came back when one fetch fails', async () => {
    mocks.requestBlob.mockImplementation((url: string) =>
      url.includes('part=3') ? Promise.reject(new Error('boom')) : Promise.resolve(png('x')))

    render(
      <Host
        attachments={[part(), part({ part: '3', contentId: 'broken@mail' })]}
        html='<img src="cid:logo@mail"><img src="cid:broken@mail">'
      />, { wrapper })

    await waitFor(() => expect(inlined()['logo@mail']).toBeTruthy())
    expect(inlined()['broken@mail']).toBeUndefined()
  })

  it('caches per folder and uid rather than refetching on every render', async () => {
    mocks.requestBlob.mockResolvedValue(png('x'))

    const view = render(<Host attachments={[part()]} html='<img src="cid:logo@mail">' />, { wrapper })
    await waitFor(() => expect(inlined()['logo@mail']).toBeTruthy())

    view.rerender(<Host attachments={[part()]} html='<img src="cid:logo@mail">' />)
    await settle()

    expect(mocks.requestBlob).toHaveBeenCalledTimes(1)
    // Account-scoped like every other key in the module: a second mailbox must not read this one.
    expect(client.getQueryData(['mail', 'primary', 'inline', 'INBOX', 2])).toBeTruthy()
  })

  // Message parts are immutable per folder+uid, and the app refetches on focus by default: a
  // newsletter with fifteen inline images would re-issue fifteen IMAP part fetches per alt-tab.
  it('does not refetch the parts when the window regains focus', async () => {
    mocks.requestBlob.mockResolvedValue(png('x'))

    render(<Host attachments={[part()]} html='<img src="cid:logo@mail">' />, { wrapper })
    await waitFor(() => expect(inlined()['logo@mail']).toBeTruthy())

    await act(async () => { focusManager.setFocused(false); focusManager.setFocused(true) })
    await settle()

    expect(mocks.requestBlob).toHaveBeenCalledTimes(1)
  })
})
