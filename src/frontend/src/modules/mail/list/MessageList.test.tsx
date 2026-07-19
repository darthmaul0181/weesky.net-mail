import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import MessageList from './MessageList'

const mocks = vi.hoisted(() => ({ getMailMessages: vi.fn() }))

vi.mock('../../../api.js', () => ({ api: { getMailMessages: mocks.getMailMessages } }))
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({ activeAccount: { id: 'primary' } }),
}))

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

const page = {
  folderPath: 'INBOX', uidValidity: 1, total: 2, page: 0, pageSize: 50,
  messages: [
    {
      uid: 2, subject: 'Re: facture', fromName: 'Alice Martin', fromAddress: 'alice@x.be',
      date: '2026-07-18T09:00:00Z', seen: false, flagged: false, answered: false,
      hasAttachments: true, size: 100, preview: 'Merci pour l’envoi',
    },
    {
      uid: 1, subject: '', fromName: '', fromAddress: 'bob@x.be',
      date: '2026-07-17T09:00:00Z', seen: true, flagged: false, answered: false,
      hasAttachments: false, size: 90, preview: '',
    },
  ],
}

describe('MessageList', () => {
  beforeEach(() => vi.clearAllMocks())

  it('prompts when no folder is selected', () => {
    render(<MessageList folderPath={null} selectedUid={null} onSelect={vi.fn()} />, { wrapper })

    expect(screen.getByText(/select a folder/i)).toBeInTheDocument()
    expect(mocks.getMailMessages).not.toHaveBeenCalled()
  })

  it('renders sender, subject and preview', async () => {
    mocks.getMailMessages.mockResolvedValue(page)

    render(<MessageList folderPath="INBOX" selectedUid={null} onSelect={vi.fn()} />, { wrapper })

    expect(await screen.findByText('Alice Martin')).toBeInTheDocument()
    expect(screen.getByText('Re: facture')).toBeInTheDocument()
    expect(screen.getByText('Merci pour l’envoi')).toBeInTheDocument()
  })

  it('names the folder above the list', async () => {
    mocks.getMailMessages.mockResolvedValue(page)

    render(
      <MessageList folderPath="INBOX.Linux server" folderName="Linux server"
        selectedUid={null} onSelect={vi.fn()} />,
      { wrapper })

    expect(await screen.findByRole('heading', { name: 'Linux server' })).toBeInTheDocument()
  })

  it('falls back to the path when the folder name is unknown', async () => {
    mocks.getMailMessages.mockResolvedValue(page)

    render(<MessageList folderPath="INBOX" selectedUid={null} onSelect={vi.fn()} />, { wrapper })

    expect(await screen.findByRole('heading', { name: 'INBOX' })).toBeInTheDocument()
  })

  // The heading is how the column says what it is showing; a state that drops it leaves the
  // user looking at an unlabelled panel.
  it('keeps the heading while loading and when the folder is empty', async () => {
    mocks.getMailMessages.mockResolvedValue({ ...page, total: 0, messages: [] })

    render(
      <MessageList folderPath="INBOX" folderName="INBOX" selectedUid={null} onSelect={vi.fn()} />,
      { wrapper })

    expect(screen.getByRole('heading', { name: 'INBOX' })).toBeInTheDocument()
    expect(await screen.findByText(/no messages/i)).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'INBOX' })).toBeInTheDocument()
  })

  // A message with no body used to render no preview element at all, making its row shorter
  // than its neighbours. The element is now always present and CSS reserves its line.
  it('reserves the preview line even when there is nothing to preview', async () => {
    mocks.getMailMessages.mockResolvedValue(page)
    const { container } = render(
      <MessageList folderPath="INBOX" selectedUid={null} onSelect={vi.fn()} />, { wrapper })

    await screen.findByText('Alice Martin')

    expect(container.querySelectorAll('.message-row-preview')).toHaveLength(page.messages.length)
  })

  it('names the attachment marker for assistive technology', async () => {
    mocks.getMailMessages.mockResolvedValue(page)

    render(<MessageList folderPath="INBOX" selectedUid={null} onSelect={vi.fn()} />, { wrapper })
    await screen.findByText('Alice Martin')

    // Only the message that has one — the marker must not be announced on every row.
    expect(screen.getAllByLabelText(/has attachments/i)).toHaveLength(1)
  })

  it('falls back to the address when there is no display name', async () => {
    mocks.getMailMessages.mockResolvedValue(page)

    render(<MessageList folderPath="INBOX" selectedUid={null} onSelect={vi.fn()} />, { wrapper })

    expect(await screen.findByText('bob@x.be')).toBeInTheDocument()
  })

  it('labels a message with no subject', async () => {
    mocks.getMailMessages.mockResolvedValue(page)

    render(<MessageList folderPath="INBOX" selectedUid={null} onSelect={vi.fn()} />, { wrapper })

    expect(await screen.findByText('(no subject)')).toBeInTheDocument()
  })

  it('marks unread rows', async () => {
    mocks.getMailMessages.mockResolvedValue(page)

    render(<MessageList folderPath="INBOX" selectedUid={null} onSelect={vi.fn()} />, { wrapper })

    expect((await screen.findByText('Alice Martin')).closest('button')).toHaveClass('is-unread')
    expect(screen.getByText('bob@x.be').closest('button')).not.toHaveClass('is-unread')
  })

  it('marks the selected row', async () => {
    mocks.getMailMessages.mockResolvedValue(page)

    render(<MessageList folderPath="INBOX" selectedUid={1} onSelect={vi.fn()} />, { wrapper })

    expect((await screen.findByText('bob@x.be')).closest('button')).toHaveClass('is-selected')
  })

  it('calls onSelect with the uid', async () => {
    mocks.getMailMessages.mockResolvedValue(page)
    const onSelect = vi.fn()

    render(<MessageList folderPath="INBOX" selectedUid={null} onSelect={onSelect} />, { wrapper })

    fireEvent.click(await screen.findByText('Alice Martin'))

    expect(onSelect).toHaveBeenCalledWith(2)
  })

  it('shows an empty state for an empty folder', async () => {
    mocks.getMailMessages.mockResolvedValue({ ...page, total: 0, messages: [] })

    render(<MessageList folderPath="INBOX" selectedUid={null} onSelect={vi.fn()} />, { wrapper })

    expect(await screen.findByText(/no messages/i)).toBeInTheDocument()
  })

  it('hides the pager when everything fits on one page', async () => {
    mocks.getMailMessages.mockResolvedValue(page)

    render(<MessageList folderPath="INBOX" selectedUid={null} onSelect={vi.fn()} />, { wrapper })
    await screen.findByText('Alice Martin')

    expect(screen.queryByRole('button', { name: /next page/i })).not.toBeInTheDocument()
  })

  it('pages forward', async () => {
    mocks.getMailMessages.mockResolvedValue({ ...page, total: 120 })

    render(<MessageList folderPath="INBOX" selectedUid={null} onSelect={vi.fn()} />, { wrapper })

    fireEvent.click(await screen.findByRole('button', { name: /next page/i }))

    await waitFor(() =>
      expect(mocks.getMailMessages).toHaveBeenCalledWith('INBOX', 1, 50, expect.anything()))
  })

  it('resets to the first page when the folder changes', async () => {
    mocks.getMailMessages.mockResolvedValue({ ...page, total: 120 })

    const { rerender } = render(
      <MessageList folderPath="INBOX" selectedUid={null} onSelect={vi.fn()} />, { wrapper })

    fireEvent.click(await screen.findByRole('button', { name: /next page/i }))
    await waitFor(() => expect(mocks.getMailMessages).toHaveBeenCalledWith('INBOX', 1, 50, expect.anything()))

    rerender(<MessageList folderPath="Sent" selectedUid={null} onSelect={vi.fn()} />)

    await waitFor(() =>
      expect(mocks.getMailMessages).toHaveBeenCalledWith('Sent', 0, 50, expect.anything()))
  })

  it('surfaces a load failure', async () => {
    mocks.getMailMessages.mockRejectedValue(new Error('boom'))

    render(<MessageList folderPath="INBOX" selectedUid={null} onSelect={vi.fn()} />, { wrapper })

    expect(await screen.findByText(/could not load/i)).toBeInTheDocument()
  })
})
