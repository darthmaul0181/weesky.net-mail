import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import FolderManager from './FolderManager'
import type { MailFolderNode } from '../api/mailTypes'

const mocks = vi.hoisted(() => ({
  rename: vi.fn(),
  remove: vi.fn(),
  subscribe: vi.fn(),
}))

vi.mock('../queries', () => ({
  useRenameFolder: () => ({ mutateAsync: mocks.rename, isPending: false }),
  useDeleteFolder: () => ({ mutateAsync: mocks.remove, isPending: false }),
  useSetFolderSubscription: () => ({ mutateAsync: mocks.subscribe, isPending: false }),
}))

function node(partial: Partial<MailFolderNode>): MailFolderNode {
  return {
    path: 'X', name: 'X', specialUse: null, selectable: true, subscribed: true,
    total: 0, unread: 0, uidValidity: 1, children: [], ...partial,
  }
}

const tree: MailFolderNode[] = [
  node({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox' }),
  // A system folder that is not the inbox: the inbox is locked for its own reasons, so on its
  // own it cannot show that the rule covers every role.
  node({ path: 'Corbeille', name: 'Corbeille', specialUse: 'trash' }),
  node({
    path: 'Projects', name: 'Projects',
    children: [node({ path: 'Projects/Alpha', name: 'Alpha', subscribed: false })],
  }),
]

function renderManager(onNotify = vi.fn()) {
  const view = render(<FolderManager folders={tree} onNotify={onNotify} />)
  return { ...view, onNotify }
}

describe('the folder list', () => {
  beforeEach(() => vi.clearAllMocks())

  it('lists every folder, children included', () => {
    renderManager()

    expect(screen.getByLabelText('Show INBOX')).toBeInTheDocument()
    expect(screen.getByLabelText('Show Projects')).toBeInTheDocument()
    expect(screen.getByLabelText('Show Alpha')).toBeInTheDocument()
  })

  it('toggles visibility with the inverted state', async () => {
    mocks.subscribe.mockResolvedValue(undefined)
    renderManager()

    fireEvent.click(screen.getByLabelText('Show Projects'))

    await waitFor(() =>
      expect(mocks.subscribe).toHaveBeenCalledWith({ path: 'Projects', subscribed: false }))
  })

  it('shows an unsubscribed folder as unchecked', () => {
    renderManager()

    expect(screen.getByLabelText('Show Alpha')).not.toBeChecked()
  })

  // Regression, same root cause as the tree: Dovecot reports INBOX unsubscribed, so the
  // checkbox rendered unchecked and invited the user to "show" a folder that is always shown.
  it('shows the inbox as always visible', () => {
    render(
      <FolderManager
        folders={[node({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox', subscribed: false })]}
        onNotify={vi.fn()}
      />)

    expect(screen.getByLabelText('Show INBOX')).toBeChecked()
  })

  // A switch, not a bare checkbox: the app shows every boolean this way.
  it('uses the house toggle switch for visibility', () => {
    const { container } = renderManager()

    expect(container.querySelectorAll('.toggle-switch')).toHaveLength(4)
    expect(screen.getByLabelText('Show Projects').closest('.toggle-switch')).toBeTruthy()
  })
})

describe('system folders are locked', () => {
  beforeEach(() => vi.clearAllMocks())

  // Hiding a system folder strands the mail filed into it; renaming or deleting one breaks the
  // role for every other client on the mailbox. The role is changed in the system-folders
  // dialog, so this list offers nothing at all on those rows.
  it.each([
    ['INBOX', 'Inbox'],
    ['Corbeille', 'Trash'],
  ])('offers no action on the %s folder and names its role', (name, label) => {
    renderManager()

    expect(screen.queryByRole('button', { name: `Rename ${name}` })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: `Delete ${name}` })).not.toBeInTheDocument()
    expect(screen.getByLabelText(`Show ${name}`)).toBeDisabled()
    expect(screen.getByText(label)).toBeInTheDocument()
  })

  it('keeps every action on an ordinary folder', () => {
    renderManager()

    expect(screen.getByRole('button', { name: 'Rename Projects' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Delete Projects' })).toBeInTheDocument()
    expect(screen.getByLabelText('Show Projects')).toBeEnabled()
  })

  // The lock has to be visible, not only enforced: an unexplained missing control reads as a bug.
  it('marks the system rows so they read as different', () => {
    const { container } = renderManager()

    expect(container.querySelectorAll('.folder-manage-row.is-system')).toHaveLength(2)
  })
})

describe('renaming a folder', () => {
  beforeEach(() => vi.clearAllMocks())

  it('renames through the parent derived from the path', async () => {
    mocks.rename.mockResolvedValue('Projects/Beta')
    renderManager()
    fireEvent.click(screen.getByRole('button', { name: 'Rename Alpha' }))

    fireEvent.change(screen.getByLabelText('New name'), { target: { value: 'Beta' } })
    fireEvent.click(screen.getByRole('button', { name: 'Rename' }))

    await waitFor(() => expect(mocks.rename).toHaveBeenCalledWith({
      path: 'Projects/Alpha', newParentPath: 'Projects', newName: 'Beta',
    }))
  })

  it('closes the rename dialog on the ✕ without renaming', () => {
    renderManager()
    fireEvent.click(screen.getByRole('button', { name: 'Rename Projects' }))

    fireEvent.click(screen.getByRole('button', { name: 'Close' }))

    expect(mocks.rename).not.toHaveBeenCalled()
    expect(screen.queryByLabelText('New name')).not.toBeInTheDocument()
  })
})

describe('deleting a folder', () => {
  beforeEach(() => vi.clearAllMocks())

  it('asks for confirmation before deleting', () => {
    renderManager()
    fireEvent.click(screen.getByRole('button', { name: 'Delete Projects' }))

    expect(mocks.remove).not.toHaveBeenCalled()
    // The modal's confirm button is what distinguishes it from the row that opened it.
    expect(screen.getByRole('button', { name: /^Delete$/ })).toBeInTheDocument()
  })

  it('deletes once confirmed', async () => {
    mocks.remove.mockResolvedValue(undefined)
    const { onNotify } = renderManager()
    fireEvent.click(screen.getByRole('button', { name: 'Delete Projects' }))

    fireEvent.click(screen.getByRole('button', { name: /^Delete$/ }))

    await waitFor(() => expect(mocks.remove).toHaveBeenCalledWith({ path: 'Projects' }))
    expect(onNotify).toHaveBeenCalledWith('Folder "Projects" deleted')
  })
})
