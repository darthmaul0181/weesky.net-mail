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
  render(<ContactCard {...props} />)
  return props
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

  it('offers edit, delete and the favourite toggle', async () => {
    const props = setup()

    await userEvent.click(screen.getByRole('button', { name: /^edit$/i }))
    await userEvent.click(screen.getByRole('button', { name: /^delete$/i }))
    await userEvent.click(screen.getByRole('button', { name: /add to favourites/i }))

    expect(props.onEdit).toHaveBeenCalledWith('b')
    expect(props.onDelete).toHaveBeenCalledWith(bruno)
    expect(props.onToggleFavorite).toHaveBeenCalledWith(bruno)
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
