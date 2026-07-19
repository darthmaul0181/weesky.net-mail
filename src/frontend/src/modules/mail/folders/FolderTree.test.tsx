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

    expect(screen.getByText('INBOX')).toBeInTheDocument()
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

    expect(screen.getByRole('button', { name: /INBOX/ })).toHaveClass('is-active')
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
    expect(names).toEqual(['INBOX', 'Trash', 'Alpha', 'Zebra'])
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
