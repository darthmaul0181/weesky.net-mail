import { describe, it, expect, vi, afterEach, beforeEach } from 'vitest'
import { render, screen, waitFor, fireEvent, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, useLocation, useNavigate } from 'react-router'
import type { ReactNode } from 'react'
import MailLayout from './MailLayout'
import { ApiError } from '../../api.js'
import type { MailFolderNode } from './api/mailTypes'
import { createTestQueryClient, mockViewport, resetViewport, settle } from '../../test-utils'
import { DRAG_MIME, serializeDrag } from './list/dragMessages'

const mocks = vi.hoisted(() => ({
  getMailFolders: vi.fn(),
  getMailMessages: vi.fn(),
  getMailMessage: vi.fn(),
  getPreferences: vi.fn(),
  moveMessages: vi.fn(),
  deleteMessages: vi.fn(),
  searchMessages: vi.fn(),
  setMessageFlags: vi.fn(),
  useListRefresh: vi.fn(),
  openDraft: vi.fn(),
  getIdentities: vi.fn(),
  emptyFolder: vi.fn(),
}))

// Actual first: ApiError is a real class the layout tests with `instanceof`, so a stub of it
// would prove nothing about the 409 the backend actually raises.
vi.mock('../../api.js', async () => ({
  ...await vi.importActual<object>('../../api.js'),
  api: mocks,
  requestBlob: vi.fn(),
  mailAttachmentUrl: vi.fn(),
}))
vi.mock('./list/useListRefresh', () => ({ useListRefresh: mocks.useListRefresh }))

// Mutable so a test can switch accounts the way the account menu does.
const auth = vi.hoisted(() => ({
  activeAccountId: 'primary', accountsLoading: false, authMode: 'Password',
}))

vi.mock('../../contexts/AuthContext', () => ({
  PRIMARY_ACCOUNT_ID: 'primary',
  useAuth: () => ({
    activeAccount: { id: auth.activeAccountId, authMode: auth.authMode },
    activeAccountId: auth.activeAccountId,
    accountsLoading: auth.accountsLoading,
  }),
}))

vi.mock('../../contexts/ThemeContext', () => ({ useTheme: () => ({ isDark: false }) }))
// The composer owns a router blocker, which needs a data router; this file's harness is a plain
// MemoryRouter, so what is under test here is the layout's compose mode, not the composer.
vi.mock('./compose/ComposeView', () => ({
  default: () => <div data-testid="compose-view">compose</div>,
}))

beforeEach(() => {
  auth.activeAccountId = 'primary'
  auth.accountsLoading = false
  auth.authMode = 'Password'
})

/* A mail row is a `role="row"` of four gridcells and its name lives on the content cell, so a
   control of the row — the star, an action — is found on the row rather than inside that cell. */
const rowOf = (cell: HTMLElement) => cell.closest('.message-row') as HTMLElement

function node(partial: Partial<MailFolderNode>): MailFolderNode {
  return {
    path: 'X', name: 'X', selectable: true, subscribed: true,
    total: 0, unread: 0, uidValidity: 1, children: [], ...partial,
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
  const navigate = useNavigate()
  return (
    <>
      <span data-testid="search">{location.search}</span>
      <span data-testid="path">{location.pathname}</span>
      {/* Echoes the navigation state so a test can check what a `navigate(..., { state })`
          call actually carried, not just that some navigation happened. */}
      <span data-testid="state">{JSON.stringify(location.state ?? null)}</span>
      {/* Stands in for the composer's own exit, which goes back to the folder it was opened
          from. ComposeView is mocked here, so the layout has no ✕ of its own to click. */}
      <button data-testid="leave-compose" onClick={() => void navigate('/mail?folder=Projects')}>
        leave
      </button>
      <button data-testid="go-back" onClick={() => { void navigate(-1) }}>back</button>
    </>
  )
}

function renderAt(
  // An Error stands for a tree the server refused to answer with.
  initial: string, tree: MailFolderNode[] | Error = folders, pane = 'right', messages: object[] = [],
  // A promise a test resolves itself is what lets it act while the identities are still in flight.
  identities: object | Promise<object> = { identities: [] },
) {
  if (tree instanceof Error) mocks.getMailFolders.mockRejectedValue(tree)
  else mocks.getMailFolders.mockResolvedValue(tree)
  mocks.getMailMessages.mockResolvedValue({
    folderPath: 'INBOX', uidValidity: 1, total: messages.length, page: 0, pageSize: 30, messages,
  })
  mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '30', 'mail.readingPane': pane })
  mocks.getIdentities.mockReturnValue(Promise.resolve(identities))

  const client = createTestQueryClient()
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[initial]}>{children}<Where /></MemoryRouter>
    </QueryClientProvider>
  )
  return render(<MailLayout />, { wrapper })
}

// Every mockViewport call leaks its matchMedia stub into the rest of the file otherwise.
afterEach(resetViewport)


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

    await waitFor(() => expect(mocks.useListRefresh).toHaveBeenCalledWith('INBOX', true))
  })

  // The manual face of the poll: the click forces the same folders refetch the tick drives.
  it('refetches the folder tree from the refresh button', async () => {
    renderAt('/mail')

    await waitFor(() =>
      expect(screen.getByTestId('search')).toHaveTextContent('folder=INBOX'))
    // The initial load spins the button; a click during the tail rotation is a no-op by
    // design, so wait out the release before clicking.
    await waitFor(() =>
      expect(screen.getByLabelText('Refresh').firstElementChild).not.toHaveClass('is-spinning'),
      { timeout: 2000 })
    const before = mocks.getMailFolders.mock.calls.length
    fireEvent.click(screen.getByLabelText('Refresh'))
    await waitFor(() =>
      expect(mocks.getMailFolders.mock.calls.length).toBeGreaterThan(before))
  })

  // A mailbox whose inbox the chain did not resolve must not be redirected into nowhere.
  it('picks nothing when no folder holds the inbox role', async () => {
    renderAt('/mail', [node({ path: 'Projects', name: 'Projects' })])

    await waitFor(() => expect(mocks.getMailFolders).toHaveBeenCalled())
    expect(screen.getByTestId('search')).toHaveTextContent('')
    expect(screen.getByText(/select a folder/i)).toBeInTheDocument()
  })
})

// A folder and a uid belong to the mailbox they were read in: carried into the next one they
// point at nothing. The URL winds back to /mail and the inbox resolution takes it from there.
describe('following an account switch', () => {
  beforeEach(() => vi.clearAllMocks())

  it('drops the previous mailbox\'s folder and message when the account changes', async () => {
    const { rerender } = renderAt('/mail?folder=Projects&uid=7')

    await waitFor(() =>
      expect(screen.getByTestId('search')).toHaveTextContent('folder=Projects'))

    auth.activeAccountId = 'linked-1'
    rerender(<MailLayout />)

    await waitFor(() =>
      expect(screen.getByTestId('search')).toHaveTextContent('folder=INBOX'))
    expect(screen.getByTestId('search')).not.toHaveTextContent('uid=7')
  })

  // A reset on mount would throw away a deep link, which is a real entry path.
  it('leaves a deep link alone on mount', async () => {
    renderAt('/mail?folder=Projects&uid=7')

    await waitFor(() => expect(mocks.getMailFolders).toHaveBeenCalled())
    await settle()
    expect(screen.getByTestId('search')).toHaveTextContent('folder=Projects')
    expect(screen.getByTestId('search')).toHaveTextContent('uid=7')
  })

  // The composer owns every exit from itself: its leave guard turns a navigation into the
  // save-or-discard question, and a reset fired into that guard can be refused and then never
  // retried — leaving the URL on A while the module has moved to B.
  it('holds the reset while composing and fires it when the composer exits on its own', async () => {
    const { rerender } = renderAt('/mail/compose')
    await screen.findByTestId('compose-view')

    auth.activeAccountId = 'linked-1'
    rerender(<MailLayout />)
    await settle()
    expect(screen.getByTestId('path')).toHaveTextContent('/mail/compose')

    // The composer goes back to the folder it was opened from — the previous account's.
    fireEvent.click(screen.getByTestId('leave-compose'))

    await waitFor(() => expect(screen.getByTestId('search')).toHaveTextContent('folder=INBOX'))
  })

  // The same exit URL, reached by a click in the *new* account's tree: that is the user naming a
  // folder this mailbox has, so the held reset has nothing left to drop and must not override it.
  it('keeps a folder picked in the new tree on the way out of the composer', async () => {
    const { rerender } = renderAt('/mail/compose')
    await screen.findByTestId('compose-view')

    auth.activeAccountId = 'linked-1'
    rerender(<MailLayout />)
    await settle()

    const tree = document.querySelector('nav[aria-label="Folders"]') as HTMLElement
    fireEvent.click(within(tree).getByRole('button', { name: 'Projects' }))

    await waitFor(() =>
      expect(screen.getByTestId('search')).toHaveTextContent('folder=Projects'))
    await settle()
    expect(screen.getByTestId('search')).toHaveTextContent('folder=Projects')
  })

  // The recorded pick discriminates one transition and no other. Left standing it would spare a
  // later switch the reset it needs: same folder, same account, and the *other* mailbox's uid
  // rides back in. No composer anywhere in this — the ref needs none.
  it('drops the uid coming back to an account whose folder was picked before', async () => {
    const row = {
      uid: 42, subject: 'over there', fromName: 'A', fromAddress: 'a@b.c',
      date: '2026-07-18T09:00:00Z', seen: true, flagged: false, answered: false,
      hasAttachments: false, size: 1, preview: '',
    }
    mocks.getMailMessage.mockResolvedValue({
      uid: 42, folderPath: 'INBOX', uidValidity: 1, subject: 'over there', fromName: '',
      fromAddress: 'a@b.c', to: [], cc: [], date: '2026-07-18T09:00:00Z', htmlBody: '',
      textBody: 'x', blockedImageCount: 0, attachments: [],
    })
    const { rerender } = renderAt('/mail', folders, 'right', [row])
    await waitFor(() =>
      expect(screen.getByTestId('search')).toHaveTextContent('folder=INBOX'))

    // A deliberate pick under the first account.
    const tree = document.querySelector('nav[aria-label="Folders"]') as HTMLElement
    fireEvent.click(within(tree).getByRole('button', { name: 'Inbox' }))

    auth.activeAccountId = 'linked-1'
    rerender(<MailLayout />)
    await waitFor(() =>
      expect(screen.getByTestId('search')).toHaveTextContent('folder=INBOX'))

    // A message of the second mailbox, whose uid means nothing in the first.
    fireEvent.click(await screen.findByRole('gridcell', { name: /over there/i }))
    await waitFor(() => expect(screen.getByTestId('search')).toHaveTextContent('uid=42'))

    auth.activeAccountId = 'primary'
    rerender(<MailLayout />)

    await waitFor(() => expect(screen.getByTestId('search')).not.toHaveTextContent('uid'))
  })

  // A reload persisted onto a connected account: the stored id may still turn out to be stale,
  // so nothing is fetched and no mailbox is drawn until the account list has settled.
  it('holds the mailbox while the account list is still loading', async () => {
    auth.activeAccountId = 'linked-1'
    auth.accountsLoading = true
    renderAt('/mail')

    await settle()
    expect(mocks.getMailFolders).not.toHaveBeenCalled()
    expect(document.querySelector('nav[aria-label="Folders"]')).toBeNull()
    expect(screen.getByRole('status', { name: 'Loading' })).toBeInTheDocument()
  })

  // The overwhelmingly common case: the primary can never be stale, so it never waits.
  it('does not hold the primary account for the same list', async () => {
    auth.accountsLoading = true
    renderAt('/mail')

    await waitFor(() => expect(mocks.getMailFolders).toHaveBeenCalled())
    expect(screen.queryByRole('status', { name: 'Loading' })).toBeNull()
  })
})

// The stored password of a connected account no longer decrypts. Every mail request would fail,
// so the three columns are replaced by the one thing that can be done about it.
describe('an account whose stored password no longer decrypts', () => {
  beforeEach(() => vi.clearAllMocks())

  const refused = () =>
    new ApiError('connected_credentials_invalid', 409, 'connected_credentials_invalid')

  it('replaces the three columns with the password prompt', async () => {
    renderAt('/mail?folder=INBOX', refused())

    expect(await screen.findByText('Password needed')).toBeInTheDocument()
    expect(screen.getByText(
      "Your main password changed, so this account's password must be entered again.",
    )).toBeInTheDocument()
    expect(document.querySelector('nav[aria-label="Folders"]')).toBeNull()
    expect(screen.queryByText(/select a message/i)).toBeNull()
  })

  it('offers the way to enter it again', async () => {
    renderAt('/mail?folder=INBOX', refused())

    const link = await screen.findByRole('link', { name: 'Enter the password' })
    expect(link).toHaveAttribute('href', '/settings/accounts')
    expect(link).toHaveClass('btn-primary')
  })

  // The pane replaces the subtree by re-render, which no leave guard can see: a background poll
  // answering 409 while a draft is open would discard it without ever asking.
  it('does not take the composer out from under an open draft', async () => {
    renderAt('/mail/compose', refused())

    expect(await screen.findByTestId('compose-view')).toBeInTheDocument()
    await settle()
    expect(screen.queryByText('Password needed')).toBeNull()
  })

  // The same 409 reaches a provider mailbox when the consent is withdrawn or its cipher stops
  // opening. There is no password anywhere in that story, so every word of the prompt would be
  // false and its instruction impossible to follow.
  it('asks a provider mailbox for a fresh sign-in, not for a password', async () => {
    auth.authMode = 'OAuth2'
    renderAt('/mail?folder=INBOX', refused())

    expect(await screen.findByText('Sign-in needed')).toBeInTheDocument()
    expect(screen.getByText(
      'Weesky can no longer sign in to this mailbox, so you need to grant it access again.',
    )).toBeInTheDocument()
    expect(screen.queryByText('Password needed')).toBeNull()

    const link = screen.getByRole('link', { name: 'Reconnect this account' })
    expect(link).toHaveAttribute('href', '/settings/accounts')
  })

  // Not a catch-all: an ordinary failure is a folder tree that did not load, not a broken account.
  it('keeps the columns for an ordinary folder failure', async () => {
    renderAt('/mail?folder=INBOX', new Error('boom'))

    expect(await screen.findByText('Could not load folders.')).toBeInTheDocument()
    expect(screen.queryByText('Password needed')).toBeNull()
  })
})

// A deep link races the listing against the detail, and the detail can win: the reader marks a
// not-yet-cached message seen, and that mutation used to cancel the list fetch in flight —
// which TanStack reverts to pending with no data and never retries. See cancelLoaded.
describe('a message detail arriving before the folder listing', () => {
  beforeEach(() => vi.clearAllMocks())

  const unread = {
    uid: 7, subject: 'first', fromName: 'A', fromAddress: 'a@b.c', date: '2026-07-18T09:00:00Z',
    seen: false, flagged: false, answered: false, hasAttachments: false, size: 1, preview: '',
  }

  it('still lands the rows', async () => {
    mocks.setMessageFlags.mockResolvedValue(undefined)
    mocks.getMailMessage.mockResolvedValue({
      uid: 7, folderPath: 'INBOX', uidValidity: 1, subject: 'open', fromName: '', fromAddress: 'a@b.c',
      to: [], cc: [], date: '2026-07-18T09:00:00Z', htmlBody: '', textBody: 'x',
      blockedImageCount: 0, attachments: [],
    })
    renderAt('/mail?folder=INBOX&uid=7', folders, 'right', [unread])
    mocks.getMailMessages.mockImplementation(() => new Promise(resolve => setTimeout(() => resolve({
      folderPath: 'INBOX', uidValidity: 1, total: 1, page: 0, pageSize: 30, messages: [unread],
    }), 60)))

    const row = rowOf(await screen.findByRole('gridcell', { name: /first/i }))
    // The race is only armed if the mark-seen mutation actually fired against the pending list.
    expect(mocks.setMessageFlags).toHaveBeenCalledWith('INBOX', [7], 'seen', true, { accountId: 'primary' })
    // And the row the listing brought back unread is reconciled to what the STORE already did.
    expect(await within(row).findByRole('button', { name: 'Mark as unread' })).toBeInTheDocument()
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

  function openFolder(rows: typeof summaries, uid: number) {
    mocks.getMailMessage.mockResolvedValue({
      uid, folderPath: 'INBOX', uidValidity: 1, subject: 'open', fromName: '', fromAddress: 'a@b.c',
      to: [], cc: [], date: '2026-07-18T09:00:00Z', htmlBody: '', textBody: 'x',
      blockedImageCount: 0, attachments: [],
    })
    return renderAt(`/mail?folder=INBOX&uid=${uid}`, folders, 'right', rows)
  }

  it('selects the next row when the open message is archived', async () => {
    openFolder(summaries, 7)

    const row = rowOf(await screen.findByRole('gridcell', { name: /first/i }))
    fireEvent.click(within(row).getByRole('button', { name: 'Archive' }))

    await waitFor(() => expect(screen.getByTestId('search')).toHaveTextContent('uid=8'))
  })

  it('closes the reader when the last remaining message departs', async () => {
    openFolder([summaries[0]!], 7)

    const row = rowOf(await screen.findByRole('gridcell', { name: /first/i }))
    fireEvent.click(within(row).getByRole('button', { name: 'Archive' }))

    await waitFor(() => expect(screen.getByTestId('search')).not.toHaveTextContent('uid'))
  })

  it('leaves the selection alone when another row departs', async () => {
    openFolder(summaries, 8)

    const row = rowOf(await screen.findByRole('gridcell', { name: /first/i }))
    fireEvent.click(within(row).getByRole('button', { name: 'Archive' }))

    await settle()
    expect(screen.getByTestId('search')).toHaveTextContent('uid=8')
  })

  // The URL does not change for this departure (uid 7 is not the open uid 8), so it must not
  // push a history entry either — one Back should leave the mail view entirely.
  it('pushes no history entry when the departing row is not the one open', async () => {
    mocks.getMailMessage.mockResolvedValue({
      uid: 8, folderPath: 'INBOX', uidValidity: 1, subject: 'open', fromName: '', fromAddress: 'a@b.c',
      to: [], cc: [], date: '2026-07-18T09:00:00Z', htmlBody: '', textBody: 'x',
      blockedImageCount: 0, attachments: [],
    })
    mocks.getMailFolders.mockResolvedValue(folders)
    mocks.getMailMessages.mockResolvedValue({
      folderPath: 'INBOX', uidValidity: 1, total: summaries.length, page: 0, pageSize: 30, messages: summaries,
    })
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '30', 'mail.readingPane': 'right' })
    mocks.getIdentities.mockReturnValue(Promise.resolve({ identities: [] }))

    const client = createTestQueryClient()
    render(<MailLayout />, {
      wrapper: ({ children }) => (
        <QueryClientProvider client={client}>
          <MemoryRouter initialEntries={['/mail?folder=Projects', '/mail?folder=INBOX&uid=8']} initialIndex={1}>
            {children}<Where />
          </MemoryRouter>
        </QueryClientProvider>
      ),
    })

    const row = rowOf(await screen.findByRole('gridcell', { name: /first/i }))
    fireEvent.click(within(row).getByRole('button', { name: 'Archive' }))
    await settle()

    fireEvent.click(screen.getByTestId('go-back'))
    await waitFor(() => expect(screen.getByTestId('search')).toHaveTextContent('folder=Projects'))
  })

  // A bulk action departs the whole selection: the reader must skip every member it removed, not
  // step onto a sibling the same action dropped from the cache. Open 7, Select all → [7, 8],
  // Archive: both are gone, so the reader closes rather than landing on the ghost uid=8.
  it('advances past the whole departing batch, never onto a member the action removed', async () => {
    openFolder(summaries, 7)

    await screen.findByRole('gridcell', { name: /first/i })
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

  // Scoped to the column: the floating action carries the same label and is only hidden by CSS,
  // which jsdom does not apply.
  it('opens the composer from the New message button in the folder column', async () => {
    const { container } = renderAt('/mail?folder=INBOX')

    const column = container.querySelector('.mail-folders') as HTMLElement
    fireEvent.click(await within(column).findByRole('button', { name: 'New message' }))

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

    await screen.findByRole('gridcell', { name: /first/i })
    dropOn('Archive', [7])

    await waitFor(() => expect(mocks.moveMessages).toHaveBeenCalledWith('INBOX', [7], 'Archives', { accountId: 'primary' }))
  })

  it('advances the reader when the open message is one of them', async () => {
    mocks.moveMessages.mockResolvedValue(undefined)
    mocks.getMailMessage.mockResolvedValue({
      uid: 7, folderPath: 'INBOX', uidValidity: 1, subject: 'open', fromName: '', fromAddress: 'a@b.c',
      to: [], cc: [], date: '2026-07-18T09:00:00Z', htmlBody: '', textBody: 'x',
      blockedImageCount: 0, attachments: [],
    })
    renderAt('/mail?folder=INBOX&uid=7', folders, 'right', summaries)

    await screen.findByRole('gridcell', { name: /first/i })
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

// The confirm hands focus back through the layout's own regions, and in `none` neither of them is
// always on screen: the list is `display: none` while a message is open, and the reader column
// leaves with the last message.
describe('focus after the reader expunges a message', () => {
  beforeEach(() => vi.clearAllMocks())

  const trash = [
    { uid: 7, subject: 'first', fromName: 'A', fromAddress: 'a@b.c', date: '2026-07-18T09:00:00Z',
      seen: true, flagged: false, answered: false, hasAttachments: false, size: 1, preview: '' },
    { uid: 8, subject: 'second', fromName: 'B', fromAddress: 'b@b.c', date: '2026-07-18T10:00:00Z',
      seen: true, flagged: false, answered: false, hasAttachments: false, size: 1, preview: '' },
  ]

  function openTrash(rows: typeof trash, pane: string) {
    mocks.getMailMessage.mockResolvedValue({
      uid: 7, folderPath: 'Corbeille', uidValidity: 1, subject: 'open', fromName: '',
      fromAddress: 'a@b.c', to: [], cc: [], date: '2026-07-18T09:00:00Z', htmlBody: '',
      textBody: 'x', blockedImageCount: 0, attachments: [],
    })
    mocks.deleteMessages.mockResolvedValue({})
    return renderAt('/mail?folder=Corbeille&uid=7', folders, pane, rows)
  }

  /** The reader's own Delete, not a row's — every row in the trash carries that name too. Clicked
      rather than fired, so the buttons really hold the focus a browser gives them. */
  async function expunge(container: HTMLElement) {
    const reader = within(container.querySelector('.mail-reader') as HTMLElement)
    await userEvent.click(reader.getByRole('button', { name: 'Delete permanently' }))
    const confirm = within(document.querySelector('.modal') as HTMLElement)
    await userEvent.click(confirm.getByRole('button', { name: 'Delete' }))
  }

  it('hands it to the reader column when the expunge has a successor to open', async () => {
    const { container } = openTrash(trash, 'none')
    await screen.findByText('open')

    await expunge(container)

    await waitFor(() => expect(screen.getByTestId('search')).toHaveTextContent('uid=8'))
    expect(container.querySelector('.mail-reader')).toHaveFocus()
  })

  // The same delete with nothing left to open: the reader column goes and the list comes back.
  it('hands it to the list column when the reader closes with the last message', async () => {
    const { container } = openTrash([trash[0]!], 'none')
    await screen.findByText('open')

    await expunge(container)

    await waitFor(() => expect(screen.getByTestId('search')).not.toHaveTextContent('uid'))
    expect(container.querySelector('.mail-list')).toHaveFocus()
  })

  // The fallback only rescues a focus the closing reader took with it: one resting elsewhere stays.
  it('leaves the focus alone when the reader closes without holding it', async () => {
    const { container } = openTrash([trash[0]!], 'none')
    await screen.findByText('open')
    const elsewhere = screen.getByTestId('leave-compose')
    elsewhere.focus()

    const tree = document.querySelector('nav[aria-label="Folders"]') as HTMLElement
    fireEvent.drop(within(tree).getByRole('button', { name: 'Archive' }).closest('.folder-line') as HTMLElement, {
      dataTransfer: {
        types: [DRAG_MIME], dropEffect: 'none',
        getData: () => serializeDrag({ sourcePath: 'Corbeille', uids: [7] }),
      },
    })

    await waitFor(() => expect(screen.getByTestId('search')).not.toHaveTextContent('uid'))
    expect(container.querySelector('.mail-reader')).toBeNull()
    expect(elsewhere).toHaveFocus()
  })

  // The phone tier draws the reader's actions as `.actionbar` across the foot of the screen, so the
  // opener is a different element on a branch where the list column is `display: none`: the reader
  // column standing across the expunge is what the hand-back has to find. Its being hidden is CSS,
  // which jsdom does not compute — that half is the manual recette's.
  it('hands it to the reader column from the phone action bar', async () => {
    mockViewport('phone')
    const { container } = openTrash(trash, 'none')
    await screen.findByText('open')
    expect(container.querySelector('.actionbar')).not.toBeNull()

    await expunge(container)

    await waitFor(() => expect(screen.getByTestId('search')).toHaveTextContent('uid=8'))
    expect(container.querySelector('.mail-reader')).toHaveFocus()
  })

  // The split arrangements keep the list on screen throughout, and it is the real column that has
  // to carry the ref — the component tests hand their own region in.
  it('hands it to the list column beside the reader in the split arrangement', async () => {
    const { container } = openTrash([trash[0]!], 'right')
    await screen.findByText('open')

    await expunge(container)

    await waitFor(() => expect(screen.getByTestId('search')).not.toHaveTextContent('uid'))
    expect(container.querySelector('.mail-list')).toHaveFocus()
  })
})

// The list's own two confirms take their opener with them as well: the row's cluster leaves with
// the row, and the banner leaves with the last message in the folder.
describe('focus after the list expunges', () => {
  beforeEach(() => vi.clearAllMocks())

  const row = (uid: number, subject: string) => ({
    uid, subject, fromName: 'A', fromAddress: 'a@b.c', date: '2026-07-18T09:00:00Z',
    seen: true, flagged: false, answered: false, hasAttachments: false, size: 1, preview: '',
  })

  /** One folder behind the listing and the writes, so a row really leaves and stays gone — a
      fixed mock would hand it back on the invalidation's refetch. */
  function serveTrash(rows: ReturnType<typeof row>[]) {
    let live = rows
    mocks.getMailFolders.mockResolvedValue(folders)
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '30', 'mail.readingPane': 'right' })
    mocks.getIdentities.mockResolvedValue({ identities: [] })
    mocks.getMailMessages.mockImplementation(async () => ({
      folderPath: 'Corbeille', uidValidity: 1, total: live.length, page: 0, pageSize: 30,
      messages: live,
    }))
    mocks.deleteMessages.mockImplementation(async (_folder: string, uids: number[]) => {
      live = live.filter(one => !uids.includes(one.uid))
      return {}
    })
    mocks.emptyFolder.mockImplementation(async () => { live = []; return {} })
    const client = createTestQueryClient()
    return render(<MailLayout />, {
      wrapper: ({ children }: { children: ReactNode }) => (
        <QueryClientProvider client={client}>
          <MemoryRouter initialEntries={['/mail?folder=Corbeille']}>{children}</MemoryRouter>
        </QueryClientProvider>
      ),
    })
  }

  it('hands focus to the list column when the row leaves with its own delete', async () => {
    const { container } = serveTrash([row(7, 'first'), row(8, 'second')])
    const first = await screen.findByText('first')
    const cluster = within(first.closest('.message-row') as HTMLElement)

    await userEvent.click(cluster.getByRole('button', { name: 'Delete permanently' }))
    const confirm = within(document.querySelector('.modal') as HTMLElement)
    await userEvent.click(confirm.getByRole('button', { name: 'Delete' }))

    // The row is drawn for the length of its exit, so the opener is still on screen — disabled,
    // which is what sends the hand-back to the column instead.
    expect(container.querySelector('.message-row-slot.is-leaving')).not.toBeNull()
    expect(container.querySelector('.mail-list')).toHaveFocus()
  })

  it('hands focus to the list column when emptying the folder takes the banner', async () => {
    const { container } = serveTrash([row(7, 'first')])
    await screen.findByText('first')

    await userEvent.click(screen.getByRole('button', { name: 'Empty trash now' }))
    const confirm = within(document.querySelector('.modal') as HTMLElement)
    await userEvent.click(confirm.getByRole('button', { name: 'Delete' }))

    await waitFor(() =>
      expect(screen.queryByRole('button', { name: 'Empty trash now' })).toBeNull())
    expect(container.querySelector('.mail-list')).toHaveFocus()
  })
})

// A drafts-role folder opens straight into the composer instead of the reading pane: the row is
// the account's own unsent text, not a message worth reading in place.
describe('opening a draft from the drafts folder', () => {
  beforeEach(() => vi.clearAllMocks())

  const draftFolders = [
    node({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox' }),
    node({ path: 'Drafts', name: 'Drafts', specialUse: 'drafts' }),
  ]

  const draftRow = {
    uid: 9, subject: 'unsent', fromName: '', fromAddress: 'me@x.be',
    to: [{ name: 'Bob', address: 'bob@x.example' }], date: '2026-07-20T09:00:00Z',
    seen: true, flagged: false, answered: false, hasAttachments: false, size: 1, preview: '',
  }

  it('opens the composer instead of the reader when a draft row is clicked', async () => {
    mocks.openDraft.mockResolvedValue({
      to: ['bob@x.example'], cc: [], bcc: [], subject: 'unsent',
      htmlBody: '<p>hi</p>', attachments: [], references: [],
    })
    renderAt('/mail?folder=Drafts', draftFolders, 'right', [draftRow])

    fireEvent.click(await screen.findByRole('gridcell', { name: /unsent/i }))

    await waitFor(() => expect(screen.getByTestId('path')).toHaveTextContent('/mail/compose'))
    expect(mocks.openDraft).toHaveBeenCalledWith('Drafts', 9, { accountId: 'primary' })
    expect(screen.getByTestId('search')).not.toHaveTextContent('uid')
    expect(mocks.getMailMessage).not.toHaveBeenCalled()

    // The navigation is only right if the seed it carries actually came from this draft: proves
    // the openDraft → buildDraftSeed → navigate chain, not just that some navigation happened.
    const state: unknown = JSON.parse(screen.getByTestId('state').textContent || 'null')
    expect(state).toHaveProperty('from', 'Drafts')
    expect(state).toHaveProperty('seed.action', 'draft')
    expect(state).toHaveProperty('seed.draftRef', { folderPath: 'Drafts', uid: 9 })
    expect(state).toHaveProperty('seed.to', ['bob@x.example'])
  })

  // An unresolved identities query is not "no identities": seeding from [] rewrites the draft's
  // From to the account default, so a resumed draft would go out from an address nobody picked.
  it('waits for the identities before it seeds the draft From', async () => {
    let release!: (value: object) => void
    const identities = new Promise<object>(resolve => { release = resolve })
    mocks.openDraft.mockResolvedValue({
      to: ['bob@x.example'], cc: [], bcc: [], subject: 'unsent', fromAddress: 'michel@weesky.be',
      htmlBody: '<p>hi</p>', attachments: [], references: [],
    })
    renderAt('/mail?folder=Drafts', draftFolders, 'right', [draftRow], identities)

    fireEvent.click(await screen.findByRole('gridcell', { name: /unsent/i }))
    await waitFor(() => expect(mocks.openDraft).toHaveBeenCalled())
    release({ identities: [{
      address: 'michel@weesky.be', displayName: 'Michel', isDefault: false, isPrimary: false,
      stale: false, labelIsCustom: false,
    }] })
    await settle()

    await waitFor(() => expect(screen.getByTestId('path')).toHaveTextContent('/mail/compose'))
    const state: unknown = JSON.parse(screen.getByTestId('state').textContent || 'null')
    expect(state).toHaveProperty('seed.fromAddress', 'michel@weesky.be')
  })

  // Two Open calls stage the draft's parts twice; the losing set is left to the TTL sweeper.
  it('ignores a second click while the draft is being opened', async () => {
    mocks.openDraft.mockReturnValue(new Promise(() => {}))
    renderAt('/mail?folder=Drafts', draftFolders, 'right', [draftRow])

    const row = await screen.findByRole('gridcell', { name: /unsent/i })
    fireEvent.click(row)
    await waitFor(() => expect(mocks.openDraft).toHaveBeenCalledTimes(1))
    fireEvent.click(row)
    await settle()

    expect(mocks.openDraft).toHaveBeenCalledTimes(1)
  })

  // Server prose never reaches the toast; the local fallback does — see apiErrorMessage.
  it('toasts instead of navigating when the draft cannot be opened', async () => {
    mocks.openDraft.mockRejectedValue(new Error('Draft is gone'))
    renderAt('/mail?folder=Drafts', draftFolders, 'right', [draftRow])

    fireEvent.click(await screen.findByRole('gridcell', { name: /unsent/i }))

    expect(await screen.findByText('Could not open the draft')).toBeInTheDocument()
    expect(screen.getByTestId('path')).toHaveTextContent(/^\/mail$/)
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

  function readerYields(uid: number, folderPath: string) {
    mocks.getMailMessage.mockResolvedValue({
      uid, folderPath, uidValidity: 1, subject: 'opened', fromName: '', fromAddress: 'z@b.c',
      to: [], cc: [], date: '2026-07-18T09:00:00Z', htmlBody: '', textBody: 'x',
      blockedImageCount: 0, attachments: [],
    })
  }

  async function runAdvancedAllFolders() {
    fireEvent.click(await screen.findByRole('button', { name: 'Search' }))
    fireEvent.click(screen.getByRole('button', { name: 'Advanced search' }))
    const modal = document.querySelector('.modal') as HTMLElement
    fireEvent.change(within(modal).getByLabelText('Subject'), { target: { value: 'e' } })
    fireEvent.change(within(modal).getByLabelText('Search in'), { target: { value: 'all' } })
    fireEvent.click(within(modal).getByRole('button', { name: 'Search' }))
  }

  // 1) Loupe → "x" → Enter: the search runs scoped to the open folder and its results show.
  it('runs a quick search scoped to the open folder and shows its results', async () => {
    searchYields([result({ subject: 'found' })])
    renderAt('/mail?folder=INBOX')

    fireEvent.click(await screen.findByRole('button', { name: 'Search' }))
    // findBy: the placeholder settles on the role label once the folder tree resolves.
    const input = await screen.findByPlaceholderText('Search in Inbox')
    fireEvent.change(input, { target: { value: 'x' } })
    fireEvent.keyDown(input, { key: 'Enter' })

    expect(await screen.findByText('found')).toBeInTheDocument()
    expect(mocks.searchMessages).toHaveBeenCalledWith(
      { folderPath: 'INBOX', allFolders: false, quick: 'x' }, 0, 30, expect.anything())
  })

  // 2) A result from another folder is opened: the reader reads from 'Archives', the URL keeps
  //    folder=INBOX.
  it('opens a cross-folder result in its own folder, keeping folder=INBOX in the URL', async () => {
    searchYields([result({ folderPath: 'Archives', subject: 'elsewhere' })])
    readerYields(20, 'Archives')
    renderAt('/mail?folder=INBOX')

    await runAdvancedAllFolders()
    fireEvent.click(await screen.findByRole('gridcell', { name: /elsewhere/i }))

    await waitFor(() =>
      expect(mocks.getMailMessage).toHaveBeenCalledWith('Archives', 20, expect.anything()))
    expect(screen.getByTestId('search')).toHaveTextContent('folder=INBOX')
    expect(screen.getByTestId('search')).toHaveTextContent('uid=20')
  })

  // 3) Clear with a result from another folder open: the reader closes, back to the folder.
  it('closes the reader and returns to the folder when a cross-folder result is cleared', async () => {
    searchYields([result({ folderPath: 'Archives', subject: 'elsewhere' })])
    readerYields(20, 'Archives')
    renderAt('/mail?folder=INBOX')

    await runAdvancedAllFolders()
    fireEvent.click(await screen.findByRole('gridcell', { name: /elsewhere/i }))
    await waitFor(() => expect(screen.getByTestId('search')).toHaveTextContent('uid=20'))

    fireEvent.click(screen.getByRole('button', { name: 'Clear' }))

    await waitFor(() => expect(screen.getByTestId('search')).not.toHaveTextContent('uid'))
    expect(screen.getByTestId('search')).toHaveTextContent('folder=INBOX')
  })

  // 3b) Back to a hit after Clear: the search is gone, so the entry's folder no longer applies. The
  //     reader reads the URL folder, and a delete walks the list it actually shows.
  it('ignores a cross-folder entry once its search is cleared', async () => {
    searchYields([result({ folderPath: 'Archives', subject: 'elsewhere' })])
    readerYields(20, 'Archives')
    const inbox = [20, 21].map(uid => result({ uid, subject: `inbox ${uid}` }))
    renderAt('/mail?folder=INBOX', folders, 'right', inbox)

    await runAdvancedAllFolders()
    fireEvent.click(await screen.findByRole('gridcell', { name: /elsewhere/i }))
    await waitFor(() => expect(screen.getByTestId('search')).toHaveTextContent('uid=20'))
    fireEvent.click(screen.getByRole('button', { name: 'Clear' }))
    await waitFor(() => expect(screen.getByTestId('search')).not.toHaveTextContent('uid'))

    fireEvent.click(screen.getByTestId('go-back'))
    await waitFor(() => expect(screen.getByTestId('search')).toHaveTextContent('uid=20'))
    await waitFor(() => expect(mocks.getMailMessage).toHaveBeenLastCalledWith('INBOX', 20, expect.anything()))
    const reader = within(document.querySelector('.mail-reader') as HTMLElement)
    fireEvent.click(await reader.findByRole('button', { name: 'Delete' }))

    await waitFor(() => expect(screen.getByTestId('search')).toHaveTextContent('uid=21'))
    await waitFor(() => expect(mocks.getMailMessage).toHaveBeenLastCalledWith('INBOX', 21, expect.anything()))
    expect(mocks.getMailMessage).not.toHaveBeenCalledWith('Archives', 21, expect.anything())
  })

  // 3c) The last hit deleted closes the reader: the entry left behind shows no message, so Clear
  //     has nothing to wind back and must not push a duplicate of it — one Back reaches the hit.
  it('pushes no entry when Clear follows a cross-folder hit deleted to an empty reader', async () => {
    searchYields([result({ folderPath: 'Archives', subject: 'elsewhere' })])
    readerYields(20, 'Archives')
    renderAt('/mail?folder=INBOX')

    await runAdvancedAllFolders()
    fireEvent.click(await screen.findByRole('gridcell', { name: /elsewhere/i }))
    await waitFor(() => expect(screen.getByTestId('search')).toHaveTextContent('uid=20'))
    const reader = within(document.querySelector('.mail-reader') as HTMLElement)
    fireEvent.click(await reader.findByRole('button', { name: 'Delete' }))
    await waitFor(() => expect(screen.getByTestId('search')).not.toHaveTextContent('uid'))
    fireEvent.click(screen.getByRole('button', { name: 'Clear' }))
    await settle()

    fireEvent.click(screen.getByTestId('go-back'))
    await waitFor(() => expect(screen.getByTestId('search')).toHaveTextContent('uid=20'))
  })

  // 4) Changing folder in the tree during a search: the list goes back to the folder, the search
  //    is not recalled and its banner disappears.
  it('drops the search when the folder changes in the tree', async () => {
    searchYields([result({ subject: 'found' })])
    renderAt('/mail?folder=INBOX')

    fireEvent.click(await screen.findByRole('button', { name: 'Search' }))
    const input = await screen.findByPlaceholderText('Search in Inbox')
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

// A phone gets one pane at a time, so `effectivePane` forces the `none` arrangement whatever the
// account stored — and that arrangement holds no second pane, which is why no splitter is drawn.
// The folder column moves behind the drawer instead of taking 240px of a 360px screen.
describe('MailLayout on a phone', () => {
  beforeEach(() => vi.clearAllMocks())

  it('renders no splitter', async () => {
    mockViewport('phone')
    const { container } = renderAt('/mail?folder=INBOX')
    await settle()
    expect(container.querySelector('.pane-splitter')).toBeNull()
  })

  it('puts the folder column in a drawer', async () => {
    mockViewport('phone')
    const { container } = renderAt('/mail?folder=INBOX')
    await settle()
    expect(container.querySelector('.context-drawer .mail-folders')).toBeTruthy()
  })

  it('leaves the folder column inline on a desktop', async () => {
    mockViewport('desktop')
    const { container } = renderAt('/mail?folder=INBOX')
    await settle()
    expect(container.querySelector('.context-drawer')).toBeNull()
    expect(container.querySelector('.mail-folders')).toBeTruthy()
  })

  // The reader draws its own action bar across the foot of the screen here, and the compose
  // button is anchored 73px up from that same edge: leaving it there would put a 56px disc over
  // the delete and the kebab. Writing stays one ← away, which the list screen still offers.
  const opened = {
    uid: 1, folderPath: 'INBOX', uidValidity: 1, subject: 'ouvert', fromName: '', fromAddress: 'a@b.c',
    to: [], cc: [], date: '2026-07-18T09:00:00Z', htmlBody: '<p>x</p>', textBody: 'x',
    blockedImageCount: 0, attachments: [],
  }

  it('withholds the compose button while a message owns the screen', async () => {
    mockViewport('phone')
    mocks.getMailMessage.mockResolvedValue(opened)
    const { container } = renderAt('/mail?folder=INBOX&uid=1')
    await screen.findByText('ouvert')
    expect(container.querySelector('.actionbar')).toBeTruthy()
    expect(container.querySelector('.floating-action')).toBeNull()
  })

  it('keeps it on the list screen', async () => {
    mockViewport('phone')
    const { container } = renderAt('/mail?folder=INBOX')
    await settle()
    expect(container.querySelector('.floating-action')).toBeTruthy()
  })

  // A tablet at `none` gives the reader the screen too, but its header has the width to keep the
  // cluster — and the drawer hides the folder column's own Compose, so the button is the only
  // way to write from here.
  it('leaves both alone on a tablet', async () => {
    mockViewport('tablet')
    mocks.getMailMessage.mockResolvedValue(opened)
    const { container } = renderAt('/mail?folder=INBOX&uid=1', undefined, 'none')
    await screen.findByText('ouvert')
    expect(container.querySelector('.actionbar')).toBeNull()
    expect(container.querySelector('.floating-action')).toBeTruthy()
  })
})
