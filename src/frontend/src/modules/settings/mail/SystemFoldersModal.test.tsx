import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import SystemFoldersModal from './SystemFoldersModal'
import type { FolderRoleEntry, MailFolderNode } from '../../mail/api/mailTypes'

const mocks = vi.hoisted(() => ({
  getMailFolders: vi.fn(),
  getFolderRoles: vi.fn(),
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
    total: 0, unread: 0, uidValidity: 1, uidNext: null, highestModSeq: null, children: [], ...partial,
  }
}

function entry(partial: Partial<FolderRoleEntry> & { role: string }): FolderRoleEntry {
  return { folderPath: null, provenance: null, staleOverride: null, ...partial }
}

const folders = [
  node({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox' }),
  node({ path: 'Deleted Items', name: 'Deleted Items', specialUse: 'trash' }),
  node({ path: 'Corbeille', name: 'Corbeille' }),
  node({ path: 'Container', name: 'Container', selectable: false }),
]

const roles = [
  entry({ role: 'sent' }),
  entry({ role: 'drafts' }),
  entry({ role: 'trash', folderPath: 'Deleted Items', provenance: 'specialUse' }),
  entry({ role: 'junk' }),
  entry({ role: 'archive' }),
]

const onNotify = vi.fn()
const onClose = vi.fn()

function renderModal() {
  mocks.getMailFolders.mockResolvedValue(folders)
  mocks.getFolderRoles.mockResolvedValue(roles)
  return render(<SystemFoldersModal onClose={onClose} onNotify={onNotify} />, { wrapper })
}

describe('SystemFoldersModal', () => {
  beforeEach(() => vi.clearAllMocks())

  it('offers one labelled select per assignable role', async () => {
    renderModal()

    expect(await screen.findByLabelText('Sent')).toBeInTheDocument()
    expect(screen.getByLabelText('Drafts')).toBeInTheDocument()
    expect(screen.getByLabelText('Trash')).toBeInTheDocument()
    expect(screen.getByLabelText('Junk')).toBeInTheDocument()
    expect(screen.getByLabelText('Archive')).toBeInTheDocument()
    // Inbox is fixed by the protocol: no select for it.
    expect(screen.queryByLabelText('Inbox')).not.toBeInTheDocument()
  })

  it('says what automatic currently resolves to', async () => {
    renderModal()

    const trash = await screen.findByLabelText('Trash')
    expect(trash).toHaveDisplayValue(/Automatic — Deleted Items/)
  })

  it('shows an override as the selected folder', async () => {
    mocks.getMailFolders.mockResolvedValue(folders)
    mocks.getFolderRoles.mockResolvedValue([
      ...roles.filter(r => r.role !== 'trash'),
      entry({ role: 'trash', folderPath: 'Corbeille', provenance: 'override' }),
    ])
    render(<SystemFoldersModal onClose={onClose} onNotify={onNotify} />, { wrapper })

    expect(await screen.findByLabelText('Trash')).toHaveValue('Corbeille')
  })

  it('assigns a role through the API', async () => {
    mocks.setFolderRole.mockResolvedValue(undefined)
    renderModal()

    fireEvent.change(await screen.findByLabelText('Trash'), { target: { value: 'Corbeille' } })

    await waitFor(() => expect(mocks.setFolderRole).toHaveBeenCalledWith('trash', 'Corbeille'))
  })

  it('clears a role when Automatic is chosen', async () => {
    mocks.clearFolderRole.mockResolvedValue(undefined)
    mocks.getMailFolders.mockResolvedValue(folders)
    mocks.getFolderRoles.mockResolvedValue([
      ...roles.filter(r => r.role !== 'trash'),
      entry({ role: 'trash', folderPath: 'Corbeille', provenance: 'override' }),
    ])
    render(<SystemFoldersModal onClose={onClose} onNotify={onNotify} />, { wrapper })

    fireEvent.change(await screen.findByLabelText('Trash'), { target: { value: '' } })

    await waitFor(() => expect(mocks.clearFolderRole).toHaveBeenCalledWith('trash'))
  })

  it('surfaces the backend message when the assignment fails', async () => {
    mocks.setFolderRole.mockRejectedValue(new Error('This folder already holds another role'))
    renderModal()

    fireEvent.change(await screen.findByLabelText('Trash'), { target: { value: 'Corbeille' } })

    await waitFor(() => expect(onNotify).toHaveBeenCalledWith('This folder already holds another role', 'error'))
  })

  it('surfaces the backend message when clearing a role fails', async () => {
    mocks.clearFolderRole.mockRejectedValue(new Error('Could not clear this role'))
    mocks.getMailFolders.mockResolvedValue(folders)
    mocks.getFolderRoles.mockResolvedValue([
      ...roles.filter(r => r.role !== 'trash'),
      entry({ role: 'trash', folderPath: 'Corbeille', provenance: 'override' }),
    ])
    render(<SystemFoldersModal onClose={onClose} onNotify={onNotify} />, { wrapper })

    fireEvent.change(await screen.findByLabelText('Trash'), { target: { value: '' } })

    await waitFor(() => expect(onNotify).toHaveBeenCalledWith('Could not clear this role', 'error'))
  })

  // A stale override is kept and signalled (§ 5.3) — the notice and the discovery-resolved
  // value coexist on screen.
  it('signals an invalidated choice next to what resolution now yields', async () => {
    mocks.getMailFolders.mockResolvedValue(folders)
    mocks.getFolderRoles.mockResolvedValue([
      ...roles.filter(r => r.role !== 'trash'),
      entry({
        role: 'trash', folderPath: 'Deleted Items', provenance: 'specialUse',
        staleOverride: { folderPath: 'Old Trash', reason: 'missing' },
      }),
    ])
    render(<SystemFoldersModal onClose={onClose} onNotify={onNotify} />, { wrapper })

    expect(await screen.findByText(/“Old Trash” was renamed or deleted/)).toBeInTheDocument()
    expect(screen.getByLabelText('Trash')).toHaveDisplayValue(/Automatic — Deleted Items/)
  })

  // The stale notice and the resolved value key off independent fields on the entry, so a
  // role can be stale with *nothing* currently resolved. The notice must still show, and must
  // not be mistaken for (or hide) a resolved value that doesn't exist.
  it('signals an invalidated choice even when automatic resolves to nothing', async () => {
    mocks.getMailFolders.mockResolvedValue(folders)
    mocks.getFolderRoles.mockResolvedValue([
      ...roles.filter(r => r.role !== 'trash'),
      entry({
        role: 'trash', folderPath: null, provenance: null,
        staleOverride: { folderPath: 'Old Trash', reason: 'missing' },
      }),
    ])
    render(<SystemFoldersModal onClose={onClose} onNotify={onNotify} />, { wrapper })

    expect(await screen.findByText(/“Old Trash” was renamed or deleted/)).toBeInTheDocument()
    expect(screen.getByLabelText('Trash')).toHaveDisplayValue('Automatic — not set')
  })

  // Fix for the accessibility gap: DOM adjacency alone doesn't announce the notice to a
  // screen-reader user tabbing through the fields — it must be wired via aria-describedby.
  it('associates the stale notice with its select via aria-describedby', async () => {
    mocks.getMailFolders.mockResolvedValue(folders)
    mocks.getFolderRoles.mockResolvedValue([
      ...roles.filter(r => r.role !== 'trash'),
      entry({
        role: 'trash', folderPath: 'Deleted Items', provenance: 'specialUse',
        staleOverride: { folderPath: 'Old Trash', reason: 'missing' },
      }),
    ])
    render(<SystemFoldersModal onClose={onClose} onNotify={onNotify} />, { wrapper })

    const select = await screen.findByLabelText('Trash')
    const notice = await screen.findByText(/“Old Trash” was renamed or deleted/)

    expect(select).toHaveAttribute('aria-describedby', notice.id)
    expect(notice.id).toBeTruthy()
  })

  // One flag for three causes had the page assert the folder was renamed or deleted in all
  // three. For the other two that statement is simply false about the user's mailbox.
  it('says the folder can no longer hold messages when that is the cause', async () => {
    mocks.getMailFolders.mockResolvedValue(folders)
    mocks.getFolderRoles.mockResolvedValue([
      ...roles.filter(r => r.role !== 'trash'),
      entry({
        role: 'trash', folderPath: 'Deleted Items', provenance: 'specialUse',
        staleOverride: { folderPath: 'Container', reason: 'notSelectable' },
      }),
    ])
    render(<SystemFoldersModal onClose={onClose} onNotify={onNotify} />, { wrapper })

    expect(await screen.findByText(/“Container” can no longer hold messages/)).toBeInTheDocument()
    expect(screen.queryByText(/renamed or deleted/)).not.toBeInTheDocument()
  })

  it('says the folder is taken by another role when that is the cause', async () => {
    mocks.getMailFolders.mockResolvedValue(folders)
    mocks.getFolderRoles.mockResolvedValue([
      ...roles.filter(r => r.role !== 'junk'),
      entry({
        role: 'junk', folderPath: null, provenance: null,
        staleOverride: { folderPath: 'Corbeille', reason: 'folderTaken' },
      }),
    ])
    render(<SystemFoldersModal onClose={onClose} onNotify={onNotify} />, { wrapper })

    expect(await screen.findByText(/“Corbeille” is already used for another role/)).toBeInTheDocument()
    expect(screen.queryByText(/renamed or deleted/)).not.toBeInTheDocument()
  })

  // Every cause keeps the accessibility wiring, not just the one the first version handled.
  it('associates a non-missing stale notice with its select too', async () => {
    mocks.getMailFolders.mockResolvedValue(folders)
    mocks.getFolderRoles.mockResolvedValue([
      ...roles.filter(r => r.role !== 'trash'),
      entry({
        role: 'trash', folderPath: 'Deleted Items', provenance: 'specialUse',
        staleOverride: { folderPath: 'Container', reason: 'notSelectable' },
      }),
    ])
    render(<SystemFoldersModal onClose={onClose} onNotify={onNotify} />, { wrapper })

    const select = await screen.findByLabelText('Trash')
    const notice = await screen.findByText(/“Container” can no longer hold messages/)

    expect(select).toHaveAttribute('aria-describedby', notice.id)
    expect(notice.id).toBeTruthy()
  })

  // "The server declared this" and "we guessed from the name" are the distinction this page
  // exists to draw; rendering both as a bare "Automatic — X" threw it away at the last step.
  it('marks a name-matched role as a guess', async () => {
    mocks.getMailFolders.mockResolvedValue(folders)
    mocks.getFolderRoles.mockResolvedValue([
      ...roles.filter(r => r.role !== 'trash'),
      entry({ role: 'trash', folderPath: 'Corbeille', provenance: 'name' }),
    ])
    render(<SystemFoldersModal onClose={onClose} onNotify={onNotify} />, { wrapper })

    expect(await screen.findByLabelText('Trash'))
      .toHaveDisplayValue('Automatic — Corbeille (detected from the name)')
  })

  it('leaves a server-declared role unqualified', async () => {
    renderModal()

    // 'Deleted Items' arrives with provenance 'specialUse'.
    expect(await screen.findByLabelText('Trash')).toHaveDisplayValue('Automatic — Deleted Items')
  })

  it('never offers the inbox or a non-selectable folder', async () => {
    renderModal()

    const options = Array.from((await screen.findByLabelText('Trash')).querySelectorAll('option'))
      .map(option => option.getAttribute('value'))

    expect(options).not.toContain('INBOX')
    expect(options).not.toContain('Container')
    expect(options).toContain('Corbeille')
  })

  it('excludes a folder already overridden for another role, but keeps its own', async () => {
    mocks.getMailFolders.mockResolvedValue(folders)
    mocks.getFolderRoles.mockResolvedValue([
      ...roles.filter(r => r.role !== 'junk'),
      entry({ role: 'junk', folderPath: 'Corbeille', provenance: 'override' }),
    ])
    render(<SystemFoldersModal onClose={onClose} onNotify={onNotify} />, { wrapper })

    const trashOptions = Array.from((await screen.findByLabelText('Trash')).querySelectorAll('option'))
      .map(option => option.getAttribute('value'))
    const junkOptions = Array.from(screen.getByLabelText('Junk').querySelectorAll('option'))
      .map(option => option.getAttribute('value'))

    expect(trashOptions).not.toContain('Corbeille')   // taken by junk
    expect(junkOptions).toContain('Corbeille')        // its own override stays choosable
  })
})
