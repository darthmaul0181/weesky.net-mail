import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import ContactCard from './ContactCard'
import type { Contact } from './contactTypes'

function contact(fields: Partial<Contact> & { id: string }): Contact {
  return {
    firstName: null, lastName: null, nickname: null, isFavorite: false, addresses: [], ...fields,
  }
}

const bruno = contact({
  id: 'b', firstName: 'Bruno', lastName: 'Mertens', nickname: 'bru',
  addresses: ['bruno@x.be', 'b.mertens@wk.be'],
})

function setup(overrides: Partial<Parameters<typeof ContactCard>[0]> = {}) {
  const props = {
    contact: bruno, onEdit: vi.fn(), onDelete: vi.fn(), onToggleFavorite: vi.fn(), ...overrides,
  }
  return { ...props, ...render(<ContactCard {...props} />) }
}

describe('ContactCard', () => {
  it('heads the card with the display name', () => {
    setup()

    expect(screen.getByRole('heading', { name: 'Bruno Mertens' })).toBeInTheDocument()
  })

  it('lists every address in order', () => {
    setup()

    const addresses = screen.getAllByTestId('card-address').map(node => node.textContent)
    expect(addresses?.[0]).toContain('bruno@x.be')
    expect(addresses?.[1]).toContain('b.mertens@wk.be')
  })

  // Position 0 is the primary by definition, so the card has to say which one it is: it is the
  // address a reply or a new message will use.
  it('marks the first address as the primary', () => {
    setup()

    expect(screen.getAllByTestId('card-address')[0]).toHaveTextContent(/primary/i)
    expect(screen.getAllByTestId('card-address')[1]).not.toHaveTextContent(/primary/i)
  })

  it('shows the nickname', () => {
    setup()

    expect(screen.getByText('bru')).toBeInTheDocument()
  })

  // A field that does not exist renders nothing at all — an empty labelled row reads as data lost.
  it('renders no nickname row when there is none', () => {
    setup({ contact: contact({ id: 'n', firstName: 'Alice', addresses: ['a@x.be'] }) })

    expect(screen.queryByText(/nickname/i)).not.toBeInTheDocument()
  })

  it('renders no address section when the contact carries none', () => {
    setup({ contact: contact({ id: 'n', firstName: 'Alice' }) })

    expect(screen.queryByTestId('card-address')).not.toBeInTheDocument()
  })

  // The tile's own arrangement, one surface up: the star and the pencil on the row, the
  // destructive one a click deeper.
  it('offers the favourite toggle and edit in the head, delete behind the kebab', async () => {
    const props = setup()

    await userEvent.click(screen.getByRole('button', { name: /^edit$/i }))
    await userEvent.click(screen.getByRole('button', { name: /add to favourites/i }))
    await userEvent.click(screen.getByRole('button', { name: /contact actions/i }))
    await userEvent.click(screen.getByRole('menuitem', { name: /^delete$/i }))

    expect(props.onEdit).toHaveBeenCalledWith('b')
    expect(props.onToggleFavorite).toHaveBeenCalledWith(bruno)
    expect(props.onDelete).toHaveBeenCalledWith(bruno)
  })

  // The phone's shape: three named cells in a band of their own, no kebab — a last cell that only
  // ever opens a one-entry menu would spend a third of the screen saying nothing.
  it('draws the three actions as a bottom band when the caller asks for one', () => {
    const { container } = setup({ bottomActions: true })

    const bar = container.querySelector('.actionbar')
    expect(bar).not.toBeNull()
    expect(bar!.querySelectorAll('.actionbar-item')).toHaveLength(3)
    expect(container.querySelector('.contact-card-actions')).toBeNull()
    expect(screen.queryByRole('button', { name: /contact actions/i })).not.toBeInTheDocument()
  })

  it('keeps the actions in the head when it is not asked for a band', () => {
    const { container } = setup()

    expect(container.querySelector('.contact-card-head .contact-card-actions')).not.toBeNull()
    expect(container.querySelector('.actionbar')).toBeNull()
  })

  it('names the action to come on the favourite toggle', () => {
    setup({ contact: { ...bruno, isFavorite: true } })

    expect(screen.getByRole('button', { name: /remove from favourites/i })).toBeInTheDocument()
  })

  it('invites a pick when nothing is selected', () => {
    setup({ contact: null })

    expect(screen.getByText(/select a contact/i)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^edit$/i })).not.toBeInTheDocument()
  })
})
