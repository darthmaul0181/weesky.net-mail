import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import MessageList from './MessageList'
import type { MailFolderNode } from '../api/mailTypes'
import { settle } from '../../../test-utils'
import { DRAG_MIME, serializeDrag } from './dragMessages'

const mocks = vi.hoisted(() => ({
  getMailMessages: vi.fn(), getPreferences: vi.fn(), useMessageList: vi.fn(), mutate: vi.fn(),
  move: vi.fn(), remove: vi.fn(), empty: vi.fn(), searchMessages: vi.fn(),
  // A default impl that survives clearAllMocks (which clears calls, not implementations), so the
  // non-searching suites never read a stale result: they never look at it, but it stays benign.
  useSearchMessages: vi.fn(
    (): { data: unknown; isLoading: boolean; isError: boolean } =>
      ({ data: undefined, isLoading: false, isError: false })),
  folders: undefined as unknown[] | undefined,
  onError: undefined as ((message: string) => void) | undefined,
  moveError: undefined as ((message: string) => void) | undefined,
  deleteError: undefined as ((message: string) => void) | undefined,
}))

vi.mock('../../../api.js', () => ({ api: mocks }))
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({ activeAccount: { id: 'primary' } }),
}))
// A plain function, not a vi.fn: the suites clear mocks between tests and the flag hook has
// to keep answering a mutation afterwards.
vi.mock('../queries', () => ({
  useSetFlags: (onError?: (message: string) => void) => {
    mocks.onError = onError
    return { mutate: mocks.mutate }
  },
  useMoveMessages: (onError?: (message: string) => void) => {
    mocks.moveError = onError
    return { mutate: mocks.move }
  },
  useDeleteMessages: (onError?: (message: string) => void) => {
    mocks.deleteError = onError
    return { mutate: mocks.remove, isPending: false }
  },
  useEmptyFolder: () => ({ mutate: mocks.empty, isPending: false }),
  useFolders: () => ({ data: mocks.folders }),
  useSearchMessages: mocks.useSearchMessages,
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

function renderList(props: Partial<ListProps> = {}, preferencesOverride?: Record<string, string>) {
  if (preferencesOverride) {
    mocks.getPreferences.mockResolvedValue(
      { 'mail.pageSize': '50', 'mail.showPreview': 'true', ...preferencesOverride })
  }
  return render(
    <MessageList folderPath="INBOX" selectedUid={null} onSelect={vi.fn()}
      search={null} onSearchChange={() => {}} {...props} />,
    { wrapper })
}

function folderNode(partial: Partial<MailFolderNode>): MailFolderNode {
  return {
    path: 'X', name: 'X', specialUse: null, selectable: true, subscribed: true,
    total: 0, unread: 0, uidValidity: 1, uidNext: null, highestModSeq: null, children: [], ...partial,
  }
}

const roleTree: MailFolderNode[] = [
  folderNode({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox' }),
  folderNode({ path: 'Archives', name: 'Archives', specialUse: 'archive' }),
  folderNode({ path: 'Corbeille', name: 'Corbeille', specialUse: 'trash' }),
]

const wideSample = [
  {
    uid: 3, subject: 'Wide row test', fromName: 'Alice', fromAddress: 'alice@x.be',
    date: '2026-07-19T09:00:00Z', seen: true, flagged: false, answered: false,
    hasAttachments: false, size: 50, preview: 'the preview text',
  },
  {
    uid: 4, subject: 'Another subject', fromName: 'Unseen Sender', fromAddress: 'unseen@x.be',
    date: '2026-07-19T10:00:00Z', seen: false, flagged: false, answered: false,
    hasAttachments: false, size: 60, preview: 'another preview',
  },
]

describe('MessageList', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.folders = roleTree
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

  // The heading band is now the SelectionToolbar; the folder name shows as its title until a
  // selection is on. A row can carry the same text, so the title is read off the toolbar.
  it('names the folder above the list', () => {
    renderList({ folderPath: 'INBOX.Linux server', folderName: 'Linux server' })

    expect(within(document.querySelector('.selection-toolbar') as HTMLElement)
      .getByText('Linux server')).toBeInTheDocument()
  })

  it('falls back to the path when the folder name is unknown', () => {
    renderList()

    expect(within(document.querySelector('.selection-toolbar') as HTMLElement)
      .getByText('INBOX')).toBeInTheDocument()
  })

  // The heading is how the column says what it is showing; a state that drops it leaves the
  // user looking at an unlabelled panel.
  it('keeps the heading while loading and when the folder is empty', () => {
    mocks.useMessageList.mockReturnValue(
      pagedState({}, { messages: [], total: 0, isLoading: true }))
    const bar = () => within(document.querySelector('.selection-toolbar') as HTMLElement)
    const { rerender } = renderList({ folderName: 'INBOX' })

    expect(bar().getByText('INBOX')).toBeInTheDocument()
    expect(screen.getByText(/loading messages/i)).toBeInTheDocument()

    mocks.useMessageList.mockReturnValue(pagedState({}, { messages: [], total: 0 }))
    rerender(
      <MessageList folderPath="INBOX" folderName="INBOX" selectedUid={null} onSelect={vi.fn()}
        search={null} onSearchChange={() => {}} />)

    expect(screen.getByText(/no messages/i)).toBeInTheDocument()
    expect(bar().getByText('INBOX')).toBeInTheDocument()
  })

  // A message with no body used to render no preview element at all, making its row shorter
  // than its neighbours. The element is now always present and CSS reserves its line.
  it('reserves the preview line even when there is nothing to preview', async () => {
    const { container } = renderList()

    await screen.findByText('Alice Martin')

    expect(container.querySelectorAll('.message-row-preview')).toHaveLength(sample.length)
  })

  it('names the attachment marker for assistive technology', () => {
    const { container } = renderList()

    // Only the message that has one — the marker must not be announced on every row.
    expect(container.querySelectorAll('svg[aria-label="Has attachments"]')).toHaveLength(1)
    // The row is a role=button, which hides the marker from assistive tech whatever it is named,
    // so the row's own name is what has to carry it — on that one row.
    const named = screen.getAllByRole('button')
      .filter(element => /has attachments/i.test(element.getAttribute('aria-label') || ''))
    expect(named).toHaveLength(1)
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

    // The row is a div[role=button] since it carries buttons of its own — closest('button')
    // would now walk past it.
    expect(screen.getByText('Alice Martin').closest('.message-row')).toHaveClass('is-unread')
    expect(screen.getByText('bob@x.be').closest('.message-row')).not.toHaveClass('is-unread')
  })

  it('marks the selected row', () => {
    renderList({ selectedUid: 1 })

    expect(screen.getByText('bob@x.be').closest('.message-row')).toHaveClass('is-selected')
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

const draftSample = [
  {
    uid: 5, subject: 'To Bob', fromName: 'Me', fromAddress: 'me@x.be',
    to: [{ name: 'Bob', address: 'bob@x.example' }, { name: '', address: 'carol@ext.example' }],
    date: '2026-07-20T09:00:00Z', seen: true, flagged: false, answered: false,
    hasAttachments: false, size: 10, preview: '',
  },
  {
    uid: 6, subject: 'No recipient yet', fromName: 'Me', fromAddress: 'me@x.be',
    to: [],
    date: '2026-07-20T10:00:00Z', seen: true, flagged: false, answered: false,
    hasAttachments: false, size: 10, preview: '',
  },
]

describe('a drafts folder', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.folders = roleTree
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '50', 'mail.showPreview': 'true' })
    mocks.useMessageList.mockReturnValue(pagedState({}, { messages: draftSample }))
  })

  it('shows the joined recipients and a Draft marker instead of the sender', async () => {
    renderList({ folderRole: 'drafts' })

    expect(await screen.findByText('Bob, carol@ext.example')).toBeInTheDocument()
    expect(screen.getAllByText('Draft')).toHaveLength(draftSample.length)
  })

  // The row is children-presentational, so a marker it shows and its name omits is invisible.
  it('voices the Draft marker in the row name', async () => {
    renderList({ folderRole: 'drafts' })

    expect(await screen.findByRole('button', { name: /^Draft\. Bob, carol@ext\.example: To Bob/ }))
      .toBeInTheDocument()
  })

  it('shows a placeholder when the draft has no recipient', async () => {
    renderList({ folderRole: 'drafts' })

    expect(await screen.findByText('(no recipient)')).toBeInTheDocument()
  })

  it('shows the recipients and the marker in the wide skin too', async () => {
    renderList({ folderRole: 'drafts', wide: true })

    expect(await screen.findByText('Bob, carol@ext.example')).toBeInTheDocument()
    expect(screen.getAllByText('Draft')).toHaveLength(draftSample.length)
  })
})

// Regression pin: only a drafts-role folder swaps the sender for the recipients.
describe('a non-drafts folder', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.folders = roleTree
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '50', 'mail.showPreview': 'true' })
    mocks.useMessageList.mockReturnValue(pagedState())
  })

  it('still shows the sender, with no Draft marker', async () => {
    renderList({ folderRole: null })

    expect(await screen.findByText('Alice Martin')).toBeInTheDocument()
    expect(screen.queryByText('Draft')).not.toBeInTheDocument()
  })
})

describe('the row controls', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.folders = roleTree
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '50', 'mail.showPreview': 'true' })
    mocks.useMessageList.mockReturnValue(pagedState())
  })

  const rowOf = (name: RegExp) => screen.getByRole('button', { name })

  it('opens a row from the keyboard', () => {
    const onSelect = vi.fn()
    renderList({ onSelect })

    fireEvent.keyDown(rowOf(/alice martin/i), { key: 'Enter' })

    expect(onSelect).toHaveBeenCalledWith(2)
  })

  it('opens a row on Space, without scrolling the list', () => {
    const onSelect = vi.fn()
    renderList({ onSelect })

    // keyDown returns false when a listener called preventDefault; Space would scroll otherwise.
    const notScrolled = !fireEvent.keyDown(rowOf(/alice martin/i), { key: ' ' })

    expect(onSelect).toHaveBeenCalledWith(2)
    expect(notScrolled).toBe(true)
  })

  // fireEvent reaches an unfocusable node just as well; without this the row could leave the tab
  // order — no keyboard path, and no :focus-within to reveal the cluster — and nothing would say so.
  it('puts the row in the tab order', () => {
    renderList()

    expect(rowOf(/alice martin/i)).toHaveAttribute('tabindex', '0')
  })

  // A role=button with no name of its own takes it from its contents: every row would announce
  // "…Star, Mark as unread, button". The date is read off the row rather than hard-coded —
  // formatListDate answers in the runner's locale.
  it('names the row after the message, not after its controls', () => {
    renderList()

    const row = rowOf(/alice martin/i)
    const date = row.querySelector('.message-row-date')!.textContent
    expect(row).toHaveAttribute(
      'aria-label', `Unread. Alice Martin: Re: facture, has attachments, ${date}`)
    expect(row.getAttribute('aria-label')).not.toMatch(/star/i)
  })

  // role=button is children-presentational, so the dot, the weight and the colour that carry
  // read state visually reach nobody: the name is the only place left to say it.
  it('says unread in the name, and says nothing on a read row', () => {
    renderList()

    expect(rowOf(/alice martin/i).getAttribute('aria-label')).toMatch(/^Unread\. /)
    expect(rowOf(/bob@x\.be/i).getAttribute('aria-label')).not.toMatch(/unread/i)
  })

  // An inner button fires Enter itself; without the target guard the row would open behind it.
  it('does not open the row when a control inside it takes the key', () => {
    const onSelect = vi.fn()
    renderList({ onSelect })

    fireEvent.keyDown(within(rowOf(/alice martin/i)).getByRole('button', { name: 'Star' }),
      { key: 'Enter' })

    expect(onSelect).not.toHaveBeenCalled()
  })

  it('the star toggles flagged without opening the message', () => {
    const onSelect = vi.fn()
    renderList({ onSelect })

    fireEvent.click(within(rowOf(/alice martin/i)).getByRole('button', { name: 'Star' }))

    expect(mocks.mutate).toHaveBeenCalledWith(
      { folderPath: 'INBOX', uids: [2], flag: 'flagged', value: true })
    expect(onSelect).not.toHaveBeenCalled()
  })

  it('a flagged row offers Unstar', () => {
    mocks.useMessageList.mockReturnValue(
      pagedState({}, { messages: [{ ...sample[0], flagged: true }] }))
    renderList()

    const star = screen.getByRole('button', { name: 'Unstar' })
    expect(star.querySelector('svg')).toHaveAttribute('fill', 'currentColor')

    fireEvent.click(star)
    expect(mocks.mutate).toHaveBeenCalledWith(
      { folderPath: 'INBOX', uids: [2], flag: 'flagged', value: false })
  })

  it('the cluster toggles seen without opening the message', () => {
    const onSelect = vi.fn()
    renderList({ onSelect })

    fireEvent.click(within(rowOf(/alice martin/i)).getByRole('button', { name: 'Mark as read' }))

    expect(mocks.mutate).toHaveBeenCalledWith(
      { folderPath: 'INBOX', uids: [2], flag: 'seen', value: true })
    expect(onSelect).not.toHaveBeenCalled()
  })

  it('offers the way back on a read row', () => {
    renderList()

    fireEvent.click(within(rowOf(/bob@x\.be/i)).getByRole('button', { name: 'Mark as unread' }))

    expect(mocks.mutate).toHaveBeenCalledWith(
      { folderPath: 'INBOX', uids: [1], flag: 'seen', value: false })
  })

  it('carries the controls in wide rows too', () => {
    mocks.useMessageList.mockReturnValue(pagedState({}, { messages: wideSample }))
    renderList({ wide: true })

    const row = rowOf(/wide row test/i)
    expect(within(row).getByRole('button', { name: 'Star' })).toBeInTheDocument()
    expect(within(row).getByRole('button', { name: 'Mark as unread' })).toBeInTheDocument()
    expect(within(row).getByRole('button', { name: 'Archive' })).toBeInTheDocument()
    expect(within(row).getByRole('button', { name: 'Delete' })).toBeInTheDocument()
  })

  it('gives each enabled action button a hover tooltip', () => {
    renderList()

    const row = rowOf(/bob@x\.be/i)
    expect(within(row).getByRole('button', { name: 'Mark as unread' }))
      .toHaveAttribute('title', 'Mark as unread')
    expect(within(row).getByRole('button', { name: 'Archive' }))
      .toHaveAttribute('title', 'Archive')
    expect(within(row).getByRole('button', { name: 'Delete' }))
      .toHaveAttribute('title', 'Delete')
  })

  it('mutation errors reach onNotify', () => {
    const onNotify = vi.fn()
    renderList({ onNotify })

    mocks.onError?.('Could not update the message')

    expect(onNotify).toHaveBeenCalledWith('Could not update the message')
  })
})

describe('archive and trash from the row', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.folders = roleTree
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '50', 'mail.showPreview': 'true' })
    mocks.useMessageList.mockReturnValue(pagedState())
  })

  const rowOf = (name: RegExp) => screen.getByRole('button', { name })
  // The modal's confirm button is named "Delete" too — the row's own must not answer for it.
  const modal = () => within(document.querySelector('.modal') as HTMLElement)

  it('archives to the folder holding the role, without opening the message', async () => {
    const onSelect = vi.fn()
    renderList({ onSelect })

    fireEvent.click(within(rowOf(/alice martin/i)).getByRole('button', { name: 'Archive' }))

    expect(mocks.move).toHaveBeenCalledWith(
      { folderPath: 'INBOX', uids: [2], targetFolderPath: 'Archives', copy: false })
    await settle()
    expect(onSelect).not.toHaveBeenCalled()
  })

  // Outside the trash, deleting is a move: the trash is the safety net, so there is nothing to
  // confirm and nothing is lost.
  it('deletes by moving to the trash folder, with no confirmation', async () => {
    const onSelect = vi.fn()
    renderList({ onSelect })

    fireEvent.click(within(rowOf(/alice martin/i)).getByRole('button', { name: 'Delete' }))

    expect(mocks.move).toHaveBeenCalledWith(
      { folderPath: 'INBOX', uids: [2], targetFolderPath: 'Corbeille', copy: false })
    await settle()
    expect(onSelect).not.toHaveBeenCalled()
    expect(screen.queryByText(/confirm deletion/i)).not.toBeInTheDocument()
  })

  // A missing button reads as a bug; a disabled one carrying its reason is an instruction.
  it('offers archive disabled with its reason when no folder holds the role', () => {
    mocks.folders = [folderNode({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox' })]
    renderList()

    const archive = within(rowOf(/alice martin/i)).getByRole('button', { name: 'Archive' })
    expect(archive).toBeDisabled()
    expect(archive).toHaveAttribute('title', 'Assign the archive folder in Settings → Folders')
  })

  it('offers delete disabled with its reason when no folder holds the trash role', () => {
    mocks.folders = [folderNode({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox' })]
    renderList()

    const trash = within(rowOf(/alice martin/i)).getByRole('button', { name: 'Delete' })
    expect(trash).toBeDisabled()
    expect(trash).toHaveAttribute('title', 'Assign the trash folder in Settings → Folders')
  })

  it('disables archive inside the archive folder', () => {
    renderList({ folderPath: 'Archives', folderRole: 'archive' })

    const archive = within(rowOf(/alice martin/i)).getByRole('button', { name: 'Archive' })
    expect(archive).toBeDisabled()
    expect(archive).toHaveAttribute('title', 'Already in the archive folder')
  })

  // Inside the trash the row's own button expunges, so it matches the reader's wording rather
  // than the everyday move-to-trash label.
  it('names the trash row button "Delete permanently"', () => {
    renderList({ folderPath: 'Corbeille', folderRole: 'trash' })

    expect(within(rowOf(/alice martin/i)).getByRole('button', { name: 'Delete permanently' }))
      .toBeInTheDocument()
    expect(within(rowOf(/alice martin/i)).queryByRole('button', { name: 'Delete' })).toBeNull()
  })

  it('waits for a confirmation before expunging from the trash', async () => {
    renderList({ folderPath: 'Corbeille', folderRole: 'trash' })

    fireEvent.click(within(rowOf(/alice martin/i)).getByRole('button', { name: 'Delete permanently' }))

    expect(modal().getByText('Re: facture')).toBeInTheDocument()
    await settle()
    expect(mocks.remove).not.toHaveBeenCalled()
    expect(mocks.move).not.toHaveBeenCalled()

    fireEvent.click(modal().getByRole('button', { name: 'Delete' }))
    expect(mocks.remove).toHaveBeenCalledWith({ folderPath: 'Corbeille', uids: [2] })
  })

  it('names a subjectless message in the confirmation', () => {
    renderList({ folderPath: 'Corbeille', folderRole: 'trash' })

    fireEvent.click(within(rowOf(/bob@x\.be/i)).getByRole('button', { name: 'Delete permanently' }))

    expect(modal().getByText('(no subject)')).toBeInTheDocument()
  })

  it('expunges nothing when the confirmation is dismissed', async () => {
    renderList({ folderPath: 'Corbeille', folderRole: 'trash' })

    fireEvent.click(within(rowOf(/alice martin/i)).getByRole('button', { name: 'Delete permanently' }))
    fireEvent.click(modal().getByRole('button', { name: '✕' }))

    await settle()
    expect(mocks.remove).not.toHaveBeenCalled()
    expect(screen.queryByText(/confirm deletion/i)).not.toBeInTheDocument()
  })

  // The row leaves optimistically, so the selection has to follow in the same gesture.
  it('reports the departing uid on archive, on trash and on an expunge', async () => {
    const onDeparted = vi.fn()
    const { unmount } = renderList({ onDeparted })

    fireEvent.click(within(rowOf(/alice martin/i)).getByRole('button', { name: 'Archive' }))
    expect(onDeparted).toHaveBeenCalledWith(2)

    fireEvent.click(within(rowOf(/bob@x\.be/i)).getByRole('button', { name: 'Delete' }))
    expect(onDeparted).toHaveBeenCalledWith(1)
    unmount()

    onDeparted.mockClear()
    renderList({ folderPath: 'Corbeille', folderRole: 'trash', onDeparted })
    fireEvent.click(within(rowOf(/alice martin/i)).getByRole('button', { name: 'Delete permanently' }))
    await settle()
    expect(onDeparted).not.toHaveBeenCalled()

    fireEvent.click(modal().getByRole('button', { name: 'Delete' }))
    expect(onDeparted).toHaveBeenCalledWith(2)
  })

  it('reports the rows it shows, and again when they change', async () => {
    const onRows = vi.fn()
    const { rerender } = renderList({ onRows })

    await settle()
    expect(onRows).toHaveBeenLastCalledWith([2, 1])

    mocks.useMessageList.mockReturnValue(pagedState({}, { messages: wideSample }))
    rerender(
      <MessageList folderPath="INBOX" selectedUid={null} onSelect={vi.fn()} onRows={onRows}
        search={null} onSearchChange={() => {}} />)

    await settle()
    expect(onRows).toHaveBeenLastCalledWith([3, 4])
  })

  it('move and delete failures reach onNotify', () => {
    const onNotify = vi.fn()
    renderList({ onNotify })

    mocks.moveError?.('Could not move the message')
    mocks.deleteError?.('Could not delete the message')

    expect(onNotify).toHaveBeenCalledWith('Could not move the message')
    expect(onNotify).toHaveBeenCalledWith('Could not delete the message')
  })
})

describe('wide rows', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.folders = roleTree
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '50', 'mail.showPreview': 'true' })
    mocks.useMessageList.mockReturnValue(pagedState({}, { messages: wideSample }))
  })

  // One line per message: sender · subject — preview · date. The stacked markup must not
  // leak in — a .message-row-top inside a line row would stack the line again.
  it('renders a single-line row with the preview inline', async () => {
    renderList({ wide: true })

    const row = await screen.findByRole('button', { name: /alice/i })
    expect(row).toHaveClass('is-line')
    expect(row.querySelector('.message-row-line-preview')).toHaveTextContent('the preview text')
    expect(row.querySelector('.message-row-top')).toBeNull()
  })

  it('ends the line at the subject when previews are off', async () => {
    renderList({ wide: true }, { 'mail.showPreview': 'false' })

    const row = await screen.findByRole('button', { name: /alice/i })
    await vi.waitFor(() => expect(row.querySelector('.message-row-line-preview')).toBeNull())
  })

  it('keeps the unread dot and classes in wide rows', async () => {
    renderList({ wide: true })

    const unreadRow = await screen.findByRole('button', { name: /unseen sender/i })
    expect(unreadRow).toHaveClass('is-unread')
    expect(unreadRow.querySelector('.message-row-unread-dot')).not.toBeNull()
  })

  it('keeps stacked rows when wide is off', async () => {
    renderList()

    const row = await screen.findByRole('button', { name: /alice/i })
    expect(row).not.toHaveClass('is-line')
    expect(row.querySelector('.message-row-top')).not.toBeNull()
  })
})

describe('the preferences it obeys', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.folders = roleTree
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
    mocks.folders = roleTree
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': 'all', 'mail.showPreview': 'true' })
  })

  // There is no page to go to and no count to report: the whole band goes, and the rows get
  // its height. A footer here would be a permanently empty strip above the list.
  it('carries no footer at all — neither a pager nor a loaded/total counter', () => {
    mocks.useMessageList.mockReturnValue(streamingState())
    const { container } = renderList()

    expect(screen.queryByRole('navigation', { name: 'Pages' })).not.toBeInTheDocument()
    expect(container.querySelector('.mail-list-footer')).toBeNull()
    expect(screen.queryByText(/^\d[\d,]* of [\d,]+$/)).not.toBeInTheDocument()
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

  it('drops the sentinel on an empty folder', () => {
    mocks.useMessageList.mockReturnValue({
      messages: [], total: 0, isLoading: false, isError: false, paging: null,
      streaming: { hasMore: false, isLoadingMore: false, loadMoreFailed: false, loadMore: vi.fn() },
    })
    const { container } = renderList()

    expect(screen.getByText('No messages')).toBeInTheDocument()
    expect(container.querySelector('.message-list-sentinel')).toBeNull()
  })

  it('returns to the top when the folder changes', () => {
    mocks.useMessageList.mockReturnValue(streamingState())
    const { container, rerender } = renderList()

    const band = container.querySelector('.mail-list-scroll') as HTMLDivElement
    band.scrollTop = 900
    rerender(<MessageList folderPath="Archive" selectedUid={null} onSelect={vi.fn()}
      search={null} onSearchChange={() => {}} />)

    expect(band.scrollTop).toBe(0)
  })
})

describe('multi-select', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.folders = roleTree
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '50', 'mail.showPreview': 'true' })
    mocks.useMessageList.mockReturnValue(pagedState())
  })

  // The row buttons carry the same names as the toolbar's (Archive, Delete permanently…), so a
  // toolbar action is always read off the toolbar band, never with a bare screen query.
  const bar = () => within(document.querySelector('.selection-toolbar') as HTMLElement)

  function renderWithRoles(
    role?: 'trash', props: Partial<ListProps> = {}, stateOverrides?: Record<string, unknown>) {
    const base = role === 'trash'
      ? { folderPath: 'Trash', folderName: 'Trash', folderRole: 'trash' as const }
      : { folderPath: 'INBOX', folderName: 'Inbox', folderRole: 'inbox' as const }
    if (stateOverrides) mocks.useMessageList.mockReturnValue(pagedState({}, stateOverrides))
    return renderList({ ...base, ...props })
  }

  it('checking rows shows the count and enables the direct actions', () => {
    renderWithRoles()
    fireEvent.click(screen.getByRole('checkbox', { name: /select message from alice/i }))
    expect(screen.getByText('1 selected')).toBeInTheDocument()
    expect(bar().getByRole('button', { name: 'Archive' })).toBeEnabled()
  })

  it('a shift-click selects the range', () => {
    renderWithRoles()
    const boxes = screen.getAllByRole('checkbox', { name: /select message from/i })
    fireEvent.click(boxes[0])
    fireEvent.click(boxes[1], { shiftKey: true })
    expect(screen.getByText('2 selected')).toBeInTheDocument()
  })

  it('the master checkbox selects and clears all loaded rows', () => {
    renderWithRoles()
    const master = screen.getByRole('checkbox', { name: 'Select all' })
    fireEvent.click(master)
    expect(screen.getByText('2 selected')).toBeInTheDocument()
    fireEvent.click(master)
    expect(screen.getByText('Inbox')).toBeInTheDocument()  // title back, selection cleared
  })

  it('bulk archive moves the whole selection and clears it', async () => {
    renderWithRoles()
    fireEvent.click(screen.getByRole('checkbox', { name: 'Select all' }))
    fireEvent.click(bar().getByRole('button', { name: 'Archive' }))
    expect(mocks.move).toHaveBeenCalledWith(expect.objectContaining({
      folderPath: 'INBOX', uids: [2, 1], targetFolderPath: 'Archives', copy: false,
    }))
    await settle()
    expect(screen.getByText('Inbox')).toBeInTheDocument()  // selection cleared
  })

  it('bulk delete inside the trash asks for confirmation, then expunges the batch', async () => {
    renderWithRoles('trash')
    fireEvent.click(screen.getByRole('checkbox', { name: 'Select all' }))
    fireEvent.click(bar().getByRole('button', { name: 'Delete permanently' }))
    await settle()
    expect(mocks.remove).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Delete' }))  // confirm modal
    expect(mocks.remove).toHaveBeenCalledWith(
      expect.objectContaining({ folderPath: 'Trash', uids: [2, 1] }))
  })

  it('advances the reader when the open message is in the acted batch', () => {
    const onDeparted = vi.fn()
    renderWithRoles(undefined, { selectedUid: 2, onDeparted })
    fireEvent.click(screen.getByRole('checkbox', { name: 'Select all' }))
    fireEvent.click(bar().getByRole('button', { name: 'Archive' }))
    // The whole batch travels, so the layout can skip every departing row, not just the open one.
    expect(onDeparted).toHaveBeenCalledWith(2, [2, 1])
  })

  it('does not advance the reader when the open message is untouched', async () => {
    const onDeparted = vi.fn()
    renderWithRoles(undefined, { selectedUid: 999, onDeparted })
    fireEvent.click(screen.getByRole('checkbox', { name: /select message from alice/i }))
    fireEvent.click(bar().getByRole('button', { name: 'Archive' }))
    await settle()
    expect(onDeparted).not.toHaveBeenCalled()
  })

  it('clears the selection when the folder changes', async () => {
    const { rerender } = renderWithRoles()
    fireEvent.click(screen.getByRole('checkbox', { name: 'Select all' }))
    expect(screen.getByText('2 selected')).toBeInTheDocument()
    rerender(
      <MessageList folderPath="Archives" folderName="Archive" folderRole="archive"
        selectedUid={null} onSelect={vi.fn()} search={null} onSearchChange={() => {}} />)
    await settle()
    expect(screen.queryByText('2 selected')).not.toBeInTheDocument()
  })

  it('Escape clears an active selection', () => {
    renderWithRoles()
    fireEvent.click(screen.getByRole('checkbox', { name: 'Select all' }))
    fireEvent.keyDown(screen.getByRole('checkbox', { name: 'Select all' }), { key: 'Escape' })
    expect(screen.getByText('Inbox')).toBeInTheDocument()
  })

  describe('empty folder', () => {
    it('shows the trash banner and purges after confirmation', async () => {
      renderWithRoles('trash')
      fireEvent.click(screen.getByRole('button', { name: 'Empty trash now' }))
      await settle()
      expect(mocks.empty).not.toHaveBeenCalled()          // confirm first
      // Fuller, folder-worded warning; the ✕ is the only way out, no Cancel button.
      expect(screen.getByText(/permanently delete all emails from the Trash folder/i)).toBeInTheDocument()
      expect(screen.getByText(/cannot be interrupted or undone/i)).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument()
      fireEvent.click(screen.getByRole('button', { name: 'Delete' }))
      expect(mocks.empty).toHaveBeenCalledWith({ folderPath: 'Trash' })
    })

    it('no banner outside trash/junk', async () => {
      renderWithRoles() // INBOX
      await settle()
      expect(screen.queryByRole('button', { name: /empty .* now/i })).not.toBeInTheDocument()
    })

    it('kebab Empty folder on a normal folder moves everything to trash, no confirm', async () => {
      renderWithRoles() // INBOX
      fireEvent.click(screen.getByRole('button', { name: 'More actions' }))
      fireEvent.click(screen.getByRole('menuitem', { name: 'Empty folder' }))
      expect(mocks.empty).toHaveBeenCalledWith({ folderPath: 'INBOX', targetFolderPath: expect.any(String) })
    })

    it('disables Empty folder when the folder is empty', () => {
      renderWithRoles(undefined, {}, { total: 0, messages: [] })
      fireEvent.click(screen.getByRole('button', { name: 'More actions' }))
      expect(screen.getByRole('menuitem', { name: 'Empty folder' })).toBeDisabled()
    })
  })
})

describe('MessageList as a drag source', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.folders = roleTree
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '50', 'mail.showPreview': 'true' })
    mocks.useMessageList.mockReturnValue(pagedState())
  })

  const rowOf = (name: RegExp) => screen.getByRole('button', { name })
  const checkOf = (label: string) => screen.getByRole('checkbox', { name: label })

  function dragDT() {
    const store: Record<string, string> = {}
    return {
      setData: vi.fn((type: string, value: string) => { store[type] = value }),
      getData: (type: string) => store[type] ?? '',
      setDragImage: vi.fn(),
      effectAllowed: 'uninitialized',
      types: [] as string[],
    }
  }

  it('makes each row draggable', () => {
    renderList()
    expect(rowOf(/alice martin/i)).toHaveAttribute('draggable', 'true')
  })

  it('carries the source folder and the dragged uid alone when nothing is checked', () => {
    renderList()
    const dataTransfer = dragDT()

    fireEvent.dragStart(rowOf(/alice martin/i), { dataTransfer })

    expect(dataTransfer.setData).toHaveBeenCalledWith(
      DRAG_MIME, serializeDrag({ sourcePath: 'INBOX', uids: [2] }))
    expect(dataTransfer.effectAllowed).toBe('move')
    expect(dataTransfer.setDragImage).toHaveBeenCalled()
  })

  it('carries the whole selection when the dragged row is part of it', () => {
    renderList()
    fireEvent.click(checkOf('Select message from Alice Martin'))
    fireEvent.click(checkOf('Select message from bob@x.be'))
    const dataTransfer = dragDT()

    fireEvent.dragStart(rowOf(/alice martin/i), { dataTransfer })

    expect(dataTransfer.setData).toHaveBeenCalledWith(
      DRAG_MIME, serializeDrag({ sourcePath: 'INBOX', uids: [2, 1] }))
  })

  it('drags an unchecked row alone without disturbing the selection', () => {
    renderList()
    fireEvent.click(checkOf('Select message from Alice Martin'))
    const dataTransfer = dragDT()

    fireEvent.dragStart(rowOf(/bob@x\.be/i), { dataTransfer })

    expect(dataTransfer.setData).toHaveBeenCalledWith(
      DRAG_MIME, serializeDrag({ sourcePath: 'INBOX', uids: [1] }))
    // The checkbox made for Alice is still checked.
    expect(checkOf('Select message from Alice Martin')).toBeChecked()
  })

  it('marks the dragged rows while the drag lasts and clears them after', () => {
    renderList()
    fireEvent.click(checkOf('Select message from Alice Martin'))
    fireEvent.click(checkOf('Select message from bob@x.be'))

    fireEvent.dragStart(rowOf(/alice martin/i), { dataTransfer: dragDT() })
    expect(rowOf(/alice martin/i)).toHaveClass('is-dragging')
    expect(rowOf(/bob@x\.be/i)).toHaveClass('is-dragging')

    fireEvent.dragEnd(rowOf(/alice martin/i), { dataTransfer: dragDT() })
    expect(rowOf(/alice martin/i)).not.toHaveClass('is-dragging')
  })
})

describe('MessageList searching', () => {
  const criteria = { folderPath: 'INBOX', allFolders: false, quick: 'x' }

  const results = [
    {
      uid: 10, subject: 'First hit', fromName: 'Carol', fromAddress: 'carol@x.be',
      date: '2026-07-20T09:00:00Z', seen: true, flagged: false, answered: false,
      hasAttachments: false, size: 10, preview: '', folderPath: 'INBOX', uidValidity: 1,
    },
    {
      uid: 11, subject: 'Second hit', fromName: 'Dave', fromAddress: 'dave@x.be',
      date: '2026-07-19T09:00:00Z', seen: true, flagged: false, answered: false,
      hasAttachments: false, size: 10, preview: '', folderPath: 'INBOX', uidValidity: 1,
    },
  ]

  const page = (data: unknown) => ({ data, isLoading: false, isError: false })

  beforeEach(() => {
    vi.clearAllMocks()
    mocks.folders = roleTree
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '50', 'mail.showPreview': 'true' })
    mocks.useMessageList.mockReturnValue(pagedState())
    mocks.useSearchMessages.mockReturnValue(page(undefined))
  })

  const bar = () => within(document.querySelector('.selection-toolbar') as HTMLElement)

  it('the magnifier unfolds the bar and a query calls onSearchChange', () => {
    const onSearchChange = vi.fn()
    renderList({ onSearchChange })

    fireEvent.click(bar().getByRole('button', { name: 'Search' }))
    const input = screen.getByPlaceholderText('Search in INBOX')
    fireEvent.change(input, { target: { value: 'x' } })
    fireEvent.keyDown(input, { key: 'Enter' })

    expect(onSearchChange).toHaveBeenCalledWith({ folderPath: 'INBOX', allFolders: false, quick: 'x' })
  })

  // The results replace the list, the banner counts them, and the empty-folder banner is gone —
  // even in the trash, where it would otherwise offer to purge under the search.
  it('renders the results and the banner in place of the list', () => {
    mocks.useSearchMessages.mockReturnValue(page({ total: 2, page: 0, pageSize: 50, results }))
    renderList({ search: criteria, folderPath: 'Corbeille', folderName: 'Trash', folderRole: 'trash' })

    expect(screen.getByText('First hit')).toBeInTheDocument()
    expect(screen.getByText('Second hit')).toBeInTheDocument()
    // The underlying folder list (from useMessageList) is not shown.
    expect(screen.queryByText('Re: facture')).not.toBeInTheDocument()
    expect(screen.getByText('2 results for “x”')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /empty .* now/i })).not.toBeInTheDocument()
  })

  it('the banner Clear calls onSearchChange(null)', () => {
    const onSearchChange = vi.fn()
    mocks.useSearchMessages.mockReturnValue(page({ total: 2, page: 0, pageSize: 50, results }))
    renderList({ search: criteria, onSearchChange })

    fireEvent.click(screen.getByRole('button', { name: 'Clear' }))

    expect(onSearchChange).toHaveBeenCalledWith(null)
  })

  it('collapsing the bar with the magnifier clears the search', () => {
    const onSearchChange = vi.fn()
    renderList({ onSearchChange })

    fireEvent.click(bar().getByRole('button', { name: 'Search' }))  // open
    fireEvent.click(bar().getByRole('button', { name: 'Search' }))  // close

    expect(onSearchChange).toHaveBeenCalledWith(null)
  })

  // All-folders results neutralize selection and row actions: a row lives in another folder, so
  // its uid means nothing here except to open it there.
  it('neutralizes selection and opens cross-folder results via onOpenResult', async () => {
    const onOpenResult = vi.fn()
    const onRows = vi.fn()
    const cross = [{ ...results[0], uid: 20, subject: 'From archive', folderPath: 'Archive' }]
    mocks.useSearchMessages.mockReturnValue(page({ total: 1, page: 0, pageSize: 50, results: cross }))
    renderList({ search: { folderPath: 'INBOX', allFolders: true, quick: 'x' }, onOpenResult, onRows })

    await settle()
    // No per-row checkbox, no star, the master is disabled, the row is not draggable.
    expect(screen.queryByRole('checkbox', { name: /select message from/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Star' })).not.toBeInTheDocument()
    expect(screen.getByRole('checkbox', { name: 'Select all' })).toBeDisabled()
    expect(screen.getByText('From archive').closest('.message-row')).toHaveAttribute('draggable', 'false')
    // The reader is told nothing is navigable in these rows.
    expect(onRows).toHaveBeenLastCalledWith([])

    fireEvent.click(screen.getByText('From archive'))
    expect(onOpenResult).toHaveBeenCalledWith(20, 'Archive')
  })

  it('in a current-folder search the rows select and carry checkboxes', () => {
    const onSelect = vi.fn()
    mocks.useSearchMessages.mockReturnValue(page({ total: 2, page: 0, pageSize: 50, results }))
    renderList({ search: criteria, onSelect })

    expect(screen.getAllByRole('checkbox', { name: /select message from/i })).toHaveLength(2)
    fireEvent.click(screen.getByText('First hit'))
    expect(onSelect).toHaveBeenCalledWith(10)
  })

  it('says "Searching…" while in flight and "No results." when empty', () => {
    mocks.useSearchMessages.mockReturnValue({ data: undefined, isLoading: true, isError: false })
    const { rerender } = renderList({ search: criteria })
    // The banner also reads "Searching…" when the count is unknown; this asserts the list body.
    expect(screen.getByText('Searching…', { selector: '.mail-empty' })).toBeInTheDocument()

    mocks.useSearchMessages.mockReturnValue(page({ total: 0, page: 0, pageSize: 50, results: [] }))
    rerender(
      <MessageList folderPath="INBOX" selectedUid={null} onSelect={vi.fn()}
        search={criteria} onSearchChange={vi.fn()} />)
    expect(screen.getByText('No results.')).toBeInTheDocument()
  })

  it('pages the results and requests the chosen page', async () => {
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '100', 'mail.showPreview': 'true' })
    mocks.useSearchMessages.mockReturnValue(page({ total: 300, page: 0, pageSize: 100, results }))
    renderList({ search: criteria })

    // 300 over 100 per page is three pages, once the page size resolves from preferences.
    expect(await screen.findByRole('button', { name: 'Page 3' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Page 2' }))
    // Zero-based on the wire: the second page is 1, and it re-queries at that page.
    expect(mocks.useSearchMessages).toHaveBeenCalledWith(criteria, 1, 100)
  })

  // Empty folder acts on the whole real folder; under a search it must be neutralized, or hits in a
  // normal folder would let it move the entire folder to trash with no confirmation.
  it('neutralizes the kebab Empty folder while searching', () => {
    mocks.useSearchMessages.mockReturnValue(page({ total: 2, page: 0, pageSize: 50, results }))
    renderList({ search: criteria })  // INBOX: non-empty, non-trash

    fireEvent.click(screen.getByRole('button', { name: 'More actions' }))
    const entry = screen.getByRole('menuitem', { name: 'Empty folder' })
    expect(entry).toBeDisabled()
    expect(entry).toHaveAttribute('title', 'Clear the search first')

    fireEvent.click(entry)
    expect(mocks.empty).not.toHaveBeenCalled()
  })

  // A search with no hits must not read the search total as the folder being empty.
  it('does not show a false "already empty" when a search returns no hits', () => {
    mocks.useSearchMessages.mockReturnValue(page({ total: 0, page: 0, pageSize: 50, results: [] }))
    renderList({ search: criteria })

    fireEvent.click(screen.getByRole('button', { name: 'More actions' }))
    const entry = screen.getByRole('menuitem', { name: 'Empty folder' })
    expect(entry).toHaveAttribute('title', 'Clear the search first')
    expect(entry).not.toHaveAttribute('title', 'This folder is already empty')
  })
})

/** The one-click filter in the heading. It is a search on this folder carrying `flagged`, so
    the rows keep their checkbox, their own star and their drag — a same-folder search is not
    neutralised the way an all-folders one is. */
describe('MessageList starred filter', () => {
  const starred = { folderPath: 'INBOX', allFolders: false, flagged: true }
  const page = (data: unknown) => ({ data, isLoading: false, isError: false })
  const results = [{
    uid: 10, subject: 'First hit', fromName: 'Carol', fromAddress: 'carol@x.be',
    date: '2026-07-20T09:00:00Z', seen: true, flagged: true, answered: false,
    hasAttachments: false, size: 10, preview: '', folderPath: 'INBOX', uidValidity: 1,
  }, {
    uid: 11, subject: 'Second hit', fromName: 'Dave', fromAddress: 'dave@x.be',
    date: '2026-07-19T09:00:00Z', seen: true, flagged: true, answered: false,
    hasAttachments: false, size: 10, preview: '', folderPath: 'INBOX', uidValidity: 1,
  }]

  it('starts a starred search on this folder', () => {
    const onSearchChange = vi.fn()
    renderList({ onSearchChange })

    fireEvent.click(screen.getByRole('button', { name: 'Show starred only' }))

    expect(onSearchChange).toHaveBeenCalledWith(starred)
  })

  it('clears the search when the filter was all there was', () => {
    const onSearchChange = vi.fn()
    mocks.useSearchMessages.mockReturnValue(page({ total: 0, page: 0, pageSize: 50, results: [] }))
    renderList({ search: starred, onSearchChange })

    fireEvent.click(screen.getByRole('button', { name: 'Show all messages' }))

    expect(onSearchChange).toHaveBeenCalledWith(null)
  })

  // Switching the star off must not throw away the rest of an advanced search.
  it('drops only the flag from a richer search', () => {
    const onSearchChange = vi.fn()
    mocks.useSearchMessages.mockReturnValue(page({ total: 0, page: 0, pageSize: 50, results: [] }))
    renderList({ search: { ...starred, quick: 'invoice' }, onSearchChange })

    fireEvent.click(screen.getByRole('button', { name: 'Show all messages' }))

    expect(onSearchChange).toHaveBeenCalledWith({ folderPath: 'INBOX', allFolders: false, quick: 'invoice' })
  })

  it('adds the flag to a search already running', () => {
    const onSearchChange = vi.fn()
    mocks.useSearchMessages.mockReturnValue(page({ total: 0, page: 0, pageSize: 50, results: [] }))
    renderList({ search: { folderPath: 'INBOX', allFolders: false, quick: 'invoice' }, onSearchChange })

    fireEvent.click(screen.getByRole('button', { name: 'Show starred only' }))

    expect(onSearchChange).toHaveBeenCalledWith({ folderPath: 'INBOX', allFolders: false, quick: 'invoice', flagged: true })
  })

  // The lit star already says what is going on, so the heading keeps the folder name.
  it('keeps the folder name and shows no banner while the filter stands alone', () => {
    mocks.useSearchMessages.mockReturnValue(page({ total: 2, page: 0, pageSize: 50, results }))
    renderList({ search: starred, folderName: 'Inbox' })

    expect(screen.getByText('Inbox')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Clear' })).not.toBeInTheDocument()
    expect(screen.queryByText(/results?/)).not.toBeInTheDocument()
  })

  it('brings the banner back as soon as another criterion joins', () => {
    mocks.useSearchMessages.mockReturnValue(page({ total: 2, page: 0, pageSize: 50, results }))
    renderList({ search: { ...starred, quick: 'x' }, folderName: 'Inbox' })

    expect(screen.getByText('2 results for “x”')).toBeInTheDocument()
  })
})

describe('choosable row actions', () => {
  const junkTree: MailFolderNode[] = [
    ...roleTree,
    folderNode({ path: 'Indesirables', name: 'Indesirables', specialUse: 'junk' }),
  ]

  beforeEach(() => {
    vi.clearAllMocks()
    mocks.folders = junkTree
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '50', 'mail.showPreview': 'true' })
    mocks.useMessageList.mockReturnValue(pagedState())
  })

  const rowOf = (name: RegExp) => screen.getByRole('button', { name })

  // An older backend answers without the key at all. Reading that as "nothing chosen" would
  // strip every icon off every row on the first render after a deploy.
  it('draws the three icons it always had when the key is absent', () => {
    renderList()

    const row = rowOf(/bob@x\.be/i)
    expect(within(row).getByRole('button', { name: 'Mark as unread' })).toBeInTheDocument()
    expect(within(row).getByRole('button', { name: 'Archive' })).toBeInTheDocument()
    expect(within(row).getByRole('button', { name: 'Delete' })).toBeInTheDocument()
    expect(within(row).queryByRole('button', { name: 'Report as junk' })).toBeNull()
  })

  it('adds junk to the row once it is chosen', async () => {
    renderList({}, { 'mail.rowActions': 'seen,archive,junk,delete' })

    expect(await within(rowOf(/bob@x\.be/i)).findByRole('button', { name: 'Report as junk' }))
      .toBeInTheDocument()
  })

  it('reports to the folder holding the junk role', async () => {
    renderList({}, { 'mail.rowActions': 'junk' })

    const junk = await within(rowOf(/alice martin/i)).findByRole('button', { name: 'Report as junk' })
    expect(junk).toBeEnabled()
    fireEvent.click(junk)

    expect(mocks.move).toHaveBeenCalledWith(
      { folderPath: 'INBOX', uids: [2], targetFolderPath: 'Indesirables', copy: false })
  })

  // Same rule the other role-backed buttons follow: disabled with its reason, never withheld,
  // because a button that vanishes on some folders reads as a rendering fault.
  it('disables junk with its reason where no folder holds the role', async () => {
    mocks.folders = roleTree
    renderList({}, { 'mail.rowActions': 'junk' })

    const junk = await within(rowOf(/alice martin/i)).findByRole('button', { name: 'Report as junk' })
    expect(junk).toBeDisabled()
    expect(junk).toHaveAttribute('title', 'Assign the junk folder in Settings → Folders')
  })

  // waitFor, not settle(): these assert a transition — the preference arrives and the cluster
  // is rebuilt — where settle() is the tool for asserting that nothing happened. One macrotask
  // was enough on a developer's machine and not on CI, which is what a one-tick wait buys you.
  it('drops an icon the account switched off', async () => {
    renderList({}, { 'mail.rowActions': 'seen,archive' })

    await waitFor(() =>
      expect(within(rowOf(/bob@x\.be/i)).queryByRole('button', { name: 'Delete' })).toBeNull())
    expect(within(rowOf(/bob@x\.be/i)).getByRole('button', { name: 'Archive' })).toBeInTheDocument()
  })

  // The row is re-queried inside the wait on purpose, and the cluster read from it rather than
  // from the document: the selection toolbar carries a "Report as junk" of its own, so an
  // unscoped query answers from it while the row still shows the default three.
  it('renders in the canonical order whatever order it was stored in', async () => {
    renderList({}, { 'mail.rowActions': 'delete,junk,seen' })

    await waitFor(() => {
      const cluster = rowOf(/bob@x\.be/i).querySelector('.message-row-cluster') as HTMLElement
      expect([...cluster.querySelectorAll('button')].map(button => button.getAttribute('aria-label')))
        .toEqual(['Mark as unread', 'Report as junk', 'Delete'])
    })
  })

  // Zero is a real choice, so the cluster goes rather than collapsing to an empty box that would
  // still eat its reserved width. The star is a flag, not an action, and stays.
  it('drops the cluster entirely when every icon is off', async () => {
    renderList({}, { 'mail.rowActions': '' })

    await waitFor(() => expect(document.querySelector('.message-row-cluster')).toBeNull())
    expect(within(rowOf(/bob@x\.be/i)).getByRole('button', { name: 'Star' })).toBeInTheDocument()
  })
})
