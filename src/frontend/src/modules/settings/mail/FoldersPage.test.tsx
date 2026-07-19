import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import FoldersPage from './FoldersPage'
import type { MailFolderNode } from '../../mail/api/mailTypes'

const mocks = vi.hoisted(() => ({
  getMailFolders: vi.fn(),
  getFolderRoles: vi.fn(),
  createMailFolder: vi.fn(),
  renameMailFolder: vi.fn(),
  deleteMailFolder: vi.fn(),
  setMailFolderSubscription: vi.fn(),
  setFolderRole: vi.fn(),
  clearFolderRole: vi.fn(),
}))

vi.mock('../../../api.js', () => ({ api: mocks }))
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({ activeAccount: { id: 'primary' } }),
}))

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

function node(partial: Partial<MailFolderNode>): MailFolderNode {
  return {
    path: 'X', name: 'X', specialUse: null, selectable: true, subscribed: true,
    total: 0, unread: 0, uidValidity: 1, children: [], ...partial,
  }
}

const folders = [
  node({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox' }),
  node({ path: 'Projects', name: 'Projects' }),
]

const roles = [
  { role: 'sent', folderPath: null, provenance: null, staleOverride: null },
  { role: 'drafts', folderPath: null, provenance: null, staleOverride: null },
  { role: 'trash', folderPath: null, provenance: null, staleOverride: null },
  { role: 'junk', folderPath: null, provenance: null, staleOverride: null },
  { role: 'archive', folderPath: null, provenance: null, staleOverride: null },
]

function renderPage() {
  mocks.getMailFolders.mockResolvedValue(folders)
  mocks.getFolderRoles.mockResolvedValue(roles)
  return render(<FoldersPage />, { wrapper })
}

describe('FoldersPage', () => {
  beforeEach(() => vi.clearAllMocks())

  it('lists the folders once loaded', async () => {
    renderPage()

    expect(await screen.findByLabelText('Show Projects')).toBeInTheDocument()
    expect(screen.getByLabelText('Show INBOX')).toBeInTheDocument()
  })

  // The two dialogs act across the whole set, so they belong above the list rather than on a row.
  it('offers both whole-set actions', async () => {
    renderPage()
    await screen.findByLabelText('Show Projects')

    expect(screen.getByRole('button', { name: 'New folder' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'System folders' })).toBeInTheDocument()
  })

  // Both shipped as a bare `.btn`, which carries no border and no background, so they rendered
  // as plain text and read as headings rather than controls. jsdom applies no stylesheet, so
  // the variant class is the only part of that this can hold on to.
  it.each(['New folder', 'System folders'])('gives %s a visible button style', async name => {
    renderPage()
    await screen.findByLabelText('Show Projects')

    expect(screen.getByRole('button', { name })).toHaveClass('btn-ghost')
  })

  it('opens the create dialog', async () => {
    renderPage()
    await screen.findByLabelText('Show Projects')

    fireEvent.click(screen.getByRole('button', { name: 'New folder' }))

    expect(screen.getByRole('button', { name: 'Create folder' })).toBeInTheDocument()
  })

  it('opens the system-folders dialog', async () => {
    renderPage()
    await screen.findByLabelText('Show Projects')

    fireEvent.click(screen.getByRole('button', { name: 'System folders' }))

    // The role selects are what distinguishes it from the create dialog.
    expect(await screen.findByLabelText('Trash')).toBeInTheDocument()
  })

  it('closes the system-folders dialog again', async () => {
    renderPage()
    await screen.findByLabelText('Show Projects')
    fireEvent.click(screen.getByRole('button', { name: 'System folders' }))
    await screen.findByLabelText('Trash')

    fireEvent.click(screen.getByRole('button', { name: 'Close' }))

    expect(screen.queryByLabelText('Trash')).not.toBeInTheDocument()
  })

  it('reports a load failure instead of an empty list', async () => {
    mocks.getMailFolders.mockRejectedValue(new Error('nope'))
    mocks.getFolderRoles.mockResolvedValue(roles)
    render(<FoldersPage />, { wrapper })

    expect(await screen.findByText('Could not load the folders.')).toBeInTheDocument()
  })
})
