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
    total: 0, unread: 0, uidValidity: 1, uidNext: null, highestModSeq: null, children: [], ...partial,
  }
}

const tree: MailFolderNode[] = [
  node({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox' }),
  // Not the inbox: it is locked for its own reasons and cannot show the rule covers every role.
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

  // Inbox first, then everything by name, system folders included.
  it('renders in the sorted order, not the order the server sent', () => {
    const { container } = render(
      <FolderManager
        folders={[
          node({ path: 'Zeta', name: 'Zeta' }),
          node({ path: 'Corbeille', name: 'Corbeille', specialUse: 'trash' }),
          node({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox' }),
          node({ path: 'Alpha', name: 'Alpha' }),
        ]}
        onNotify={vi.fn()}
      />)

    const rows = Array.from(container.querySelectorAll('.admin-list-item-email'))
    expect(rows.map(r => r.textContent)).toEqual(['INBOX', 'Alpha', 'Corbeille', 'Zeta'])
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

  // Regression: Dovecot reports INBOX unsubscribed, so the switch rendered off.
  it('shows the inbox as always visible', () => {
    render(
      <FolderManager
        folders={[node({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox', subscribed: false })]}
        onNotify={vi.fn()}
      />)

    expect(screen.getByLabelText('Show INBOX')).toBeChecked()
  })

  // Regression, found on a connected Proximus account: that server keeps no subscriptions, so
  // every role-holding folder rendered its switch off *and* disabled — hidden, with no way to
  // show it. The switch is disabled because such a folder can never be hidden, so off is the
  // one state it must never display.
  it('shows a role-holding folder as always visible', () => {
    render(
      <FolderManager
        folders={[node({ path: 'SentMail', name: 'SentMail', specialUse: 'sent', subscribed: false })]}
        onNotify={vi.fn()}
      />)

    expect(screen.getByLabelText('Show SentMail')).toBeChecked()
    expect(screen.getByLabelText('Show SentMail')).toBeDisabled()
  })

  // Tiles, not bare rows: every list of elements on a page wears the site's tile.
  it('renders each folder as a tile of the shared list', () => {
    const { container } = renderManager()

    expect(container.querySelector('.admin-list.folder-list')).toBeTruthy()
    expect(container.querySelectorAll('.admin-list-item.folder-tile')).toHaveLength(4)
  })

  // The tile steps in with the depth; the label does not, so the actions keep one column.
  it('indents a child tile rather than its name', () => {
    const { container } = renderManager()
    const tiles = Array.from(container.querySelectorAll<HTMLElement>('.folder-tile'))

    const alpha = tiles.find(t => t.textContent?.includes('Alpha'))!
    const projects = tiles.find(t => t.textContent?.includes('Projects'))!

    expect(projects.style.marginLeft).toBe('0px')
    expect(alpha.style.marginLeft).toBe('18px')
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

  // Disabled, not withheld: withheld, those rows are a different shape from the rest.
  it.each([
    ['INBOX', 'Inbox'],
    ['Corbeille', 'Trash'],
  ])('disables every action on the %s folder and names its role', (name, label) => {
    renderManager()

    expect(screen.getByRole('button', { name: `Rename ${name}` })).toBeDisabled()
    expect(screen.getByRole('button', { name: `Delete ${name}` })).toBeDisabled()
    expect(screen.getByLabelText(`Show ${name}`)).toBeDisabled()
    expect(screen.getByText(label)).toBeInTheDocument()
  })

  // Disabled must mean inert: the row still carries a live handler.
  it.each(['Rename Corbeille', 'Delete Corbeille'])('ignores a click on %s', label => {
    renderManager()

    fireEvent.click(screen.getByRole('button', { name: label }))

    expect(screen.queryByLabelText('New name')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Delete$/ })).not.toBeInTheDocument()
  })

  // Tile anatomy: the state control leads on the left, the badge qualifies the name it sits
  // against, and the actions are the rightmost thing on the row.
  it('places the role badge next to the name, before the actions', () => {
    const { container } = renderManager()

    const tile = container.querySelector('.folder-tile')!
    const parts = Array.from(tile.children).map(c => c.className.split(' ')[0])

    expect(parts).toEqual(['toggle-switch', 'admin-list-item-email', 'row-tag', 'admin-list-item-actions'])
  })

  it('keeps every action live on an ordinary folder', () => {
    renderManager()

    expect(screen.getByRole('button', { name: 'Rename Projects' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Delete Projects' })).toBeEnabled()
    expect(screen.getByLabelText('Show Projects')).toBeEnabled()
  })

  // The lock has to be visible, not only enforced: an unexplained missing control reads as a
  // bug. Every tile's name is bold, so the badge is what carries it.
  it('marks the system rows so they read as different', () => {
    const { container } = renderManager()

    expect(container.querySelectorAll('.row-tag')).toHaveLength(2)
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
