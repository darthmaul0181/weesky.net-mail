import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import ContactList from './ContactList'
import type { Contact } from './contactTypes'

function contact(fields: Partial<Contact> & { id: string }): Contact {
  return {
    firstName: null, lastName: null, nickname: null, isFavorite: false, addresses: [], ...fields,
  }
}

const alice = contact({
  id: 'a', firstName: 'Alice', lastName: 'Dupont', isFavorite: true, addresses: ['alice@x.be'],
})
const bruno = contact({
  id: 'b', firstName: 'Bruno', lastName: 'Mertens', addresses: ['bruno@x.be', 'b@wk.be', 'c@wk.be'],
})

function setup(overrides: Partial<Parameters<typeof ContactList>[0]> = {}) {
  const props = {
    contacts: [alice, bruno], selectedId: null,
    onSelect: vi.fn(), onToggleFavorite: vi.fn(), onEdit: vi.fn(), onDelete: vi.fn(),
    ...overrides,
  }
  render(<ContactList {...props} />)
  return props
}

describe('ContactList', () => {
  it('renders one tile per contact, named by displayNameOf', () => {
    setup()

    expect(screen.getByText('Alice Dupont')).toBeInTheDocument()
    expect(screen.getByText('Bruno Mertens')).toBeInTheDocument()
  })

  it('shows the primary address and counts the others', () => {
    setup()

    expect(screen.getByText(/bruno@x\.be/)).toHaveTextContent('+2')
  })

  it('shows the address alone when there is only one', () => {
    setup()

    expect(screen.getByText(/alice@x\.be/)).not.toHaveTextContent('+')
  })

  // Anchored: "2 / 2" contains a 2 as well, and printing it while nothing is filtered is exactly
  // the reading — something is hidden — the bare count exists to avoid.
  it('shows the bare total while nothing is being filtered', () => {
    setup()

    expect(screen.getByTestId('contact-count')).toHaveTextContent(/^2$/)
  })

  it('filters live as the user types, and updates the count', async () => {
    setup()

    await userEvent.type(screen.getByRole('searchbox'), 'dupont')

    expect(screen.getByText('Alice Dupont')).toBeInTheDocument()
    expect(screen.queryByText('Bruno Mertens')).not.toBeInTheDocument()
    expect(screen.getByTestId('contact-count')).toHaveTextContent('1 / 2')
  })

  it('finds a contact by an address that is not the primary', async () => {
    setup()

    await userEvent.type(screen.getByRole('searchbox'), 'wk.be')

    expect(screen.getByText('Bruno Mertens')).toBeInTheDocument()
  })

  it('reports the picked contact', async () => {
    const props = setup()

    await userEvent.click(screen.getByText('Bruno Mertens'))

    expect(props.onSelect).toHaveBeenCalledWith('b')
  })

  // `is-selected` is the hook the content-row paint hangs on — the selected fill plus an inset
  // accent bar, the opposite language from the navigation band. The paint itself is a CSS fact
  // jsdom computes nothing about; it is measured in the browser pass, Task 15.
  it('marks the selected tile with the content-row class', () => {
    setup({ selectedId: 'b' })

    expect(screen.getByTestId('contact-tile-b')).toHaveClass('is-selected')
    expect(screen.getByTestId('contact-tile-a')).not.toHaveClass('is-selected')
  })

  // Two things at once, and the label alone proves neither: it names the action to come, while
  // `is-on` is what actually lights the star.
  it('shows a lit star for a favourite and an unlit one otherwise', () => {
    setup()

    expect(screen.getByRole('button', { name: /remove alice dupont from favourites/i }))
      .toHaveClass('is-on')
    expect(screen.getByRole('button', { name: /add bruno mertens to favourites/i }))
      .not.toHaveClass('is-on')
  })

  // The star must not open the contact underneath it: two things would happen on one click.
  it('toggling the star does not select the contact', async () => {
    const props = setup()

    await userEvent.click(screen.getByRole('button', { name: /add bruno mertens to favourites/i }))

    expect(props.onToggleFavorite).toHaveBeenCalledWith(bruno)
    expect(props.onSelect).not.toHaveBeenCalled()
  })

  it('reports edit and delete without selecting', async () => {
    const props = setup()

    await userEvent.click(screen.getByRole('button', { name: /edit bruno mertens/i }))
    await userEvent.click(screen.getByRole('button', { name: /delete bruno mertens/i }))

    expect(props.onEdit).toHaveBeenCalledWith('b')
    expect(props.onDelete).toHaveBeenCalledWith(bruno)
    expect(props.onSelect).not.toHaveBeenCalled()
  })

  it('shows a muted line rather than a blank area when empty', () => {
    setup({ contacts: [] })

    expect(screen.getByText(/no contacts/i)).toBeInTheDocument()
  })

  it('says so when the filter matches nothing', async () => {
    setup()

    await userEvent.type(screen.getByRole('searchbox'), 'zzz')

    expect(screen.getByText(/no matching contacts/i)).toBeInTheDocument()
  })

  // The 13 tests above only check presence, so swapping the star and the action cluster in the
  // JSX would leave every one of them green. website-design.md fixes the tile anatomy left to
  // right — star, identifier, actions — so the DOM order itself has to be the assertion.
  it('keeps the tile anatomy in order: star, name, then actions', () => {
    setup()

    const line = screen.getByTestId('contact-tile-a').querySelector('.contact-tile-line')!
    const [first, second, third] = Array.from(line.children)

    expect(first).toHaveClass('contact-star')
    expect(second).toHaveClass('contact-tile-name')
    expect(third).toHaveClass('contact-tile-actions')
  })

  // Neither fixture contact lacks an address, so nothing above exercises this. Rendering the line
  // conditionally instead of always-but-empty would still pass every other test.
  it('still renders the address line, empty, for a contact with no address', () => {
    const noAddress = contact({ id: 'c', firstName: 'Chloé', lastName: 'Petit', addresses: [] })
    setup({ contacts: [alice, bruno, noAddress] })

    const address = screen.getByTestId('contact-tile-c').querySelector('.contact-tile-address')

    expect(address).not.toBeNull()
    expect(address).toHaveTextContent('')
  })

  it('selects the focused tile on Enter', () => {
    const props = setup()

    fireEvent.keyDown(screen.getByTestId('contact-tile-b'), { key: 'Enter' })

    expect(props.onSelect).toHaveBeenCalledWith('b')
  })

  it('selects the focused tile on Space', () => {
    const props = setup()

    fireEvent.keyDown(screen.getByTestId('contact-tile-b'), { key: ' ' })

    expect(props.onSelect).toHaveBeenCalledWith('b')
  })
})
