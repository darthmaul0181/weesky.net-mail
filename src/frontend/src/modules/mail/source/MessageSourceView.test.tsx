import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import MessageSourceView from './MessageSourceView'

const useMessageSource = vi.fn()
vi.mock('../queries', () => ({ useMessageSource: (...a: unknown[]) => useMessageSource(...a) }))

const payload = {
  subject: 'Mount ZFS on rescue system',
  messageId: 'c24494a9de@weesky.be',
  date: '2026-02-02T01:01:00Z',
  fromName: 'Michaël',
  fromAddress: 'darth@weesky.be',
  to: [{ name: '', address: 'darthmaul0181@gmail.com' }],
  authentication: { spf: 'pass', dkim: 'pass', dmarc: 'pass', raw: 'mx.google.com; spf=pass' },
  source: 'Delivered-To: darthmaul0181@gmail.com\r\nSubject: Mount ZFS\r\n',
  totalBytes: 2048,
  truncated: false,
}

function renderAt(search: string) {
  return render(
    <MemoryRouter initialEntries={[`/mail/source${search}`]}>
      <MessageSourceView />
    </MemoryRouter>,
  )
}

describe('MessageSourceView', () => {
  beforeEach(() => {
    // jsdom's document is shared across the file: without this an earlier render's title would
    // still be standing when the title assertion runs.
    document.title = ''
    useMessageSource.mockReset()
    useMessageSource.mockReturnValue({ data: payload, isLoading: false, error: null, refetch: vi.fn() })
  })

  it('shows the synthesis and the raw source', () => {
    renderAt('?folder=INBOX&uid=42')

    expect(screen.getByText('c24494a9de@weesky.be')).toBeInTheDocument()
    expect(screen.getByText('darthmaul0181@gmail.com')).toBeInTheDocument()
    expect(screen.getByText('pass · pass · pass')).toBeInTheDocument()
    expect(screen.getByText(/Delivered-To: darthmaul0181@gmail\.com/)).toBeInTheDocument()
  })

  it('omits a row whose datum is missing', () => {
    useMessageSource.mockReturnValue({
      data: { ...payload, messageId: null, authentication: null },
      isLoading: false, error: null, refetch: vi.fn(),
    })
    renderAt('?folder=INBOX&uid=42')

    expect(screen.queryByText('Message ID')).not.toBeInTheDocument()
    expect(screen.queryByText('SPF / DKIM / DMARC')).not.toBeInTheDocument()
  })

  it('omits the verdict row when the header reported no verdict at all', () => {
    useMessageSource.mockReturnValue({
      data: { ...payload, authentication: { spf: null, dkim: null, dmarc: null, raw: 'mx.google.com' } },
      isLoading: false, error: null, refetch: vi.fn(),
    })
    renderAt('?folder=INBOX&uid=42')

    expect(screen.queryByText('SPF / DKIM / DMARC')).not.toBeInTheDocument()
    expect(screen.queryByText('— · — · —')).not.toBeInTheDocument()
  })

  it('dashes only the verdicts the header left out', () => {
    useMessageSource.mockReturnValue({
      data: { ...payload, authentication: { spf: 'pass', dkim: null, dmarc: null, raw: 'mx.google.com' } },
      isLoading: false, error: null, refetch: vi.fn(),
    })
    renderAt('?folder=INBOX&uid=42')

    expect(screen.getByText('pass · — · —')).toBeInTheDocument()
  })

  it('says what it is not showing when the source is truncated', () => {
    useMessageSource.mockReturnValue({
      data: { ...payload, truncated: true, totalBytes: 25_480_000 },
      isLoading: false, error: null, refetch: vi.fn(),
    })
    renderAt('?folder=INBOX&uid=42')

    expect(screen.getByText('— truncated at 1 MB of 24.3 MB —')).toBeInTheDocument()
  })

  it('carries no truncation marker on a whole source', () => {
    renderAt('?folder=INBOX&uid=42')

    expect(screen.queryByText(/truncated at/)).not.toBeInTheDocument()
  })

  it('titles the tab with the subject', () => {
    renderAt('?folder=INBOX&uid=42')

    expect(document.title).toBe('Mount ZFS on rescue system — source')
  })

  it('offers a retry when the read fails', () => {
    useMessageSource.mockReturnValue({
      data: undefined, isLoading: false, error: new Error('nope'), refetch: vi.fn(),
    })
    renderAt('?folder=INBOX&uid=42')

    expect(screen.getByText('Could not load the message source')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })

  it('reads the message again when the retry is pressed', () => {
    const refetch = vi.fn()
    useMessageSource.mockReturnValue({
      data: undefined, isLoading: false, error: new Error('nope'), refetch,
    })
    renderAt('?folder=INBOX&uid=42')

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))

    expect(refetch).toHaveBeenCalledTimes(1)
  })

  it('says the retry is under way while it is', () => {
    // `error` survives the refetch it triggers, so the label is the only thing that can show
    // the click landed.
    useMessageSource.mockReturnValue({
      data: undefined, isLoading: false, isFetching: true, error: new Error('nope'), refetch: vi.fn(),
    })
    renderAt('?folder=INBOX&uid=42')

    expect(screen.getByRole('button', { name: 'Retrying…' })).toBeDisabled()
  })

  it('refuses a URL naming no message, without requesting anything', () => {
    renderAt('?folder=INBOX')

    expect(screen.getByText('Could not load the message source')).toBeInTheDocument()
    expect(useMessageSource).toHaveBeenCalledWith(null, null)
  })

  it.each([
    ['a uid that is not a number', '?folder=INBOX&uid=abc'],
    ['uid 0, which no IMAP message can carry', '?folder=INBOX&uid=0'],
  ])('requests nothing for %s', (_label, search) => {
    renderAt(search)

    expect(screen.getByText('Could not load the message source')).toBeInTheDocument()
    expect(useMessageSource).toHaveBeenCalledWith(null, null)
  })

  it('offers no retry on a URL naming no message', () => {
    renderAt('?folder=INBOX')

    // The query never ran, so refetch() would do nothing: a button that cannot act is worse
    // than no button.
    expect(screen.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument()
  })
})
