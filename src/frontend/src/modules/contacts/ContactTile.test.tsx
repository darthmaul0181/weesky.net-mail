import { describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import ContactTile from './ContactTile'
import type { ContactTileProps, TileCallbacks } from './ContactTile'
import type { Contact } from './contactTypes'

/**
 * The memo contract, which only makes sense at this level: what the list must hand a tile for one
 * tick — or one letter typed — to redraw one tile instead of the book. What the tile *draws* is
 * asserted in `ContactList.test.tsx`, where the tiles ship the way they are drawn.
 */

const contact = (over: Partial<Contact> = {}): Contact => ({
  id: 'a', firstName: 'Alice', lastName: 'Dupont', isFavorite: false,
  addresses: ['alice@x.be'], ...over,
})

function callbacks(): TileCallbacks {
  return {
    open: vi.fn(), check: vi.fn(), toggleFavorite: vi.fn(), edit: vi.fn(), remove: vi.fn(),
    dragStart: vi.fn(), dragEnd: vi.fn(),
  }
}

/** A getter on the one field the tile reads exactly once per render — `data-testid` is the only
    place `id` is read during a render, every handler below closing over the contact instead. */
function counted(over: Partial<Contact> = {}) {
  const one = contact(over)
  const seen = { drawn: 0 }
  const { id } = one
  Object.defineProperty(one, 'id', { get() { seen.drawn += 1; return id } })
  return { contact: one, seen }
}

function props(over: Partial<ContactTileProps> = {}): ContactTileProps {
  return {
    contact: contact(), checked: false, open: false, dragging: false, on: callbacks(), ...over,
  }
}

describe('ContactTile', () => {
  it('draws nothing again when the list re-renders with the same props', () => {
    const { contact: watched, seen } = counted()
    const base = props({ contact: watched })
    const { rerender } = render(<ContactTile {...base} />)
    expect(seen.drawn).toBe(1)

    rerender(<ContactTile {...base} />)

    expect(seen.drawn).toBe(1)
  })

  it('draws again when its own checkbox changes', () => {
    const { contact: watched, seen } = counted()
    const base = props({ contact: watched })
    const { rerender } = render(<ContactTile {...base} />)

    rerender(<ContactTile {...base} checked />)

    expect(seen.drawn).toBe(2)
    expect(screen.getByRole('checkbox')).toBeChecked()
  })

  /* Why `useSelection` is never handed to a tile: it answers a fresh object with fresh closures
     every render, and a tile holding one redraws whenever anything does. */
  it('draws again when it is handed a callback object built afresh', () => {
    const { contact: watched, seen } = counted()
    const base = props({ contact: watched })
    const { rerender } = render(<ContactTile {...base} />)

    rerender(<ContactTile {...base} on={callbacks()} />)

    expect(seen.drawn).toBe(2)
  })

  it('acts on its own contact: the id where a position was, the row where the whole card was', () => {
    const on = callbacks()
    const one = contact({ id: 'b', isFavorite: true })
    render(<ContactTile {...props({ contact: one, on })} />)

    fireEvent.click(screen.getByRole('checkbox'))
    fireEvent.click(screen.getByRole('button', { name: /from favourites/i }))
    fireEvent.click(screen.getByRole('button', { name: /^edit/i }))
    fireEvent.click(screen.getByRole('button', { name: /^delete/i }))

    expect(on.check).toHaveBeenCalledWith('b')
    expect(on.toggleFavorite).toHaveBeenCalledWith(one)
    expect(on.edit).toHaveBeenCalledWith('b')
    expect(on.remove).toHaveBeenCalledWith(one)
    expect(on.open).not.toHaveBeenCalled()
  })

  // `aria-current` and never `aria-selected`: the checkboxes are a real multi-selection here.
  it('names the open contact on the content cell alone', () => {
    render(<ContactTile {...props({ open: true })} />)

    expect(screen.getByRole('row')).toHaveClass('is-selected')
    expect(document.querySelector('.contact-tile-content'))
      .toHaveAttribute('aria-current', 'true')
    expect(screen.getByRole('row')).not.toHaveAttribute('aria-selected')
  })

  it('wears is-dragging while the drag it belongs to is in flight', () => {
    render(<ContactTile {...props({ dragging: true })} />)

    expect(screen.getByRole('row')).toHaveClass('is-dragging')
  })
})
