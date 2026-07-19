import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import FolderTree from './FolderTree'
import type { MailFolderNode } from '../api/mailTypes'

function node(partial: Partial<MailFolderNode>): MailFolderNode {
  return {
    path: 'X', name: 'X', specialUse: null, selectable: true, subscribed: true,
    total: 0, unread: 0, uidValidity: 1, children: [], ...partial,
  }
}

const tree: MailFolderNode[] = [
  node({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox', unread: 4 }),
  node({
    path: 'Projects', name: 'Projects',
    children: [node({ path: 'Projects/Alpha', name: 'Alpha', unread: 2 })],
  }),
  node({ path: 'Hidden', name: 'Hidden', subscribed: false }),
]

describe('FolderTree', () => {
  it('renders subscribed folders and hides unsubscribed ones', () => {
    render(<FolderTree folders={tree} selectedPath="INBOX" onSelect={vi.fn()} />)

    expect(screen.getByText('Inbox')).toBeInTheDocument()
    expect(screen.getByText('Projects')).toBeInTheDocument()
    expect(screen.queryByText('Hidden')).not.toBeInTheDocument()
  })

  it('shows unread counts only when non-zero', () => {
    render(<FolderTree folders={tree} selectedPath="INBOX" onSelect={vi.fn()} />)

    expect(screen.getByText('4')).toBeInTheDocument()
    expect(screen.queryByText('0')).not.toBeInTheDocument()
  })

  it('marks the selected folder', () => {
    render(<FolderTree folders={tree} selectedPath="INBOX" onSelect={vi.fn()} />)

    // INBOX carries unread: 4 in the fixture, so its accessible name includes the count.
    expect(screen.getByRole('button', { name: 'Inbox, 4 unread' })).toHaveClass('is-active')
  })

  it('calls onSelect with the folder path', () => {
    const onSelect = vi.fn()
    render(<FolderTree folders={tree} selectedPath="INBOX" onSelect={onSelect} />)

    // Exact name: a parent folder also has an "Expand Projects" toggle button.
    fireEvent.click(screen.getByRole('button', { name: 'Projects' }))

    expect(onSelect).toHaveBeenCalledWith('Projects')
  })

  it('hides children until the parent is expanded', () => {
    render(<FolderTree folders={tree} selectedPath="INBOX" onSelect={vi.fn()} />)

    expect(screen.queryByText('Alpha')).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /expand Projects/i }))
    expect(screen.getByText('Alpha')).toBeInTheDocument()
  })

  it('starts with the inbox expanded', () => {
    const withChild = [node({
      path: 'INBOX', name: 'INBOX', specialUse: 'inbox',
      children: [node({ path: 'INBOX/Sub', name: 'Sub' })],
    })]

    render(<FolderTree folders={withChild} selectedPath={null} onSelect={vi.fn()} />)

    expect(screen.getByText('Sub')).toBeInTheDocument()
  })

  it('does not select an unselectable container folder', () => {
    const onSelect = vi.fn()
    render(
      <FolderTree
        folders={[node({ path: 'Container', name: 'Container', selectable: false })]}
        selectedPath={null}
        onSelect={onSelect}
      />)

    fireEvent.click(screen.getByRole('button', { name: /Container/ }))

    expect(onSelect).not.toHaveBeenCalled()
  })

  it('orders well-known folders before ordinary ones', () => {
    const unordered = [
      node({ path: 'Zebra', name: 'Zebra' }),
      node({ path: 'Trash', name: 'Trash', specialUse: 'trash' }),
      node({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox' }),
      node({ path: 'Alpha', name: 'Alpha' }),
    ]

    render(<FolderTree folders={unordered} selectedPath={null} onSelect={vi.fn()} />)

    const names = screen.getAllByRole('button').map(b => b.textContent)
    expect(names).toEqual(['Inbox', 'Trash', 'Alpha', 'Zebra'])
  })

  // The two blocks used to run together, so a well-known folder was distinguishable from an
  // ordinary one only by recognising its name — which fails exactly where it matters, on a
  // mailbox holding both "Drafts" and "Brouillons".
  it("rules off the well-known folders from the user's own", () => {
    const { container } = render(
      <FolderTree
        folders={[
          node({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox' }),
          node({ path: 'Alpha', name: 'Alpha' }),
        ]}
        selectedPath={null}
        onSelect={vi.fn()}
      />)

    expect(container.querySelectorAll('.folder-separator')).toHaveLength(1)
  })

  // A rule under nothing, or above nothing, reads as a rendering fault. A freshly provisioned
  // mailbox with no folders of its own is the common case, not an edge case.
  it.each([
    ['no folders of its own', [node({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox' })]],
    ['no well-known folders', [node({ path: 'Alpha', name: 'Alpha' })]],
  ])('draws no rule when the mailbox has %s', (_, folders) => {
    const { container } = render(
      <FolderTree folders={folders} selectedPath={null} onSelect={vi.fn()} />)

    expect(container.querySelectorAll('.folder-separator')).toHaveLength(0)
  })

  it('marks the well-known rows so they read as a group', () => {
    const { container } = render(
      <FolderTree
        folders={[
          node({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox' }),
          node({ path: 'Trash', name: 'Trash', specialUse: 'trash' }),
          node({ path: 'Alpha', name: 'Alpha' }),
        ]}
        selectedPath={null}
        onSelect={vi.fn()}
      />)

    expect(container.querySelectorAll('.folder-row.is-system')).toHaveLength(2)
  })

  // Below the rule the question is "where is the folder I am looking for", so the answer is the
  // same order the folders list uses — accents included, since a codepoint sort files every one
  // of them after "Z".
  it("orders the user's own folders by name, accents in their place", () => {
    render(
      <FolderTree
        folders={[
          node({ path: 'Zebra', name: 'Zebra' }),
          node({ path: 'Éléments', name: 'Éléments' }),
          node({ path: 'e-commerce', name: 'e-commerce' }),
          node({ path: 'English', name: 'English' }),
        ]}
        selectedPath={null}
        onSelect={vi.fn()}
      />)

    expect(screen.getAllByRole('button').map(b => b.textContent))
      .toEqual(['e-commerce', 'Éléments', 'English', 'Zebra'])
  })

  // Regression: Dovecot reports INBOX as subscribed=false, because the subscription flag is
  // meaningless for a folder that is always available. Filtering on subscription alone hid the
  // inbox entirely. Found against a live server, not by the mocks — every fixture here used to
  // say subscribed: true.
  it('always shows the inbox even when the server reports it unsubscribed', () => {
    const asDovecotReportsIt = [
      node({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox', subscribed: false, unread: 3 }),
      node({ path: 'Sent', name: 'Sent', specialUse: 'sent', subscribed: true }),
    ]

    render(<FolderTree folders={asDovecotReportsIt} selectedPath={null} onSelect={vi.fn()} />)

    expect(screen.getByText('Inbox')).toBeInTheDocument()
    expect(screen.getByText('3')).toBeInTheDocument()
  })

  it('still hides an ordinary unsubscribed folder', () => {
    const folders = [
      node({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox', subscribed: false }),
      node({ path: 'Archive', name: 'Archive', specialUse: 'archive', subscribed: false }),
    ]

    render(<FolderTree folders={folders} selectedPath={null} onSelect={vi.fn()} />)

    expect(screen.getByText('Inbox')).toBeInTheDocument()
    expect(screen.queryByText('Archive')).not.toBeInTheDocument()
  })

  // An unread badge asks the user to go and read something. Deleted mail and filtered spam are
  // the two places where that prompt is noise — the live mailbox showed 8 unread in the trash.
  it('does not badge unread counts in the trash or the junk folder', () => {
    const folders = [
      node({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox', unread: 1 }),
      node({ path: 'Deleted Items', name: 'Deleted Items', specialUse: 'trash', unread: 8 }),
      node({ path: 'Junk', name: 'Junk', specialUse: 'junk', unread: 5 }),
    ]

    render(<FolderTree folders={folders} selectedPath={null} onSelect={vi.fn()} />)

    expect(screen.getByText('1')).toBeInTheDocument()
    expect(screen.queryByText('8')).not.toBeInTheDocument()
    expect(screen.queryByText('5')).not.toBeInTheDocument()
    // The folders themselves stay visible — only their badge goes.
    expect(screen.getByText('Trash')).toBeInTheDocument()
    expect(screen.getByText('Junk')).toBeInTheDocument()
  })

  // The visible badge says "Inbox 4" to a sighted user; a bare aria-hidden count would say only
  // "Inbox" to a screen reader and the four waiting messages would never be announced. The name
  // must carry both pieces of information — and only when a badge is actually rendered.
  it('exposes the unread count in the accessible name, but only where the badge is shown', () => {
    const folders = [
      node({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox', unread: 4 }),
      node({ path: 'Deleted Items', name: 'Deleted Items', specialUse: 'trash', unread: 8 }),
    ]

    render(<FolderTree folders={folders} selectedPath={null} onSelect={vi.fn()} />)

    expect(screen.getByRole('button', { name: 'Inbox, 4 unread' })).toBeInTheDocument()
    // Trash suppresses the badge entirely, so its accessible name stays the plain label —
    // an exact-name query for 'Trash' only matches if no suffix leaked in.
    expect(screen.getByRole('button', { name: 'Trash' })).not.toHaveAccessibleName(/unread/i)
  })

  // The role label replaces the folder name — that is the point of assigning roles — but the
  // real mailbox name must stay reachable: it lives in the button's title.
  it('shows the role label and keeps the real name as the tooltip', () => {
    const folders = [
      node({ path: 'Deleted Items', name: 'Deleted Items', specialUse: 'trash' }),
      node({ path: 'Perso', name: 'Perso' }),
    ]

    render(<FolderTree folders={folders} selectedPath={null} onSelect={vi.fn()} />)

    expect(screen.getByText('Trash')).toBeInTheDocument()
    expect(screen.queryByText('Deleted Items')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Trash' })).toHaveAttribute('title', 'Deleted Items')
    // An ordinary folder keeps its name and needs no tooltip.
    expect(screen.getByRole('button', { name: 'Perso' })).not.toHaveAttribute('title')
  })

  it('hides an unsubscribed child of a visible parent', () => {
    const withHiddenChild = [node({
      path: 'INBOX', name: 'INBOX', specialUse: 'inbox',
      children: [node({ path: 'INBOX/Gone', name: 'Gone', subscribed: false })],
    })]

    render(<FolderTree folders={withHiddenChild} selectedPath={null} onSelect={vi.fn()} />)

    expect(screen.queryByText('Gone')).not.toBeInTheDocument()
  })
})
