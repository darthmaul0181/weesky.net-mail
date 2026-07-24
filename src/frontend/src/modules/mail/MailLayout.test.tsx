import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, useLocation } from 'react-router-dom'
import type { ReactNode } from 'react'
import MailLayout from './MailLayout'
import type { MailFolderNode } from './api/mailTypes'
import { settle } from '../../test-utils'
import { DRAG_MIME, serializeDrag } from './list/dragMessages'

const mocks = vi.hoisted(() => ({
  getMailFolders: vi.fn(),
  getMailMessages: vi.fn(),
  getMailMessage: vi.fn(),
  getPreferences: vi.fn(),
  moveMessages: vi.fn(),
  deleteMessages: vi.fn(),
  searchMessages: vi.fn(),
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
// The composer owns a router blocker, which needs a data router; this file's harness is a plain
// MemoryRouter, so what is under test here is the layout's compose mode, not the composer.
vi.mock('./compose/ComposeView', () => ({
  default: () => <div data-testid="compose-view">compose</div>,
}))

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
  const location = useLocation()
  return (
    <>
      <span data-testid="search">{location.search}</span>
      <span data-testid="path">{location.pathname}</span>
    </>
  )
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

// The composer lives inside the mail module: the rail and the folder tree stay, only the
// list/reader side is replaced.
describe('compose mode', () => {
  beforeEach(() => vi.clearAllMocks())

  it('replaces the list and the reader with the composer, keeping the folder tree', async () => {
    const { container } = renderAt('/mail/compose')

    expect(await screen.findByTestId('compose-view')).toBeInTheDocument()
    await waitFor(() => expect(document.querySelector('nav[aria-label="Folders"]')).not.toBeNull())
    expect(container.querySelector('.mail-list')).toBeNull()
    expect(screen.queryByText(/select a message/i)).toBeNull()
  })

  // No folder in the URL on /mail/compose: the inbox redirect must not fire there, or it would
  // navigate — the very thing the composer's blocker is there to question.
  it('does not redirect to the inbox while composing', async () => {
    renderAt('/mail/compose')

    await screen.findByTestId('compose-view')
    await settle()
    expect(screen.getByTestId('search')).toHaveTextContent('')
  })

  it('opens the composer from the New message button in the folder column', async () => {
    renderAt('/mail?folder=INBOX')

    fireEvent.click(await screen.findByRole('button', { name: 'New message' }))

    await waitFor(() => expect(screen.getByTestId('path')).toHaveTextContent('/mail/compose'))
    expect(screen.getByTestId('compose-view')).toBeInTheDocument()
  })

  // Clicking a folder is a navigation out of /mail/compose; the composer's blocker owns the
  // "discard?" question, so the layout only has to leave.
  it('leaves the composer for the folder clicked in the tree', async () => {
    renderAt('/mail/compose')

    await screen.findByTestId('compose-view')
    await waitFor(() => expect(document.querySelector('nav[aria-label="Folders"]')).not.toBeNull())
    const tree = document.querySelector('nav[aria-label="Folders"]') as HTMLElement
    fireEvent.click(within(tree).getByRole('button', { name: 'Projects' }))

    await waitFor(() => expect(screen.getByTestId('path')).toHaveTextContent(/^\/mail$/))
    expect(screen.getByTestId('search')).toHaveTextContent('folder=Projects')
    expect(screen.queryByTestId('compose-view')).toBeNull()
  })
})

describe('dropping a dragged message onto a folder', () => {
  beforeEach(() => vi.clearAllMocks())

  const summaries = [
    { uid: 7, subject: 'first', fromName: 'A', fromAddress: 'a@b.c', date: '2026-07-18T09:00:00Z',
      seen: true, flagged: false, answered: false, hasAttachments: false, size: 1, preview: '' },
    { uid: 8, subject: 'second', fromName: 'B', fromAddress: 'b@b.c', date: '2026-07-18T10:00:00Z',
      seen: true, flagged: false, answered: false, hasAttachments: false, size: 1, preview: '' },
  ]

  // Scoped to the tree: the message rows carry their own "Archive" action buttons too.
  const dropOn = (roleName: string, uids: number[]) => {
    const tree = document.querySelector('nav[aria-label="Folders"]') as HTMLElement
    const line = within(tree).getByRole('button', { name: roleName }).closest('.folder-line') as HTMLElement
    fireEvent.drop(line, {
      dataTransfer: {
        types: [DRAG_MIME], dropEffect: 'none',
        getData: () => serializeDrag({ sourcePath: 'INBOX', uids }),
      },
    })
  }

  it('moves the dropped messages through the API', async () => {
    mocks.moveMessages.mockResolvedValue(undefined)
    renderAt('/mail?folder=INBOX', folders, 'right', summaries)

    await screen.findByRole('button', { name: /first/i })
    dropOn('Archive', [7])

    await waitFor(() => expect(mocks.moveMessages).toHaveBeenCalledWith('INBOX', [7], 'Archives'))
  })

  it('advances the reader when the open message is one of them', async () => {
    mocks.moveMessages.mockResolvedValue(undefined)
    // Detail slower than the list: an immediate resolve marks the open row seen and cancels the
    // list fetch in flight, so the rows would never arrive — the same delay the departing suite uses.
    mocks.getMailMessage.mockImplementation(() => new Promise(resolve => setTimeout(() => resolve({
      uid: 7, folderPath: 'INBOX', uidValidity: 1, subject: 'open', fromName: '', fromAddress: 'a@b.c',
      to: [], cc: [], date: '2026-07-18T09:00:00Z', htmlBody: '', textBody: 'x',
      blockedImageCount: 0, attachments: [],
    }), 250)))
    renderAt('/mail?folder=INBOX&uid=7', folders, 'right', summaries)

    await screen.findByRole('button', { name: /first/i })
    dropOn('Archive', [7])

    await waitFor(() => expect(screen.getByTestId('search')).toHaveTextContent('uid=8'))
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

// The search criteria live in the layout, so the reader can open a hit from another folder while
// the URL folder stays put; leaving the folder or clearing a cross-folder result winds it back.
describe('searching from the layout', () => {
  beforeEach(() => vi.clearAllMocks())

  const result = (partial: object) => ({
    uid: 20, folderPath: 'INBOX', uidValidity: 1, subject: 'found', fromName: 'Z', fromAddress: 'z@b.c',
    date: '2026-07-18T09:00:00Z', seen: true, flagged: false, answered: false,
    hasAttachments: false, size: 1, preview: '', ...partial,
  })

  function searchYields(results: object[]) {
    mocks.searchMessages.mockResolvedValue({ total: results.length, page: 0, pageSize: 30, results })
  }

  // A hit from another folder resolves slowly like the departing suite: an instant detail marks the
  // row seen and cancels the search fetch in flight.
  function readerYields(uid: number, folderPath: string) {
    mocks.getMailMessage.mockImplementation(() => new Promise(resolve => setTimeout(() => resolve({
      uid, folderPath, uidValidity: 1, subject: 'opened', fromName: '', fromAddress: 'z@b.c',
      to: [], cc: [], date: '2026-07-18T09:00:00Z', htmlBody: '', textBody: 'x',
      blockedImageCount: 0, attachments: [],
    }), 250)))
  }

  async function runAdvancedAllFolders() {
    fireEvent.click(await screen.findByRole('button', { name: 'Search' }))
    fireEvent.click(screen.getByRole('button', { name: 'Advanced search' }))
    const modal = document.querySelector('.modal') as HTMLElement
    fireEvent.change(within(modal).getByLabelText('Subject'), { target: { value: 'e' } })
    fireEvent.change(within(modal).getByLabelText('Search in'), { target: { value: 'all' } })
    fireEvent.click(within(modal).getByRole('button', { name: 'Search' }))
  }

  // 1) Loupe → « x » → Entrée : la recherche part avec le dossier ouvert et ses résultats s'affichent.
  it('runs a quick search scoped to the open folder and shows its results', async () => {
    searchYields([result({ subject: 'found' })])
    renderAt('/mail?folder=INBOX')

    fireEvent.click(await screen.findByRole('button', { name: 'Search' }))
    const input = screen.getByPlaceholderText('Search in INBOX')
    fireEvent.change(input, { target: { value: 'x' } })
    fireEvent.keyDown(input, { key: 'Enter' })

    expect(await screen.findByText('found')).toBeInTheDocument()
    expect(mocks.searchMessages).toHaveBeenCalledWith(
      { folderPath: 'INBOX', allFolders: false, quick: 'x' }, 0, 30, expect.anything())
  })

  // 2) Résultat d'un autre dossier ouvert : le lecteur lit dans 'Archives', l'URL garde folder=INBOX.
  it('opens a cross-folder result in its own folder, keeping folder=INBOX in the URL', async () => {
    searchYields([result({ folderPath: 'Archives', subject: 'elsewhere' })])
    readerYields(20, 'Archives')
    renderAt('/mail?folder=INBOX')

    await runAdvancedAllFolders()
    fireEvent.click(await screen.findByRole('button', { name: /elsewhere/i }))

    await waitFor(() =>
      expect(mocks.getMailMessage).toHaveBeenCalledWith('Archives', 20, expect.anything()))
    expect(screen.getByTestId('search')).toHaveTextContent('folder=INBOX')
    expect(screen.getByTestId('search')).toHaveTextContent('uid=20')
  })

  // 3) Clear avec un résultat d'un autre dossier ouvert : le lecteur se ferme, retour au dossier.
  it('closes the reader and returns to the folder when a cross-folder result is cleared', async () => {
    searchYields([result({ folderPath: 'Archives', subject: 'elsewhere' })])
    readerYields(20, 'Archives')
    renderAt('/mail?folder=INBOX')

    await runAdvancedAllFolders()
    fireEvent.click(await screen.findByRole('button', { name: /elsewhere/i }))
    await waitFor(() => expect(screen.getByTestId('search')).toHaveTextContent('uid=20'))

    fireEvent.click(screen.getByRole('button', { name: 'Clear' }))

    await waitFor(() => expect(screen.getByTestId('search')).not.toHaveTextContent('uid'))
    expect(screen.getByTestId('search')).toHaveTextContent('folder=INBOX')
  })

  // 4) Changer de dossier dans l'arbre pendant une recherche : la liste redevient le dossier,
  //    la recherche n'est pas rappelée et sa bannière disparaît.
  it('drops the search when the folder changes in the tree', async () => {
    searchYields([result({ subject: 'found' })])
    renderAt('/mail?folder=INBOX')

    fireEvent.click(await screen.findByRole('button', { name: 'Search' }))
    const input = screen.getByPlaceholderText('Search in INBOX')
    fireEvent.change(input, { target: { value: 'x' } })
    fireEvent.keyDown(input, { key: 'Enter' })
    await screen.findByText('found')
    mocks.searchMessages.mockClear()

    const tree = document.querySelector('nav[aria-label="Folders"]') as HTMLElement
    fireEvent.click(within(tree).getByRole('button', { name: 'Projects' }))

    await waitFor(() => expect(screen.getByTestId('search')).toHaveTextContent('folder=Projects'))
    await settle()
    expect(mocks.searchMessages).not.toHaveBeenCalled()
    expect(document.querySelector('.search-results-banner')).toBeNull()
  })
})
