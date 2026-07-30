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

  // Search shape 1: the filter lives in the list header, which also carries the count. It has
  // no visible label there, so the accessible name has to come from aria-label.
  it('puts the filter in the list header, named by aria-label', () => {
    const { container } = renderModal()

    expect(search()).toHaveAttribute('aria-label', 'Search folders')
    expect(container.querySelector('.admin-list-header')).toContainElement(search())
    expect(container.querySelector('.admin-list-header')).toContainElement(
      screen.getByText('7 folders'))
  })

  it('wears the site\'s search-input class rather than a picker-local one', () => {
    renderModal()

    expect(search()).toHaveClass('search-input')
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

  // .row-tag is the site's badge for a row qualifier; the picker used to carry a duplicate of it.
  it('badges rows with the shared .row-tag, not a picker-local class', () => {
    const { container } = renderModal()

    expect(container.querySelectorAll('.row-tag')).toHaveLength(3) // current, container, Junk
    expect(container.querySelector('.folder-pick-badge')).not.toBeInTheDocument()
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

  // The label names the outcome, not the verb, so it is the button itself that says where the
  // mail is going. Disarmed, it falls back to the bare verb.
  it('keeps the action button disabled until a row is selected, then names the outcome', () => {
    const { onPick } = renderModal()

    expect(screen.getByRole('button', { name: 'Move' })).toBeDisabled()

    fireEvent.click(screen.getByRole('button', { name: /Archive/ }))

    const move = screen.getByRole('button', { name: 'Move to Archive' })
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

  // Enter used to work only on a lone match; a picked folder ignored the key entirely.
  it('commits the picked folder on Enter', () => {
    const { onPick } = renderModal()

    fireEvent.click(screen.getByRole('button', { name: /Belfius/ }))
    fireEvent.keyDown(search(), { key: 'Enter' })

    expect(onPick).toHaveBeenCalledWith('Banque/Belfius')
  })

  // The body is a form, so submitting it commits — that is what makes Enter work from anywhere
  // inside the dialog rather than from the search box alone.
  it('commits the picked folder when the form is submitted', () => {
    const { onPick, container } = renderModal()

    fireEvent.click(screen.getByRole('button', { name: /Archive/ }))
    fireEvent.submit(container.querySelector('form')!)

    expect(onPick).toHaveBeenCalledWith('Archive')
  })

  // The class is the seam: .is-selected is what carries the fill and the inset accent bar the
  // content-list language calls for. jsdom loads no stylesheet, so the bar itself is unassertable
  // here — only that the right row wears the class that draws it.
  it('marks the picked row with the content-list selected class', () => {
    const { container } = renderModal()

    fireEvent.click(screen.getByRole('button', { name: /Archive/ }))

    const picked = container.querySelectorAll('.folder-pick-row.is-selected')
    expect(picked).toHaveLength(1)
    expect(picked[0]).toHaveTextContent('Archive')
  })

  // A row already selected can become the current folder if it changes underneath the open
  // modal; the guard must test the enabled set, not merely the visible one.
  it('disarms the primary button when the selected row becomes the current folder', async () => {
    const { rerender, onPick, onClose } = renderModal()

    fireEvent.click(screen.getByRole('button', { name: /Archive/ }))
    expect(screen.getByRole('button', { name: 'Move to Archive' })).toBeEnabled()

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

  // Icon continuity: the kebab entry that opened the dialog carries the same glyph, so the
  // trigger and what it produced read as one action.
  it('precedes its title with the trigger\'s own icon', () => {
    const { container, unmount } = renderModal()
    // FolderMoveIcon: paths, no rect. The kebab's "Move to…" entry draws the same glyph.
    expect(container.querySelector('.modal-title svg path')).toBeInTheDocument()
    expect(container.querySelector('.modal-title svg rect')).not.toBeInTheDocument()
    unmount()

    // CopyIcon: two rects, no path.
    const copy = renderModal({ mode: 'copy' })
    expect(copy.container.querySelectorAll('.modal-title svg rect')).toHaveLength(2)
    expect(copy.container.querySelector('.modal-title svg path')).not.toBeInTheDocument()
  })

  it('closes on the ✕ and on Escape, and offers no second way out', () => {
    const { onClose } = renderModal()

    // The ✕ is the only dismissal control the site's dialogs carry.
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(onClose).toHaveBeenCalledTimes(1)

    fireEvent.keyDown(document, { key: 'Escape' })
    expect(onClose).toHaveBeenCalledTimes(2)
  })

  // Depth comes from the whole tree, so a match whose parent was filtered out still reads
  // as a child rather than pretending to be top-level.
  it('keeps a matching child indented after its parent is filtered away', () => {
    const { container } = renderModal()

    fireEvent.change(search(), { target: { value: 'bel' } })

    expect(container.querySelector('.folder-pick-indent')!.textContent).toBe(indent(1))
  })
})
