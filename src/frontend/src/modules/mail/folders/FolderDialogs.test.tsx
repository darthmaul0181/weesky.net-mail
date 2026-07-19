import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import FolderDialogs, { flatten, parentOf } from './FolderDialogs'
import type { MailFolderNode } from '../api/mailTypes'

const mocks = vi.hoisted(() => ({
  create: vi.fn(),
  rename: vi.fn(),
  remove: vi.fn(),
  subscribe: vi.fn(),
}))

vi.mock('../queries', () => ({
  useCreateFolder: () => ({ mutateAsync: mocks.create, isPending: false }),
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

function renderDialogs(onNotify = vi.fn()) {
  render(<MemoryRouter><FolderDialogs folders={tree} selectedPath="INBOX" onNotify={onNotify} /></MemoryRouter>)
  return onNotify
}

/** Both actions are icon buttons in the folder column's footer, named by their aria-label. */
function openCreate() {
  fireEvent.click(screen.getByRole('button', { name: 'New folder' }))
}

function openManage() {
  fireEvent.click(screen.getByRole('button', { name: 'Manage folders' }))
}

describe('parentOf', () => {
  it('strips the leaf name whatever the separator', () => {
    expect(parentOf(node({ path: 'INBOX/Projects', name: 'Projects' }))).toBe('INBOX')
    expect(parentOf(node({ path: 'INBOX.Projects', name: 'Projects' }))).toBe('INBOX')
  })

  it('returns empty for a top-level folder', () => {
    expect(parentOf(node({ path: 'INBOX', name: 'INBOX' }))).toBe('')
  })
})

describe('flatten', () => {
  it('includes children with their depth', () => {
    expect(flatten(tree).map(f => [f.node.name, f.depth]))
      .toEqual([['INBOX', 0], ['Corbeille', 0], ['Projects', 0], ['Alpha', 1]])
  })
})

describe('the folder actions', () => {
  beforeEach(() => vi.clearAllMocks())

  // The two actions are icons in the column footer now. They carry no visible text, so the
  // accessible name is the only name they have — losing it would leave them unreachable to a
  // screen reader and unfindable to these tests alike.
  it('names both icon actions', () => {
    renderDialogs()

    expect(screen.getByRole('button', { name: 'New folder' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Manage folders' })).toBeInTheDocument()
  })

  it('opens nothing until an action is clicked', () => {
    renderDialogs()

    expect(screen.queryByRole('button', { name: 'Create folder' })).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Show Projects')).not.toBeInTheDocument()
  })
})

describe('creating a folder', () => {
  beforeEach(() => vi.clearAllMocks())

  // Closing is the ✕ alone, as in the admin dialogs — no separate Cancel button.
  it('closes without creating anything', () => {
    renderDialogs()
    openCreate()

    fireEvent.click(screen.getByRole('button', { name: 'Close' }))

    expect(mocks.create).not.toHaveBeenCalled()
    expect(screen.queryByRole('button', { name: 'Create folder' })).not.toBeInTheDocument()
  })

  // The dialog is built from the admin module's parts, so a webmail does not end up with two
  // dialects of form. .field-h is the horizontal label/control row those dialogs use.
  it('lays its fields out the way the admin dialogs do', () => {
    const { container } = render(
      <FolderDialogs folders={tree} selectedPath="INBOX" onNotify={vi.fn()} />)
    openCreate()

    expect(container.querySelectorAll('.field-h')).toHaveLength(2)
    // Labels sit beside their control, so the association has to be explicit to survive.
    expect(screen.getByLabelText('Name')).toHaveAttribute('id', 'new-folder-name')
    expect(screen.getByLabelText('Parent')).toHaveAttribute('id', 'new-folder-parent')
  })

  it('submits on Enter, like every other form in the app', async () => {
    mocks.create.mockResolvedValue('Reports')
    const { container } = render(
      <FolderDialogs folders={tree} selectedPath="INBOX" onNotify={vi.fn()} />)
    openCreate()

    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Reports' } })
    fireEvent.submit(container.querySelector('form')!)

    await waitFor(() =>
      expect(mocks.create).toHaveBeenCalledWith({ parentPath: 'INBOX', name: 'Reports' }))
  })

  it('keeps Create disabled until a name is typed', () => {
    renderDialogs()
    openCreate()

    expect(screen.getByRole('button', { name: 'Create folder' })).toBeDisabled()
  })

  it('creates under the chosen parent and notifies', async () => {
    mocks.create.mockResolvedValue('INBOX/Reports')
    const onNotify = renderDialogs()

    openCreate()
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Reports' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create folder' }))

    await waitFor(() =>
      expect(mocks.create).toHaveBeenCalledWith({ parentPath: 'INBOX', name: 'Reports' }))
    expect(onNotify).toHaveBeenCalledWith('Folder "Reports" created')
  })

  it('surfaces the backend message when creation fails', async () => {
    mocks.create.mockRejectedValue(new Error("A folder name cannot be empty or contain '/'"))
    const onNotify = renderDialogs()

    openCreate()
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'a/b' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create folder' }))

    await waitFor(() =>
      expect(onNotify).toHaveBeenCalledWith("A folder name cannot be empty or contain '/'", 'error'))
  })
})

describe('managing folders', () => {
  beforeEach(() => vi.clearAllMocks())

  it('lists every folder, children included, once opened', () => {
    renderDialogs()
    openManage()

    expect(screen.getByLabelText('Show INBOX')).toBeInTheDocument()
    expect(screen.getByLabelText('Show Projects')).toBeInTheDocument()
    expect(screen.getByLabelText('Show Alpha')).toBeInTheDocument()
  })

  it('closes on the close button', () => {
    renderDialogs()
    openManage()

    fireEvent.click(screen.getByRole('button', { name: 'Close' }))

    expect(screen.queryByLabelText('Show Projects')).not.toBeInTheDocument()
  })

  it('toggles visibility with the inverted state', async () => {
    mocks.subscribe.mockResolvedValue(undefined)
    renderDialogs()
    openManage()

    fireEvent.click(screen.getByLabelText('Show Projects'))

    await waitFor(() =>
      expect(mocks.subscribe).toHaveBeenCalledWith({ path: 'Projects', subscribed: false }))
  })

  it('shows an unsubscribed folder as unchecked', () => {
    renderDialogs()
    openManage()

    expect(screen.getByLabelText('Show Alpha')).not.toBeChecked()
  })

  // Regression, same root cause as the tree: Dovecot reports INBOX unsubscribed, so the
  // checkbox rendered unchecked and invited the user to "show" a folder that is always shown —
  // or worse, to unsubscribe it.
  it('shows the inbox as always visible and refuses to toggle it', () => {
    render(
      <MemoryRouter>
        <FolderDialogs
          folders={[node({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox', subscribed: false })]}
          selectedPath={null}
          onNotify={vi.fn()}
        />
      </MemoryRouter>)
    openManage()

    const toggle = screen.getByLabelText('Show INBOX')
    expect(toggle).toBeChecked()
    expect(toggle).toBeDisabled()
  })

  // A switch, not a bare checkbox: the app shows every boolean this way.
  it('uses the house toggle switch for visibility', () => {
    const { container } = render(
      <MemoryRouter><FolderDialogs folders={tree} selectedPath="INBOX" onNotify={vi.fn()} /></MemoryRouter>)
    openManage()

    expect(container.querySelectorAll('.toggle-switch')).toHaveLength(flatten(tree).length)
    expect(screen.getByLabelText('Show Projects').closest('.toggle-switch')).toBeTruthy()
  })

  // Hiding a system folder strands the mail filed into it; renaming or deleting one breaks the
  // role for every other client on the mailbox. The role is changed on the system-folders page,
  // so this dialog offers nothing at all on those rows.
  it.each([
    ['INBOX', 'Inbox'],
    ['Corbeille', 'Trash'],
  ])('offers no action on the %s folder and names its role', (name, label) => {
    renderDialogs()
    openManage()

    expect(screen.queryByRole('button', { name: `Rename ${name}` })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: `Delete ${name}` })).not.toBeInTheDocument()
    expect(screen.getByLabelText(`Show ${name}`)).toBeDisabled()
    expect(screen.getByText(label)).toBeInTheDocument()
  })

  it('keeps every action on an ordinary folder', () => {
    renderDialogs()
    openManage()

    expect(screen.getByRole('button', { name: 'Rename Projects' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Delete Projects' })).toBeInTheDocument()
    expect(screen.getByLabelText('Show Projects')).toBeEnabled()
  })

  // The lock has to be visible, not only enforced: an unexplained missing control reads as a bug.
  it('marks the system rows so they read as different', () => {
    const { container } = render(
      <MemoryRouter><FolderDialogs folders={tree} selectedPath="INBOX" onNotify={vi.fn()} /></MemoryRouter>)
    openManage()

    expect(container.querySelectorAll('.folder-manage-row.is-system')).toHaveLength(2)
  })

  it('links the manage dialog to the system-folders settings', () => {
    renderDialogs()
    openManage()

    const link = screen.getByRole('link', { name: /system folders/i })
    expect(link).toHaveAttribute('href', '/settings/system-folders')
  })

  it('renames through the parent derived from the path', async () => {
    mocks.rename.mockResolvedValue('Projects/Beta')
    renderDialogs()
    openManage()
    fireEvent.click(screen.getByRole('button', { name: 'Rename Alpha' }))

    fireEvent.change(screen.getByLabelText('New name'), { target: { value: 'Beta' } })
    fireEvent.click(screen.getByRole('button', { name: 'Rename' }))

    await waitFor(() => expect(mocks.rename).toHaveBeenCalledWith({
      path: 'Projects/Alpha', newParentPath: 'Projects', newName: 'Beta',
    }))
  })
})

describe('deleting a folder', () => {
  beforeEach(() => vi.clearAllMocks())

  it('asks for confirmation before deleting', () => {
    renderDialogs()
    openManage()
    fireEvent.click(screen.getByRole('button', { name: 'Delete Projects' }))

    expect(mocks.remove).not.toHaveBeenCalled()
    // The modal's confirm button is what distinguishes it from the manage row that opened it.
    expect(screen.getByRole('button', { name: /^Delete$/ })).toBeInTheDocument()
  })

  it('deletes once confirmed', async () => {
    mocks.remove.mockResolvedValue(undefined)
    const onNotify = renderDialogs()
    openManage()
    fireEvent.click(screen.getByRole('button', { name: 'Delete Projects' }))

    fireEvent.click(screen.getByRole('button', { name: /^Delete$/ }))

    await waitFor(() => expect(mocks.remove).toHaveBeenCalledWith({ path: 'Projects' }))
    expect(onNotify).toHaveBeenCalledWith('Folder "Projects" deleted')
  })
})
