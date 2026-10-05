import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import SystemFoldersModal from './SystemFoldersModal'
import type { FolderRoleEntry, MailFolderNode } from '../../mail/api/mailTypes'
import { createTestQueryClient, optionsOf, pickOption } from '../../../test-utils'

const mocks = vi.hoisted(() => ({
  getMailFolders: vi.fn(),
  getFolderRoles: vi.fn(),
  setFolderRole: vi.fn(),
  clearFolderRole: vi.fn(),
}))

vi.mock('../../../api.js', () => ({ api: mocks }))
vi.mock('../../../contexts/AuthContext', () => import('../../../test-auth'))

function wrapper({ children }: { children: ReactNode }) {
  const client = createTestQueryClient()
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

function node(partial: Partial<MailFolderNode>): MailFolderNode {
  return {
    path: 'X', name: 'X', selectable: true, subscribed: true,
    total: 0, unread: 0, uidValidity: 1, children: [], ...partial,
  }
}

function entry(partial: Partial<FolderRoleEntry> & { role: string }): FolderRoleEntry {
  return partial
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

/** The modal over the stock roles, with one role's entry replaced. */
function renderWithRole(replaced?: FolderRoleEntry) {
  mocks.getMailFolders.mockResolvedValue(folders)
  mocks.getFolderRoles.mockResolvedValue(
    replaced ? [...roles.filter(r => r.role !== replaced.role), replaced] : roles)
  return render(<SystemFoldersModal onClose={onClose} onNotify={onNotify} />, { wrapper })
}

const TRASH_OVERRIDE = entry({ role: 'trash', folderPath: 'Corbeille', provenance: 'override' })

describe('SystemFoldersModal', () => {
  beforeEach(() => vi.clearAllMocks())

  it('offers one labelled select per assignable role', async () => {
    renderWithRole()

    expect(await screen.findByLabelText('Sent')).toBeInTheDocument()
    expect(screen.getByLabelText('Drafts')).toBeInTheDocument()
    expect(screen.getByLabelText('Trash')).toBeInTheDocument()
    expect(screen.getByLabelText('Junk')).toBeInTheDocument()
    expect(screen.getByLabelText('Archive')).toBeInTheDocument()
    // Inbox is fixed by the protocol: no select for it.
    expect(screen.queryByLabelText('Inbox')).not.toBeInTheDocument()
  })

  it('says what automatic currently resolves to', async () => {
    renderWithRole()

    const trash = await screen.findByLabelText('Trash')
    expect(trash).toHaveTextContent(/Automatic — Deleted Items/)
  })

  it('shows an override as the selected folder', async () => {
    renderWithRole(TRASH_OVERRIDE)

    expect(await screen.findByLabelText('Trash')).toHaveTextContent('Corbeille')
  })

  it('assigns a role through the API', async () => {
    mocks.setFolderRole.mockResolvedValue(undefined)
    renderWithRole()

    await pickOption(await screen.findByLabelText('Trash'), 'Corbeille')

    await waitFor(() => expect(mocks.setFolderRole).toHaveBeenCalledWith('trash', 'Corbeille', { accountId: 'primary' }))
  })

  it('clears a role when Automatic is chosen', async () => {
    mocks.clearFolderRole.mockResolvedValue(undefined)
    renderWithRole(TRASH_OVERRIDE)

    await pickOption(await screen.findByLabelText('Trash'), 'Automatic')

    await waitFor(() => expect(mocks.clearFolderRole).toHaveBeenCalledWith('trash', { accountId: 'primary' }))
  })

  // Server prose never reaches the toast; the local fallback does — see apiErrorMessage.
  it.each([
    ['the assignment', undefined, 'Corbeille', () => mocks.setFolderRole.mockRejectedValue(new Error('This folder already holds another role'))],
    ['clearing a role', TRASH_OVERRIDE, 'Automatic', () => mocks.clearFolderRole.mockRejectedValue(new Error('Server refused'))],
  ])('surfaces the local fallback when %s fails', async (_what, replaced, pick, refuse) => {
    refuse()
    renderWithRole(replaced)

    await pickOption(await screen.findByLabelText('Trash'), pick)

    await waitFor(() => expect(onNotify).toHaveBeenCalledWith('Could not save the folder role', 'error'))
  })

  // A stale override is kept and signalled (§ 5.3) — the notice and the discovery-resolved
  // value coexist on screen.
  it('signals an invalidated choice next to what resolution now yields', async () => {
    renderWithRole(entry({
      role: 'trash', folderPath: 'Deleted Items', provenance: 'specialUse',
      staleOverride: { folderPath: 'Old Trash', reason: 'missing' },
    }))

    expect(await screen.findByText(/“Old Trash” was renamed or deleted/)).toBeInTheDocument()
    expect(screen.getByLabelText('Trash')).toHaveTextContent(/Automatic — Deleted Items/)
  })

  // The stale notice and the resolved value key off independent fields on the entry, so a
  // role can be stale with *nothing* currently resolved. The notice must still show, and must
  // not be mistaken for (or hide) a resolved value that doesn't exist.
  it('signals an invalidated choice even when automatic resolves to nothing', async () => {
    renderWithRole(entry({
      role: 'trash',
      staleOverride: { folderPath: 'Old Trash', reason: 'missing' },
    }))

    expect(await screen.findByText(/“Old Trash” was renamed or deleted/)).toBeInTheDocument()
    expect(screen.getByLabelText('Trash')).toHaveTextContent('Automatic — not set')
  })

  // Fix for the accessibility gap: DOM adjacency alone doesn't announce the notice to a
  // screen-reader user tabbing through the fields — it must be wired via aria-describedby. Every
  // cause keeps the wiring, not just the one the first version handled.
  it.each([
    ['missing', 'Old Trash', /“Old Trash” was renamed or deleted/],
    ['notSelectable', 'Container', /“Container” can no longer hold messages/],
  ] as const)('associates the %s stale notice with its select via aria-describedby', async (reason, folderPath, text) => {
    renderWithRole(entry({
      role: 'trash', folderPath: 'Deleted Items', provenance: 'specialUse', staleOverride: { folderPath, reason },
    }))

    const select = await screen.findByLabelText('Trash')
    const notice = await screen.findByText(text)

    expect(select).toHaveAttribute('aria-describedby', notice.id)
    expect(notice.id).toBeTruthy()
  })

  // One flag for three causes had the page assert the folder was renamed or deleted in all
  // three. For the other two that statement is simply false about the user's mailbox.
  it.each([
    ['can no longer hold messages', entry({
      role: 'trash', folderPath: 'Deleted Items', provenance: 'specialUse',
      staleOverride: { folderPath: 'Container', reason: 'notSelectable' },
    }), /“Container” can no longer hold messages/],
    ['is taken by another role', entry({
      role: 'junk', staleOverride: { folderPath: 'Corbeille', reason: 'folderTaken' },
    }), /“Corbeille” is already used for another role/],
  ])('says the folder %s when that is the cause', async (_cause, stale, text) => {
    renderWithRole(stale)

    expect(await screen.findByText(text)).toBeInTheDocument()
    expect(screen.queryByText(/renamed or deleted/)).not.toBeInTheDocument()
  })

  // "The server declared this" and "we guessed from the name" are the distinction this page
  // exists to draw; rendering both as a bare "Automatic — X" threw it away at the last step.
  it('marks a name-matched role as a guess', async () => {
    renderWithRole(entry({ role: 'trash', folderPath: 'Corbeille', provenance: 'name' }))

    expect(await screen.findByLabelText('Trash'))
      .toHaveTextContent('Automatic — Corbeille (detected from the name)')
  })

  it('leaves a server-declared role unqualified', async () => {
    renderWithRole()

    // 'Deleted Items' arrives with provenance 'specialUse'.
    const trash = await screen.findByLabelText('Trash')
    expect(trash).toHaveTextContent('Automatic — Deleted Items')
    expect(trash).not.toHaveTextContent(/detected from the name/)
  })

  it('never offers the inbox or a non-selectable folder', async () => {
    renderWithRole()

    const options = await optionsOf(await screen.findByLabelText('Trash'))

    expect(options).not.toContain('INBOX')
    expect(options).not.toContain('Container')
    expect(options).toContain('Corbeille')
  })

  it('excludes a folder already overridden for another role, but keeps its own', async () => {
    renderWithRole(entry({ role: 'junk', folderPath: 'Corbeille', provenance: 'override' }))

    const trashOptions = await optionsOf(await screen.findByLabelText('Trash'))
    const junkOptions = await optionsOf(screen.getByLabelText('Junk'))

    expect(trashOptions).not.toContain('Corbeille')   // taken by junk
    expect(junkOptions).toContain('Corbeille')        // its own override stays choosable
  })
})
