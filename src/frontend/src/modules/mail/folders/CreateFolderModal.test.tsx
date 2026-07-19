import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import CreateFolderModal from './CreateFolderModal'
import type { MailFolderNode } from '../api/mailTypes'

const mocks = vi.hoisted(() => ({ create: vi.fn() }))

vi.mock('../queries', () => ({
  useCreateFolder: () => ({ mutateAsync: mocks.create, isPending: false }),
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
    children: [node({ path: 'Projects/Alpha', name: 'Alpha' })],
  }),
]

function renderModal(props: Partial<React.ComponentProps<typeof CreateFolderModal>> = {}) {
  const onNotify = props.onNotify ?? vi.fn()
  const onClose = props.onClose ?? vi.fn()
  const view = render(
    <CreateFolderModal
      folders={tree}
      defaultParent={props.defaultParent ?? 'INBOX'}
      onClose={onClose}
      onNotify={onNotify}
    />)
  return { ...view, onNotify, onClose }
}

describe('CreateFolderModal', () => {
  beforeEach(() => vi.clearAllMocks())

  // Closing is the ✕ alone, as in the admin dialogs — no separate Cancel button.
  it('closes without creating anything', () => {
    const { onClose } = renderModal()

    fireEvent.click(screen.getByRole('button', { name: 'Close' }))

    expect(mocks.create).not.toHaveBeenCalled()
    expect(onClose).toHaveBeenCalled()
  })

  // Built from the admin module's parts: .field-h is their horizontal label/control row.
  it('lays its fields out the way the admin dialogs do', () => {
    const { container } = renderModal()

    expect(container.querySelectorAll('.field-h')).toHaveLength(2)
    // Labels sit beside their control, so the association must be explicit.
    expect(screen.getByLabelText('Name')).toHaveAttribute('id', 'new-folder-name')
    expect(screen.getByLabelText('Parent')).toHaveAttribute('id', 'new-folder-parent')
  })

  it('submits on Enter, like every other form in the app', async () => {
    mocks.create.mockResolvedValue('Reports')
    const { container } = renderModal()

    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Reports' } })
    fireEvent.submit(container.querySelector('form')!)

    await waitFor(() =>
      expect(mocks.create).toHaveBeenCalledWith({ parentPath: 'INBOX', name: 'Reports' }))
  })

  it('keeps Create disabled until a name is typed', () => {
    renderModal()

    expect(screen.getByRole('button', { name: 'Create folder' })).toBeDisabled()
  })

  it('creates under the chosen parent, notifies and closes', async () => {
    mocks.create.mockResolvedValue('INBOX/Reports')
    const { onNotify, onClose } = renderModal()

    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Reports' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create folder' }))

    await waitFor(() =>
      expect(mocks.create).toHaveBeenCalledWith({ parentPath: 'INBOX', name: 'Reports' }))
    expect(onNotify).toHaveBeenCalledWith('Folder "Reports" created')
    expect(onClose).toHaveBeenCalled()
  })

  it('surfaces the backend message when creation fails, and stays open', async () => {
    mocks.create.mockRejectedValue(new Error("A folder name cannot be empty or contain '/'"))
    const { onNotify, onClose } = renderModal()

    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'a/b' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create folder' }))

    await waitFor(() =>
      expect(onNotify).toHaveBeenCalledWith("A folder name cannot be empty or contain '/'", 'error'))
    expect(onClose).not.toHaveBeenCalled()
  })

  // Opened from the mail column, the folder in view is the parent one expects.
  it('preselects the parent it was given', () => {
    renderModal({ defaultParent: 'Projects' })

    expect(screen.getByLabelText('Parent')).toHaveValue('Projects')
  })

  it('offers every folder as a parent, children included', () => {
    renderModal()

    const options = Array.from(screen.getByLabelText('Parent').querySelectorAll('option'))
    expect(options.map(o => o.value)).toEqual(['', 'INBOX', 'Projects', 'Projects/Alpha'])
  })
})
