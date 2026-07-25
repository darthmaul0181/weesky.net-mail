import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import type { MenuEntry } from '../../../components/DropdownMenu'
import ReaderActions from './ReaderActions'

const noop = () => {}
// Every prop with a default, so a render only spells out what the case is about.
const base = {
  showColourToggle: false,
  originalColours: false,
  onToggleColours: noop,
  seen: true,
  flagged: false,
  onToggleSeen: noop,
  onToggleFlagged: noop,
  deleteLabel: 'Delete' as 'Delete' | 'Delete permanently',
  deleteDisabled: false,
  onDelete: noop,
  actions: [] as MenuEntry[],
  onReply: noop,
  onReplyAll: noop,
  onForward: noop,
  preparing: false,
}

describe('ReaderActions', () => {
  it('offers the sender colours while the message wears the theme adaptation', () => {
    render(<ReaderActions {...base} showColourToggle />)

    expect(screen.getByRole('button', { name: 'Original colours' })).toBeInTheDocument()
    expect(screen.getByRole('tooltip')).toHaveTextContent('Showing the colours the sender chose.')
  })

  it('offers the way back while the sender colours are shown', () => {
    render(<ReaderActions {...base} showColourToggle originalColours />)

    expect(screen.getByRole('button', { name: 'Match my theme' })).toBeInTheDocument()
    expect(screen.getByRole('tooltip')).toHaveTextContent('Colours are adapted to your dark theme.')
  })

  // Both icons are aria-hidden, so the accessible tree cannot tell them apart — the sun's
  // <circle> is the one structural difference a test can hold on to. Without this, swapping
  // the icons while keeping the labels would ship a visually lying button, all tests green.
  it('draws the sun while adapted and the moon once original', () => {
    const { container, rerender } = render(<ReaderActions {...base} showColourToggle />)
    expect(container.querySelector('.action-btn circle')).not.toBeNull()

    rerender(<ReaderActions {...base} showColourToggle originalColours />)
    expect(container.querySelector('.action-btn circle')).toBeNull()
  })

  it('reports the click', () => {
    const onToggle = vi.fn()
    render(<ReaderActions {...base} showColourToggle onToggleColours={onToggle} />)

    fireEvent.click(screen.getByRole('button', { name: 'Original colours' }))

    expect(onToggle).toHaveBeenCalledOnce()
  })

  // A rule beside a lone button reads as a rendering fault — same reason the folder tree
  // only draws its hr between two populated blocks. The quote-actions group always carries
  // its own leading rule, so only the colour toggle's rule is what disappears with it.
  it('hides the toggle and its rule together, keeping the menu button', () => {
    const { container } = render(<ReaderActions {...base} showColourToggle={false} />)

    expect(screen.queryByRole('button', { name: 'Original colours' })).not.toBeInTheDocument()
    expect(container.querySelectorAll('.actions-rule')).toHaveLength(1)
    expect(screen.getByRole('button', { name: 'Message actions' })).toBeInTheDocument()
  })

  it('the kebab opens a menu with the two flag entries', () => {
    render(<ReaderActions {...base} seen flagged={false} />)

    fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))

    expect(screen.getByRole('menuitem', { name: 'Mark as unread' })).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Star' })).toBeInTheDocument()
  })

  it('labels follow the state', () => {
    render(<ReaderActions {...base} seen={false} flagged />)

    fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))

    expect(screen.getByRole('menuitem', { name: 'Mark as read' })).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Unstar' })).toBeInTheDocument()
  })

  it('entries fire their callbacks and close', () => {
    const onToggleSeen = vi.fn()
    const onToggleFlagged = vi.fn()
    render(<ReaderActions {...base} seen flagged={false}
      onToggleSeen={onToggleSeen} onToggleFlagged={onToggleFlagged} />)

    fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Mark as unread' }))

    expect(onToggleSeen).toHaveBeenCalledOnce()
    expect(screen.queryByRole('menuitem', { name: 'Mark as unread' })).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Star' }))

    expect(onToggleFlagged).toHaveBeenCalledOnce()
  })

  // Both mail icons are aria-hidden, so the accessible name alone can't tell MailIcon (closed
  // envelope, a <rect>) from MailOpenIcon (no <rect>) — same trick as the sun/moon check above.
  // Without this, swapping the two would ship a visually lying entry, all tests green.
  it('draws the shut envelope for "Mark as unread" and the open one for "Mark as read"', () => {
    const { unmount } = render(<ReaderActions {...base} seen flagged={false} />)
    fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))
    expect(screen.getByRole('menuitem', { name: 'Mark as unread' }).querySelector('rect')).not.toBeNull()
    unmount()

    render(<ReaderActions {...base} seen={false} flagged={false} />)
    fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))
    expect(screen.getByRole('menuitem', { name: 'Mark as read' }).querySelector('rect')).toBeNull()
  })

  // StarIcon's fill is the state, not just its label — the same shape of guard MessageList's
  // row star has.
  it('fills the star icon only when the message is flagged', () => {
    const { unmount } = render(<ReaderActions {...base} seen flagged />)
    fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))
    expect(screen.getByRole('menuitem', { name: 'Unstar' }).querySelector('svg'))
      .toHaveAttribute('fill', 'currentColor')
    unmount()

    render(<ReaderActions {...base} seen flagged={false} />)
    fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))
    expect(screen.getByRole('menuitem', { name: 'Star' }).querySelector('svg')).toHaveAttribute('fill', 'none')
  })

  describe('the header delete button', () => {
    it('shows whether or not the colour toggle is present', () => {
      const { rerender } = render(<ReaderActions {...base} showColourToggle={false} />)
      expect(screen.getByRole('button', { name: 'Delete' })).toBeInTheDocument()

      rerender(<ReaderActions {...base} showColourToggle />)
      expect(screen.getByRole('button', { name: 'Delete' })).toBeInTheDocument()
    })

    // Rendered order is [colour toggle?] [rule?] [delete] [kebab].
    it('sits between the colour rule and the kebab', () => {
      const { container } = render(<ReaderActions {...base} showColourToggle />)

      const kids = Array.from(container.querySelector('.reader-actions')!.children)
      const ruleAt = kids.findIndex(k => k.classList.contains('actions-rule'))
      const deleteAt = kids.findIndex(k => k.classList.contains('is-danger'))
      const kebabAt = kids.findIndex(k => k.classList.contains('dropdown-root'))
      expect(ruleAt).toBeGreaterThanOrEqual(0)
      expect(deleteAt).toBeGreaterThan(ruleAt)
      expect(kebabAt).toBeGreaterThan(deleteAt)
    })

    it('names itself per deleteLabel and fires onDelete', () => {
      const onDelete = vi.fn()
      render(<ReaderActions {...base} onDelete={onDelete} />)

      fireEvent.click(screen.getByRole('button', { name: 'Delete' }))

      expect(onDelete).toHaveBeenCalledOnce()
    })

    it('reads "Delete permanently" inside the trash', () => {
      render(<ReaderActions {...base} deleteLabel="Delete permanently" />)

      expect(screen.getByRole('button', { name: 'Delete permanently' })).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument()
    })

    it('disables with its reason and fires nothing', () => {
      const onDelete = vi.fn()
      render(<ReaderActions {...base} deleteDisabled onDelete={onDelete} />)

      const button = screen.getByRole('button', { name: 'Delete' })
      expect(button).toBeDisabled()
      expect(button).toHaveAttribute('title', 'Assign the trash folder in Settings → Folders')

      fireEvent.click(button)
      expect(onDelete).not.toHaveBeenCalled()
    })
  })

  describe('the quote actions', () => {
    it('fires the three quote actions and disables them while preparing', () => {
      const onReply = vi.fn(); const onReplyAll = vi.fn(); const onForward = vi.fn()
      render(<ReaderActions {...base} onReply={onReply} onReplyAll={onReplyAll} onForward={onForward} preparing={false} />)

      fireEvent.click(screen.getByRole('button', { name: 'Reply' }))
      fireEvent.click(screen.getByRole('button', { name: 'Reply all' }))
      fireEvent.click(screen.getByRole('button', { name: 'Forward' }))
      expect(onReply).toHaveBeenCalledOnce()
      expect(onReplyAll).toHaveBeenCalledOnce()
      expect(onForward).toHaveBeenCalledOnce()
    })

    it('disables the quote actions while a preparation is pending', () => {
      render(<ReaderActions {...base} onReply={vi.fn()} onReplyAll={vi.fn()} onForward={vi.fn()} preparing />)
      expect(screen.getByRole('button', { name: 'Reply' })).toBeDisabled()
      expect(screen.getByRole('button', { name: 'Forward' })).toBeDisabled()
    })
  })

  describe('the kebab action group', () => {
    const actions: MenuEntry[] = [
      { label: 'Archive', onSelect: noop },
      { label: 'Report as junk', onSelect: noop },
      { label: 'Move to…', onSelect: noop },
      { label: 'Copy to…', onSelect: noop },
    ]

    it('separates the flag entries from the action entries', () => {
      const { container } = render(<ReaderActions {...base} actions={actions} />)

      fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))

      expect(container.querySelector('.dropdown-menu .dropdown-rule')).not.toBeNull()
      expect(screen.getByRole('menuitem', { name: 'Archive' })).toBeInTheDocument()
      expect(screen.getByRole('menuitem', { name: 'Report as junk' })).toBeInTheDocument()
      expect(screen.getByRole('menuitem', { name: 'Move to…' })).toBeInTheDocument()
      expect(screen.getByRole('menuitem', { name: 'Copy to…' })).toBeInTheDocument()
    })

    // A separator under nothing reads as a rendering fault, like the missing colour rule.
    it('omits the separator when there are no action entries', () => {
      const { container } = render(<ReaderActions {...base} actions={[]} />)

      fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))

      expect(container.querySelector('.dropdown-menu .dropdown-rule')).toBeNull()
    })

    it('renders a disabled action entry with its title, firing nothing', () => {
      const onSelect = vi.fn()
      render(<ReaderActions {...base}
        actions={[{ label: 'Report as junk', onSelect, disabled: true, title: 'no junk folder' }]} />)

      fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))
      const entry = screen.getByRole('menuitem', { name: 'Report as junk' })
      expect(entry).toBeDisabled()
      expect(entry).toHaveAttribute('title', 'no junk folder')

      fireEvent.click(entry)
      expect(onSelect).not.toHaveBeenCalled()
    })
  })
})
