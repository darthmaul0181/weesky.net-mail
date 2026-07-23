import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, useLocation } from 'react-router-dom'
import type { ReactNode } from 'react'
import MailLayout from './MailLayout'
import type { MailFolderNode } from './api/mailTypes'
import { settle } from '../../test-utils'

const mocks = vi.hoisted(() => ({
  getMailFolders: vi.fn(),
  getMailMessages: vi.fn(),
  getMailMessage: vi.fn(),
  getPreferences: vi.fn(),
  moveMessages: vi.fn(),
  deleteMessages: vi.fn(),
  useListRefresh: vi.fn(),
}))

vi.mock('../../api.js', () => ({
  api: mocks,
  requestBlob: vi.fn(),
  mailAttachmentUrl: vi.fn(),
}))
vi.mock('./list/useListRefresh', () => ({ useListRefresh: mocks.useListRefresh }))
vi.mock('../../contexts/AuthContext', () => ({
  useAuth: () => ({ activeAccount: { id: 'primary' } }),
}))
vi.mock('../../contexts/ThemeContext', () => ({ useTheme: () => ({ isDark: false }) }))

function node(partial: Partial<MailFolderNode>): MailFolderNode {
  return {
    path: 'X', name: 'X', specialUse: null, selectable: true, subscribed: true,
    total: 0, unread: 0, uidValidity: 1, uidNext: null, highestModSeq: null, children: [], ...partial,
  }
}

const folders = [
  node({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox' }),
  node({ path: 'Archives', name: 'Archives', specialUse: 'archive' }),
  node({ path: 'Corbeille', name: 'Corbeille', specialUse: 'trash' }),
  node({ path: 'Projects', name: 'Projects' }),
]

function Where() {
  return <span data-testid="search">{useLocation().search}</span>
}

function renderAt(
  initial: string, tree: MailFolderNode[] = folders, pane = 'right', messages: object[] = [],
) {
  mocks.getMailFolders.mockResolvedValue(tree)
  mocks.getMailMessages.mockResolvedValue({
    folderPath: 'INBOX', uidValidity: 1, total: messages.length, page: 0, pageSize: 30, messages,
  })
  mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '30', 'mail.readingPane': pane })

  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[initial]}>{children}<Where /></MemoryRouter>
    </QueryClientProvider>
  )
  return render(<MailLayout />, { wrapper })
}

describe('MailLayout', () => {
  beforeEach(() => vi.clearAllMocks())

  // Landing on an empty three-column view asks the user to pick the one folder everybody
  // starts in. The inbox is chosen from the resolution chain's own role, not by matching the
  // name "INBOX", so a server that names it otherwise still lands right.
  it('opens the inbox when the URL names no folder', async () => {
    renderAt('/mail')

    await waitFor(() =>
      expect(screen.getByTestId('search')).toHaveTextContent('folder=INBOX'))
  })

  // Which message to read is the user's call — the reader stays empty until they make it.
  it('opens no message', async () => {
    renderAt('/mail')

    await waitFor(() =>
      expect(screen.getByTestId('search')).toHaveTextContent('folder=INBOX'))
    expect(screen.getByTestId('search')).not.toHaveTextContent('uid')
    expect(screen.getByText(/select a message/i)).toBeInTheDocument()
    expect(mocks.getMailMessage).not.toHaveBeenCalled()
  })

  it('leaves a folder the URL already names alone', async () => {
    renderAt('/mail?folder=Projects')

    await waitFor(() => expect(mocks.getMailFolders).toHaveBeenCalled())
    expect(screen.getByTestId('search')).toHaveTextContent('folder=Projects')
  })

  it('leaves a message the URL already names alone', async () => {
    mocks.getMailMessage.mockResolvedValue({
      uid: 7, folderPath: 'INBOX', uidValidity: 1, subject: 'kept', fromName: '', fromAddress: 'a@b.c',
      to: [], cc: [], date: '2026-07-18T09:00:00Z', htmlBody: '<p>x</p>', textBody: 'x',
      blockedImageCount: 0, attachments: [],
    })
    renderAt('/mail?folder=INBOX&uid=7')

    expect(await screen.findByText('kept')).toBeInTheDocument()
  })

  it('watches the displayed folder for remote changes', async () => {
    renderAt('/mail')

    await waitFor(() => expect(mocks.useListRefresh).toHaveBeenCalledWith('INBOX'))
  })

  // A mailbox whose inbox the chain did not resolve must not be redirected into nowhere.
  it('picks nothing when no folder holds the inbox role', async () => {
    renderAt('/mail', [node({ path: 'Projects', name: 'Projects' })])

    await waitFor(() => expect(mocks.getMailFolders).toHaveBeenCalled())
    expect(screen.getByTestId('search')).toHaveTextContent('')
    expect(screen.getByText(/select a folder/i)).toBeInTheDocument()
  })
})

// The row leaves the folder optimistically, so the selection cannot wait for a refetch.
describe('a message departing the folder', () => {
  beforeEach(() => vi.clearAllMocks())

  const summaries = [
    { uid: 7, subject: 'first', fromName: 'A', fromAddress: 'a@b.c', date: '2026-07-18T09:00:00Z',
      seen: true, flagged: false, answered: false, hasAttachments: false, size: 1, preview: '' },
    { uid: 8, subject: 'second', fromName: 'B', fromAddress: 'b@b.c', date: '2026-07-18T10:00:00Z',
      seen: true, flagged: false, answered: false, hasAttachments: false, size: 1, preview: '' },
  ]

  // The detail lands after the list: a reader opening on a not-yet-cached summary marks it
  // seen, and that mutation cancels the list fetch in flight — the rows would never arrive.
  // The gap is generous on purpose: it stands in for "detail slower than list", and a thin
  // margin turned flaky once the heading band grew from an <h2> to the selection toolbar, whose
  // extra synchronous render pushed the list commit past a 20ms detail.
  function openFolder(rows: typeof summaries, uid: number) {
    mocks.getMailMessage.mockImplementation(() => new Promise(resolve => setTimeout(() => resolve({
      uid, folderPath: 'INBOX', uidValidity: 1, subject: 'open', fromName: '', fromAddress: 'a@b.c',
      to: [], cc: [], date: '2026-07-18T09:00:00Z', htmlBody: '', textBody: 'x',
      blockedImageCount: 0, attachments: [],
    }), 250)))
    return renderAt(`/mail?folder=INBOX&uid=${uid}`, folders, 'right', rows)
  }

  it('selects the next row when the open message is archived', async () => {
    openFolder(summaries, 7)

    const row = await screen.findByRole('button', { name: /first/i })
    fireEvent.click(within(row).getByRole('button', { name: 'Archive' }))

    await waitFor(() => expect(screen.getByTestId('search')).toHaveTextContent('uid=8'))
  })

  it('closes the reader when the last remaining message departs', async () => {
    openFolder([summaries[0]], 7)

    const row = await screen.findByRole('button', { name: /first/i })
    fireEvent.click(within(row).getByRole('button', { name: 'Archive' }))

    await waitFor(() => expect(screen.getByTestId('search')).not.toHaveTextContent('uid'))
  })

  it('leaves the selection alone when another row departs', async () => {
    openFolder(summaries, 8)

    const row = await screen.findByRole('button', { name: /first/i })
    fireEvent.click(within(row).getByRole('button', { name: 'Archive' }))

    await settle()
    expect(screen.getByTestId('search')).toHaveTextContent('uid=8')
  })

  // A bulk action departs the whole selection: the reader must skip every member it removed, not
  // step onto a sibling the same action dropped from the cache. Open 7, Select all → [7, 8],
  // Archive: both are gone, so the reader closes rather than landing on the ghost uid=8.
  it('advances past the whole departing batch, never onto a member the action removed', async () => {
    openFolder(summaries, 7)

    await screen.findByRole('button', { name: /first/i })
    fireEvent.click(screen.getByRole('checkbox', { name: 'Select all' }))
    const toolbar = document.querySelector('.selection-toolbar') as HTMLElement
    fireEvent.click(within(toolbar).getByRole('button', { name: 'Archive' }))

    await waitFor(() => {
      const search = screen.getByTestId('search').textContent || ''
      expect(search).not.toContain('uid=7')
      expect(search).not.toContain('uid=8')
    })
  })
})

describe('reading pane arrangements', () => {
  beforeEach(() => vi.clearAllMocks())

  // The folder tree draws its own <hr> (role separator) between blocks, so the pane splitter is
  // reached by its accessible name rather than by role alone.
  it('renders the side-by-side split with a vertical splitter', async () => {
    const { container } = renderAt('/mail?folder=INBOX')

    const splitter = await screen.findByRole('separator', { name: 'Resize the panes' })
    expect(splitter).toHaveAttribute('aria-orientation', 'vertical')
    expect(container.querySelector('.mail-layout.is-right')).not.toBeNull()
  })

  // The splitter's drag ceiling is measured against its own parent, which must exclude the
  // 240px folders column — otherwise the right mode's drag ceiling overshoots by that width.
  it('wraps the right arrangement in its own row, excluding the folders column', async () => {
    const { container } = renderAt('/mail?folder=INBOX')

    await screen.findByRole('separator', { name: 'Resize the panes' })
    expect(container.querySelector('.mail-row [role="separator"]')).not.toBeNull()
  })

  it('renders the stacked split with a horizontal splitter', async () => {
    const { container } = renderAt('/mail?folder=INBOX', folders, 'bottom')

    const splitter = await screen.findByRole('separator', { name: 'Resize the panes' })
    expect(splitter).toHaveAttribute('aria-orientation', 'horizontal')
    expect(container.querySelector('.mail-stack')).not.toBeNull()
  })

  // No message open: the list has the space and there is nothing to split.
  it('renders no reader and no splitter in the no-split mode without a message', async () => {
    const { container } = renderAt('/mail?folder=INBOX', folders, 'none')

    await waitFor(() => expect(container.querySelector('.mail-layout.is-none')).not.toBeNull())
    expect(screen.queryByRole('separator', { name: 'Resize the panes' })).toBeNull()
    expect(screen.queryByText(/select a message/i)).toBeNull()
    expect(container.querySelector('.mail-list.is-hidden')).toBeNull()
  })

  // The list is hidden, never unmounted: unmounting would lose the scroll position and, in
  // streaming mode, the loaded blocks.
  it('hides the list behind the reader in the no-split mode', async () => {
    mocks.getMailMessage.mockResolvedValue({
      uid: 7, folderPath: 'INBOX', uidValidity: 1, subject: 'open me', fromName: '', fromAddress: 'a@b.c',
      to: [], cc: [], date: '2026-07-18T09:00:00Z', htmlBody: '<p>x</p>', textBody: 'x',
      blockedImageCount: 0, attachments: [],
    })
    const { container } = renderAt('/mail?folder=INBOX&uid=7', folders, 'none')

    await screen.findByText('open me')
    expect(container.querySelector('.mail-list.is-hidden')).not.toBeNull()
  })

  it('drops the uid when the back button is used', async () => {
    mocks.getMailMessage.mockResolvedValue({
      uid: 7, folderPath: 'INBOX', uidValidity: 1, subject: 'open me', fromName: '', fromAddress: 'a@b.c',
      to: [], cc: [], date: '2026-07-18T09:00:00Z', htmlBody: '<p>x</p>', textBody: 'x',
      blockedImageCount: 0, attachments: [],
    })
    renderAt('/mail?folder=INBOX&uid=7', folders, 'none')

    fireEvent.click(await screen.findByRole('button', { name: 'Back to the message list' }))

    await waitFor(() => expect(screen.getByTestId('search')).not.toHaveTextContent('uid'))
    expect(screen.getByTestId('search')).toHaveTextContent('folder=INBOX')
  })
})
