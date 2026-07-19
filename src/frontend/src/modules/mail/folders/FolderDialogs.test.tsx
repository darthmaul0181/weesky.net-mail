import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
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
  node({
    path: 'Projects', name: 'Projects',
    children: [node({ path: 'Projects/Alpha', name: 'Alpha', subscribed: false })],
  }),
]

function renderDialogs(onNotify = vi.fn()) {
  render(<FolderDialogs folders={tree} selectedPath="INBOX" onNotify={onNotify} />)
  return onNotify
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
      .toEqual([['INBOX', 0], ['Projects', 0], ['Alpha', 1]])
  })
})

describe('creating a folder', () => {
  beforeEach(() => vi.clearAllMocks())

  it('keeps Create disabled until a name is typed', () => {
    renderDialogs()
    fireEvent.click(screen.getByRole('button', { name: 'New folder' }))

    expect(screen.getByRole('button', { name: 'Create' })).toBeDisabled()
  })

  it('creates under the chosen parent and notifies', async () => {
    mocks.create.mockResolvedValue('INBOX/Reports')
    const onNotify = renderDialogs()

    fireEvent.click(screen.getByRole('button', { name: 'New folder' }))
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Reports' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create' }))

    await waitFor(() =>
      expect(mocks.create).toHaveBeenCalledWith({ parentPath: 'INBOX', name: 'Reports' }))
    expect(onNotify).toHaveBeenCalledWith('Folder "Reports" created')
  })

  it('surfaces the backend message when creation fails', async () => {
    mocks.create.mockRejectedValue(new Error("A folder name cannot be empty or contain '/'"))
    const onNotify = renderDialogs()

    fireEvent.click(screen.getByRole('button', { name: 'New folder' }))
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'a/b' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create' }))

    await waitFor(() =>
      expect(onNotify).toHaveBeenCalledWith("A folder name cannot be empty or contain '/'", 'error'))
  })
})

describe('managing folders', () => {
  beforeEach(() => vi.clearAllMocks())

  it('toggles visibility with the inverted state', async () => {
    mocks.subscribe.mockResolvedValue(undefined)
    renderDialogs()
    fireEvent.click(screen.getByRole('button', { name: 'Manage' }))

    fireEvent.click(screen.getByLabelText('Show Projects'))

    await waitFor(() =>
      expect(mocks.subscribe).toHaveBeenCalledWith({ path: 'Projects', subscribed: false }))
  })

  it('shows an unsubscribed folder as unchecked', () => {
    renderDialogs()
    fireEvent.click(screen.getByRole('button', { name: 'Manage' }))

    expect(screen.getByLabelText('Show Alpha')).not.toBeChecked()
  })

  // Regression, same root cause as the tree: Dovecot reports INBOX unsubscribed, so the
  // checkbox rendered unchecked and invited the user to "show" a folder that is always shown —
  // or worse, to unsubscribe it.
  it('shows the inbox as always visible and refuses to toggle it', () => {
    render(
      <FolderDialogs
        folders={[node({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox', subscribed: false })]}
        selectedPath={null}
        onNotify={vi.fn()}
      />)
    fireEvent.click(screen.getByRole('button', { name: 'Manage' }))

    const toggle = screen.getByLabelText('Show INBOX')
    expect(toggle).toBeChecked()
    expect(toggle).toBeDisabled()
  })

  it('offers no Delete for the inbox', () => {
    renderDialogs()
    fireEvent.click(screen.getByRole('button', { name: 'Manage' }))

    expect(screen.queryByRole('button', { name: 'Delete INBOX' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Delete Projects' })).toBeInTheDocument()
  })

  it('renames through the parent derived from the path', async () => {
    mocks.rename.mockResolvedValue('Projects/Beta')
    renderDialogs()
    fireEvent.click(screen.getByRole('button', { name: 'Manage' }))
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
    fireEvent.click(screen.getByRole('button', { name: 'Manage' }))
    fireEvent.click(screen.getByRole('button', { name: 'Delete Projects' }))

    expect(mocks.remove).not.toHaveBeenCalled()
    // The modal's confirm button is what distinguishes it from the manage row that opened it.
    expect(screen.getByRole('button', { name: /^Delete$/ })).toBeInTheDocument()
  })

  it('deletes once confirmed', async () => {
    mocks.remove.mockResolvedValue(undefined)
    const onNotify = renderDialogs()
    fireEvent.click(screen.getByRole('button', { name: 'Manage' }))
    fireEvent.click(screen.getByRole('button', { name: 'Delete Projects' }))

    fireEvent.click(screen.getByRole('button', { name: /^Delete$/ }))

    await waitFor(() => expect(mocks.remove).toHaveBeenCalledWith({ path: 'Projects' }))
    expect(onNotify).toHaveBeenCalledWith('Folder "Projects" deleted')
  })
})
