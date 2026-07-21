import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, useLocation } from 'react-router-dom'
import type { ReactNode } from 'react'
import MailLayout from './MailLayout'
import type { MailFolderNode } from './api/mailTypes'

const mocks = vi.hoisted(() => ({
  getMailFolders: vi.fn(),
  getMailMessages: vi.fn(),
  getMailMessage: vi.fn(),
}))

vi.mock('../../api.js', () => ({
  api: mocks,
  requestBlob: vi.fn(),
  mailAttachmentUrl: vi.fn(),
}))
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
  node({ path: 'Projects', name: 'Projects' }),
]

function Where() {
  return <span data-testid="search">{useLocation().search}</span>
}

function renderAt(initial: string, tree: MailFolderNode[] = folders) {
  mocks.getMailFolders.mockResolvedValue(tree)
  mocks.getMailMessages.mockResolvedValue({ messages: [], total: 0 })

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

  // A mailbox whose inbox the chain did not resolve must not be redirected into nowhere.
  it('picks nothing when no folder holds the inbox role', async () => {
    renderAt('/mail', [node({ path: 'Projects', name: 'Projects' })])

    await waitFor(() => expect(mocks.getMailFolders).toHaveBeenCalled())
    expect(screen.getByTestId('search')).toHaveTextContent('')
    expect(screen.getByText(/select a folder/i)).toBeInTheDocument()
  })
})
