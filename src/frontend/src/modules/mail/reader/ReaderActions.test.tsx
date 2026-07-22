import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import ReaderActions from './ReaderActions'

const flagProps = { seen: true, flagged: false, onToggleSeen: () => {}, onToggleFlagged: () => {} }

describe('ReaderActions', () => {
  it('offers the sender colours while the message wears the theme adaptation', () => {
    render(<ReaderActions showColourToggle originalColours={false} onToggleColours={() => {}} {...flagProps} />)

    expect(screen.getByRole('button', { name: 'Original colours' })).toBeInTheDocument()
    expect(screen.getByRole('tooltip')).toHaveTextContent('Showing the colours the sender chose.')
  })

  it('offers the way back while the sender colours are shown', () => {
    render(<ReaderActions showColourToggle originalColours onToggleColours={() => {}} {...flagProps} />)

    expect(screen.getByRole('button', { name: 'Match my theme' })).toBeInTheDocument()
    expect(screen.getByRole('tooltip')).toHaveTextContent('Colours are adapted to your dark theme.')
  })

  // Both icons are aria-hidden, so the accessible tree cannot tell them apart — the sun's
  // <circle> is the one structural difference a test can hold on to. Without this, swapping
  // the icons while keeping the labels would ship a visually lying button, all tests green.
  it('draws the sun while adapted and the moon once original', () => {
    const { container, rerender } = render(
      <ReaderActions showColourToggle originalColours={false} onToggleColours={() => {}} {...flagProps} />)
    expect(container.querySelector('.action-btn circle')).not.toBeNull()

    rerender(<ReaderActions showColourToggle originalColours onToggleColours={() => {}} {...flagProps} />)
    expect(container.querySelector('.action-btn circle')).toBeNull()
  })

  it('reports the click', () => {
    const onToggle = vi.fn()
    render(<ReaderActions showColourToggle originalColours={false} onToggleColours={onToggle} {...flagProps} />)

    fireEvent.click(screen.getByRole('button', { name: 'Original colours' }))

    expect(onToggle).toHaveBeenCalledOnce()
  })

  // A rule beside a lone button reads as a rendering fault — same reason the folder tree
  // only draws its hr between two populated blocks.
  it('hides the toggle and its rule together, keeping the menu button', () => {
    const { container } = render(
      <ReaderActions showColourToggle={false} originalColours={false} onToggleColours={() => {}} {...flagProps} />)

    expect(screen.queryByRole('button', { name: 'Original colours' })).not.toBeInTheDocument()
    expect(container.querySelector('.actions-rule')).toBeNull()
    expect(screen.getByRole('button', { name: 'Message actions' })).toBeInTheDocument()
  })

  it('the kebab opens a menu with the two flag entries', () => {
    render(<ReaderActions showColourToggle={false} originalColours={false} onToggleColours={() => {}}
      seen flagged={false} onToggleSeen={() => {}} onToggleFlagged={() => {}} />)

    fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))

    expect(screen.getByRole('menuitem', { name: 'Mark as unread' })).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Star' })).toBeInTheDocument()
  })

  it('labels follow the state', () => {
    render(<ReaderActions showColourToggle={false} originalColours={false} onToggleColours={() => {}}
      seen={false} flagged onToggleSeen={() => {}} onToggleFlagged={() => {}} />)

    fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))

    expect(screen.getByRole('menuitem', { name: 'Mark as read' })).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Unstar' })).toBeInTheDocument()
  })

  it('entries fire their callbacks and close', () => {
    const onToggleSeen = vi.fn()
    const onToggleFlagged = vi.fn()
    render(<ReaderActions showColourToggle={false} originalColours={false} onToggleColours={() => {}}
      seen flagged={false} onToggleSeen={onToggleSeen} onToggleFlagged={onToggleFlagged} />)

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
    const { unmount } = render(<ReaderActions showColourToggle={false} originalColours={false}
      onToggleColours={() => {}} seen flagged={false} onToggleSeen={() => {}} onToggleFlagged={() => {}} />)
    fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))
    expect(screen.getByRole('menuitem', { name: 'Mark as unread' }).querySelector('rect')).not.toBeNull()
    unmount()

    render(<ReaderActions showColourToggle={false} originalColours={false} onToggleColours={() => {}}
      seen={false} flagged={false} onToggleSeen={() => {}} onToggleFlagged={() => {}} />)
    fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))
    expect(screen.getByRole('menuitem', { name: 'Mark as read' }).querySelector('rect')).toBeNull()
  })

  // StarIcon's fill is the state, not just its label — the same shape of guard MessageList's
  // row star has.
  it('fills the star icon only when the message is flagged', () => {
    const { unmount } = render(<ReaderActions showColourToggle={false} originalColours={false}
      onToggleColours={() => {}} seen flagged onToggleSeen={() => {}} onToggleFlagged={() => {}} />)
    fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))
    expect(screen.getByRole('menuitem', { name: 'Unstar' }).querySelector('svg'))
      .toHaveAttribute('fill', 'currentColor')
    unmount()

    render(<ReaderActions showColourToggle={false} originalColours={false} onToggleColours={() => {}}
      seen flagged={false} onToggleSeen={() => {}} onToggleFlagged={() => {}} />)
    fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))
    expect(screen.getByRole('menuitem', { name: 'Star' }).querySelector('svg')).toHaveAttribute('fill', 'none')
  })
})
