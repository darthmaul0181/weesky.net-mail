import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import MoveMessagesModal from './MoveMessagesModal'
import { settle } from '../../test-utils'
import { indent } from './folders/folderNodes'
import type { MailFolderNode } from './api/mailTypes'

function node(partial: Partial<MailFolderNode>): MailFolderNode {
  return {
    path: 'X', name: 'X', specialUse: null, selectable: true, subscribed: true,
    total: 0, unread: 0, uidValidity: 1, uidNext: null, highestModSeq: null, children: [], ...partial,
  }
}

// Seven folders, the mockup's mailbox: an inbox, a parent with two children, a junk folder
// carrying an accent, and a container nobody can file into.
const tree: MailFolderNode[] = [
  node({ path: 'INBOX', name: 'Inbox', specialUse: 'inbox' }),
  node({ path: 'Archive', name: 'Archive' }),
  node({
    path: 'Banque', name: 'Banque',
    children: [
      node({ path: 'Banque/Belfius', name: 'Belfius' }),
      node({ path: 'Banque/ING', name: 'ING' }),
    ],
  }),
  node({ path: 'Junk', name: 'Courrier indésirable', specialUse: 'junk' }),
  node({ path: 'Projets', name: 'Projets', selectable: false }),
]

function renderModal(props: Partial<React.ComponentProps<typeof MoveMessagesModal>> = {}) {
  const onPick = props.onPick ?? vi.fn()
  const onClose = props.onClose ?? vi.fn()
  const view = render(
    <MoveMessagesModal
      mode={props.mode ?? 'move'}
      folders={props.folders ?? tree}
      currentFolderPath={props.currentFolderPath ?? 'INBOX'}
      onPick={onPick}
      onClose={onClose}
    />)
  return { ...view, onPick, onClose }
}

const search = () => screen.getByLabelText('Search folders')

describe('MoveMessagesModal', () => {
  beforeEach(() => vi.clearAllMocks())

  it('takes the focus on mount, so typing filters straight away', () => {
    renderModal()

    expect(search()).toHaveFocus()
  })

  // .field-style rows put the label beside the control, so the pair must be explicit.
  it('associates the label with the field', () => {
    renderModal()

    expect(search()).toHaveAttribute('id', 'move-folder-search')
  })

  it('lists every folder, children included, and counts them', () => {
    renderModal()

    expect(screen.getByText('7 folders')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Belfius/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Courrier indésirable/ })).toBeInTheDocument()
  })

  it('filters on the typed text and rewords the count', () => {
    renderModal()

    fireEvent.change(search(), { target: { value: 'bel' } })

    expect(screen.getByText('1 of 7 folders')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Belfius/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Archive/ })).not.toBeInTheDocument()
  })

  it('finds an accented folder from an unaccented query', () => {
    renderModal()

    fireEvent.change(search(), { target: { value: 'indesirable' } })

    expect(screen.getByRole('button', { name: /Courrier indésirable/ })).toBeInTheDocument()
    expect(screen.getByText('1 of 7 folders')).toBeInTheDocument()
  })

  it('says so when nothing matches', () => {
    renderModal()

    fireEvent.change(search(), { target: { value: 'zzz' } })

    expect(screen.getByText('0 of 7 folders')).toBeInTheDocument()
    expect(screen.getByText(/No folder matches/)).toBeInTheDocument()
  })

  // Visible but disabled: a list missing a folder one expects reads as a bug.
  it('disables the current folder and badges it, without removing it', async () => {
    const { onPick } = renderModal()

    const row = screen.getByRole('button', { name: /Inbox/ })
    expect(row).toBeDisabled()
    expect(row).toHaveTextContent('current')

    fireEvent.click(row)
    await settle()
    expect(onPick).not.toHaveBeenCalled()
  })

  it('disables a non-selectable container and badges it', async () => {
    const { onPick } = renderModal()

    const row = screen.getByRole('button', { name: /Projets/ })
    expect(row).toBeDisabled()
    expect(row).toHaveTextContent('container')

    fireEvent.click(row)
    await settle()
    expect(onPick).not.toHaveBeenCalled()
  })

  // Settled with the user: copying a message into its own folder is refused like moving is.
  it('disables the current folder in copy mode too', () => {
    renderModal({ mode: 'copy' })

    expect(screen.getByRole('button', { name: /Inbox/ })).toBeDisabled()
  })

  it('badges a folder holding a role', () => {
    renderModal()

    expect(screen.getByRole('button', { name: /Courrier indésirable/ })).toHaveTextContent('Junk')
  })

  it('picks on Enter when exactly one enabled row matches', () => {
    const { onPick } = renderModal()

    fireEvent.change(search(), { target: { value: 'bel' } })
    fireEvent.keyDown(search(), { key: 'Enter' })

    expect(onPick).toHaveBeenCalledWith('Banque/Belfius')
  })

  it('does nothing on Enter when several rows match', async () => {
    const { onPick } = renderModal()

    fireEvent.change(search(), { target: { value: 'n' } })
    fireEvent.keyDown(search(), { key: 'Enter' })

    await settle()
    expect(onPick).not.toHaveBeenCalled()
  })

  it('does nothing on Enter when the single match is disabled', async () => {
    const { onPick } = renderModal()

    fireEvent.change(search(), { target: { value: 'projets' } })
    fireEvent.keyDown(search(), { key: 'Enter' })

    await settle()
    expect(onPick).not.toHaveBeenCalled()
  })

  it('keeps the action button disabled until a row is selected, then picks it', () => {
    const { onPick } = renderModal()

    const move = screen.getByRole('button', { name: 'Move' })
    expect(move).toBeDisabled()

    fireEvent.click(screen.getByRole('button', { name: /Archive/ }))
    expect(move).toBeEnabled()

    fireEvent.click(move)
    expect(onPick).toHaveBeenCalledWith('Archive')
  })

  it('drops a selection the query has filtered off-screen', () => {
    renderModal()

    fireEvent.click(screen.getByRole('button', { name: /Archive/ }))
    fireEvent.change(search(), { target: { value: 'bel' } })

    expect(screen.getByRole('button', { name: 'Move' })).toBeDisabled()
  })

  // A row already selected can become the current folder if it changes underneath the open
  // modal; the guard must test the enabled set, not merely the visible one.
  it('disarms the primary button when the selected row becomes the current folder', async () => {
    const { rerender, onPick, onClose } = renderModal()

    fireEvent.click(screen.getByRole('button', { name: /Archive/ }))
    expect(screen.getByRole('button', { name: 'Move' })).toBeEnabled()

    rerender(
      <MoveMessagesModal
        mode="move"
        folders={tree}
        currentFolderPath="Archive"
        onPick={onPick}
        onClose={onClose}
      />)

    await settle()
    expect(screen.getByRole('button', { name: 'Move' })).toBeDisabled()
  })

  it('titles itself and labels its button by mode', () => {
    const { unmount } = renderModal()
    expect(screen.getByText('Move to folder')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Move' })).toBeInTheDocument()
    unmount()

    renderModal({ mode: 'copy' })
    expect(screen.getByText('Copy to folder')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Copy' })).toBeInTheDocument()
  })

  it('closes on the ✕, on Cancel and on Escape', () => {
    const { onClose, unmount } = renderModal()

    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(onClose).toHaveBeenCalledTimes(1)

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(onClose).toHaveBeenCalledTimes(2)

    fireEvent.keyDown(document, { key: 'Escape' })
    expect(onClose).toHaveBeenCalledTimes(3)
    unmount()
  })

  // Depth comes from the whole tree, so a match whose parent was filtered out still reads
  // as a child rather than pretending to be top-level.
  it('keeps a matching child indented after its parent is filtered away', () => {
    const { container } = renderModal()

    fireEvent.change(search(), { target: { value: 'bel' } })

    expect(container.querySelector('.folder-pick-indent')!.textContent).toBe(indent(1))
  })
})
