import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import FolderDialogs from './FolderDialogs'
import type { MailFolderNode } from '../api/mailTypes'

const mocks = vi.hoisted(() => ({ create: vi.fn() }))

vi.mock('../queries', () => ({
  useCreateFolder: () => ({ mutateAsync: mocks.create, isPending: false }),
}))

function node(partial: Partial<MailFolderNode>): MailFolderNode {
  return {
    path: 'X', name: 'X', specialUse: null, selectable: true, subscribed: true,
    total: 0, unread: 0, uidValidity: 1, uidNext: null, highestModSeq: null, children: [], ...partial,
  }
}

const tree: MailFolderNode[] = [
  node({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox' }),
  node({ path: 'Projects', name: 'Projects' }),
]

function renderDialogs(onNotify = vi.fn()) {
  render(
    <MemoryRouter>
      <FolderDialogs folders={tree} selectedPath="INBOX" onNotify={onNotify} />
    </MemoryRouter>)
  return onNotify
}

describe('the folder column actions', () => {
  beforeEach(() => vi.clearAllMocks())

  // Icons with no visible text: the accessible name is the only name they have.
  it('names both icon actions', () => {
    renderDialogs()

    expect(screen.getByRole('button', { name: 'New folder' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Manage folders' })).toBeInTheDocument()
  })

  // Creating is a quick action; managing needs width a 240px column does not have.
  it('sends Manage folders to the folders settings page', () => {
    renderDialogs()

    expect(screen.getByRole('link', { name: 'Manage folders' }))
      .toHaveAttribute('href', '/settings/folders')
  })

  it('opens nothing until New folder is clicked', () => {
    renderDialogs()

    expect(screen.queryByRole('button', { name: 'Create folder' })).not.toBeInTheDocument()
  })

  it('opens the create dialog on the folder in view', () => {
    renderDialogs()

    fireEvent.click(screen.getByRole('button', { name: 'New folder' }))

    expect(screen.getByRole('button', { name: 'Create folder' })).toBeInTheDocument()
    expect(screen.getByLabelText('Parent')).toHaveValue('INBOX')
  })

  it('closes the create dialog again', () => {
    renderDialogs()
    fireEvent.click(screen.getByRole('button', { name: 'New folder' }))

    fireEvent.click(screen.getByRole('button', { name: 'Close' }))

    expect(screen.queryByRole('button', { name: 'Create folder' })).not.toBeInTheDocument()
  })
})
