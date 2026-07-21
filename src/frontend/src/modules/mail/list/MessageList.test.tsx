import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import MessageList from './MessageList'

const mocks = vi.hoisted(() => ({
  getMailMessages: vi.fn(), getPreferences: vi.fn(), useMessageList: vi.fn(),
}))

vi.mock('../../../api.js', () => ({ api: mocks }))
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({ activeAccount: { id: 'primary' } }),
}))
// The list is tested against the shape it consumes, not against the network: what the hook
// puts on the wire is useMessageList's own test.
vi.mock('./useMessageList', () => ({ useMessageList: mocks.useMessageList }))

// The DOM lib type has no `instances` static — that's the test double's addition.
const IntersectionObserver = globalThis.IntersectionObserver as unknown as {
  instances: { trigger: (isIntersecting?: boolean) => void; options: { root: Element | null } }[]
}

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

const sample = [
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
]

function pagedState(paging = {}, overrides = {}) {
  return {
    messages: sample,
    total: 2,
    isLoading: false,
    isError: false,
    paging: { page: 0, lastPage: 0, onSelect: vi.fn(), ...paging },
    streaming: null,
    ...overrides,
  }
}

type ListProps = Parameters<typeof MessageList>[0]

function renderList(props: Partial<ListProps> = {}) {
  return render(
    <MessageList folderPath="INBOX" selectedUid={null} onSelect={vi.fn()} {...props} />,
    { wrapper })
}

describe('MessageList', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '50', 'mail.showPreview': 'true' })
    mocks.useMessageList.mockReturnValue(pagedState())
  })

  it('prompts when no folder is selected', () => {
    renderList({ folderPath: null })

    expect(screen.getByText(/select a folder/i)).toBeInTheDocument()
    // Nothing is asked for: the hook is handed the absence of a folder, and answers nothing.
    expect(mocks.useMessageList).toHaveBeenCalledWith(null)
  })

  it('renders sender, subject and preview', async () => {
    renderList()

    expect(await screen.findByText('Merci pour l’envoi')).toBeInTheDocument()
    expect(screen.getByText('Alice Martin')).toBeInTheDocument()
    expect(screen.getByText('Re: facture')).toBeInTheDocument()
  })

  it('names the folder above the list', () => {
    renderList({ folderPath: 'INBOX.Linux server', folderName: 'Linux server' })

    expect(screen.getByRole('heading', { name: 'Linux server' })).toBeInTheDocument()
  })

  it('falls back to the path when the folder name is unknown', () => {
    renderList()

    expect(screen.getByRole('heading', { name: 'INBOX' })).toBeInTheDocument()
  })

  // The heading is how the column says what it is showing; a state that drops it leaves the
  // user looking at an unlabelled panel.
  it('keeps the heading while loading and when the folder is empty', () => {
    mocks.useMessageList.mockReturnValue(
      pagedState({}, { messages: [], total: 0, isLoading: true }))
    const { rerender } = renderList({ folderName: 'INBOX' })

    expect(screen.getByRole('heading', { name: 'INBOX' })).toBeInTheDocument()
    expect(screen.getByText(/loading messages/i)).toBeInTheDocument()

    mocks.useMessageList.mockReturnValue(pagedState({}, { messages: [], total: 0 }))
    rerender(
      <MessageList folderPath="INBOX" folderName="INBOX" selectedUid={null} onSelect={vi.fn()} />)

    expect(screen.getByText(/no messages/i)).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'INBOX' })).toBeInTheDocument()
  })

  // A message with no body used to render no preview element at all, making its row shorter
  // than its neighbours. The element is now always present and CSS reserves its line.
  it('reserves the preview line even when there is nothing to preview', async () => {
    const { container } = renderList()

    await screen.findByText('Alice Martin')

    expect(container.querySelectorAll('.message-row-preview')).toHaveLength(sample.length)
  })

  it('names the attachment marker for assistive technology', () => {
    renderList()

    // Only the message that has one — the marker must not be announced on every row.
    expect(screen.getAllByLabelText(/has attachments/i)).toHaveLength(1)
  })

  it('falls back to the address when there is no display name', () => {
    renderList()

    expect(screen.getByText('bob@x.be')).toBeInTheDocument()
  })

  it('labels a message with no subject', () => {
    renderList()

    expect(screen.getByText('(no subject)')).toBeInTheDocument()
  })

  it('marks unread rows', () => {
    renderList()

    expect(screen.getByText('Alice Martin').closest('button')).toHaveClass('is-unread')
    expect(screen.getByText('bob@x.be').closest('button')).not.toHaveClass('is-unread')
  })

  it('marks the selected row', () => {
    renderList({ selectedUid: 1 })

    expect(screen.getByText('bob@x.be').closest('button')).toHaveClass('is-selected')
  })

  it('calls onSelect with the uid', () => {
    const onSelect = vi.fn()
    renderList({ onSelect })

    fireEvent.click(screen.getByText('Alice Martin'))

    expect(onSelect).toHaveBeenCalledWith(2)
  })

  it('shows an empty state for an empty folder', () => {
    mocks.useMessageList.mockReturnValue(pagedState({}, { messages: [], total: 0 }))
    renderList()

    expect(screen.getByText(/no messages/i)).toBeInTheDocument()
  })

  it('hides the pager when everything fits on one page', () => {
    renderList()

    expect(screen.queryByRole('button', { name: /next page/i })).not.toBeInTheDocument()
  })

  it('pages forward', () => {
    const onSelect = vi.fn()
    mocks.useMessageList.mockReturnValue(pagedState({ lastPage: 2, onSelect }))
    renderList()

    fireEvent.click(screen.getByRole('button', { name: /next page/i }))

    // Zero-based on the wire: the second page is 1.
    expect(onSelect).toHaveBeenCalledWith(1)
  })

  it('offers the pages as numbers', () => {
    mocks.useMessageList.mockReturnValue(pagedState({ lastPage: 2 }))
    renderList()

    // 120 messages over 50 per page is three pages, numbered from one on screen.
    expect(screen.getByRole('button', { name: 'Page 1' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Page 2' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Page 3' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Page 4' })).not.toBeInTheDocument()
  })

  it('jumps straight to a numbered page', () => {
    const onSelect = vi.fn()
    mocks.useMessageList.mockReturnValue(pagedState({ lastPage: 2, onSelect }))
    renderList()

    fireEvent.click(screen.getByRole('button', { name: 'Page 3' }))

    // Zero-based on the wire, one-based on screen.
    expect(onSelect).toHaveBeenCalledWith(2)
  })

  it('marks the page being shown', () => {
    mocks.useMessageList.mockReturnValue(pagedState({ lastPage: 2 }))
    renderList()

    expect(screen.getByRole('button', { name: 'Page 1' })).toHaveAttribute('aria-current', 'page')
    expect(screen.getByRole('button', { name: 'Page 2' })).not.toHaveAttribute('aria-current')
  })

  it('surfaces a load failure', () => {
    mocks.useMessageList.mockReturnValue(pagedState({}, { messages: [], isError: true }))
    renderList()

    expect(screen.getByText(/could not load/i)).toBeInTheDocument()
  })
})

describe('the preferences it obeys', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.useMessageList.mockReturnValue(pagedState())
  })

  it('shows the preview when the preference is on', async () => {
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '30', 'mail.showPreview': 'true' })
    renderList()

    expect(await screen.findByText('Merci pour l’envoi')).toBeInTheDocument()
  })

  it('hides it when the preference is off', async () => {
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '30', 'mail.showPreview': 'false' })
    const { container } = renderList()

    await screen.findByText('Re: facture')
    await vi.waitFor(() =>
      expect(container.querySelectorAll('.message-row-preview')).toHaveLength(0))
    expect(screen.queryByText('Merci pour l’envoi')).not.toBeInTheDocument()
  })
})

function streamingState(overrides = {}, count = 100) {
  return {
    messages: Array.from({ length: count }, (_, i) => ({
      uid: i + 1, subject: `Subject ${i + 1}`, fromName: 'A', fromAddress: 'a@b.c',
      date: '2026-07-21T00:00:00Z', seen: true, flagged: false, answered: false,
      hasAttachments: false, size: 0, preview: '',
    })),
    total: 3812,
    isLoading: false,
    isError: false,
    paging: null,
    streaming: {
      hasMore: true, isLoadingMore: false, loadMoreFailed: false, loadMore: vi.fn(), ...overrides,
    },
  }
}

describe('MessageList streaming', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': 'all', 'mail.showPreview': 'true' })
  })

  it('shows the counter instead of a pager', () => {
    mocks.useMessageList.mockReturnValue(streamingState())
    renderList()

    expect(screen.getByText('100 of 3,812')).toBeInTheDocument()
    expect(screen.queryByRole('navigation', { name: 'Pages' })).not.toBeInTheDocument()
  })

  it('places the sentinel 20 rows before the last', () => {
    mocks.useMessageList.mockReturnValue(streamingState())
    const { container } = renderList()

    const rows = Array.from(container.querySelectorAll('.message-list > li'))
    const carrying = rows.findIndex(row => row.querySelector('.message-list-sentinel'))
    expect(carrying).toBe(80)
  })

  // A second length pins the rule to move with the block count, not to a fixed row.
  it('moves the sentinel as more blocks arrive', () => {
    mocks.useMessageList.mockReturnValue(streamingState({}, 250))
    const { container } = renderList()

    const rows = Array.from(container.querySelectorAll('.message-list > li'))
    const carrying = rows.findIndex(row => row.querySelector('.message-list-sentinel'))
    expect(carrying).toBe(230)
  })

  it('roots the observer at the scrolling band', () => {
    mocks.useMessageList.mockReturnValue(streamingState())
    const { container } = renderList()

    const band = container.querySelector('.mail-list-scroll')
    expect(IntersectionObserver.instances[0].options.root).toBe(band)
  })

  it('asks for the next block when the sentinel comes into view', () => {
    const loadMore = vi.fn()
    mocks.useMessageList.mockReturnValue(streamingState({ loadMore }))
    renderList()

    IntersectionObserver.instances[0].trigger(true)

    expect(loadMore).toHaveBeenCalledTimes(1)
  })

  it('says a block is on its way', () => {
    mocks.useMessageList.mockReturnValue(streamingState({ isLoadingMore: true }))
    renderList()

    expect(screen.getByText('Loading more…')).toBeInTheDocument()
  })

  // The point of the whole error path: three thousand valid rows must not be erased because
  // the three-thousand-and-first did not arrive.
  it('keeps the loaded rows when a block fails, and offers Retry', () => {
    const loadMore = vi.fn()
    mocks.useMessageList.mockReturnValue(
      streamingState({ loadMoreFailed: true, hasMore: true, loadMore }))
    renderList()

    expect(screen.getByText('Subject 1')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
    expect(loadMore).toHaveBeenCalledTimes(1)
  })

  it('drops the sentinel once there is nothing more to load', () => {
    mocks.useMessageList.mockReturnValue(streamingState({ hasMore: false }))
    const { container } = renderList()

    expect(container.querySelector('.message-list-sentinel')).toBeNull()
  })

  it('drops the sentinel and the counter on an empty folder', () => {
    mocks.useMessageList.mockReturnValue({
      messages: [], total: 0, isLoading: false, isError: false, paging: null,
      streaming: { hasMore: false, isLoadingMore: false, loadMoreFailed: false, loadMore: vi.fn() },
    })
    const { container } = renderList()

    expect(screen.getByText('No messages')).toBeInTheDocument()
    expect(container.querySelector('.message-list-sentinel')).toBeNull()
    expect(container.querySelector('.mail-list-count')).toBeNull()
  })

  it('returns to the top when the folder changes', () => {
    mocks.useMessageList.mockReturnValue(streamingState())
    const { container, rerender } = renderList()

    const band = container.querySelector('.mail-list-scroll') as HTMLDivElement
    band.scrollTop = 900
    rerender(<MessageList folderPath="Archive" selectedUid={null} onSelect={vi.fn()} />)

    expect(band.scrollTop).toBe(0)
  })
})
