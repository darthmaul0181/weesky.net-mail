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
    contacts: [alice, bruno], selectedId: null, scope: 'all',
    onSelect: vi.fn(), onToggleFavorite: vi.fn(), onEdit: vi.fn(), onDelete: vi.fn(),
    onDeleteMany: vi.fn(),
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

  // `contacts` already arrives scoped, so an empty group is not the whole book being empty — "No
  // contacts yet" reads as though the group had never held anybody, with "All contacts" full one
  // column to the left.
  it('names the scope in the empty line under an empty group', () => {
    setup({ contacts: [], scope: 'group:g1' })

    expect(screen.getByText('This group has no members yet')).toBeInTheDocument()
    expect(screen.queryByText('No contacts yet')).not.toBeInTheDocument()
  })

  it('names the scope in the empty line under empty favourites', () => {
    setup({ contacts: [], scope: 'favorites' })

    expect(screen.getByText('No favourites yet')).toBeInTheDocument()
    expect(screen.queryByText('No contacts yet')).not.toBeInTheDocument()
  })

  it('says so when the filter matches nothing', async () => {
    setup()

    await userEvent.type(screen.getByRole('searchbox'), 'zzz')

    expect(screen.getByText(/no matching contacts/i)).toBeInTheDocument()
  })

  // The 13 tests above only check presence, so moving the star or the action cluster in the JSX
  // would leave every one of them green. The anatomy is the assertion, and it is the message row's:
  // the name takes the first line with the star closing it on the RIGHT, the address sits under
  // them, and the cluster is the tile's LAST child — out of the flow over the bottom line. The star
  // back at the head of the line is the page-tile idiom this list deliberately left.
  it('keeps the tile anatomy in order: the box, name then star, the address, then the actions', () => {
    setup()

    const [check, line, address, actions] = Array.from(screen.getByTestId('contact-tile-a').children)
    const [name, star] = Array.from(line.children)

    expect(check).toHaveClass('contact-tile-check')
    expect(name).toHaveClass('contact-tile-name')
    expect(star).toHaveClass('contact-star')
    expect(address).toHaveClass('contact-tile-address')
    expect(actions).toHaveClass('contact-tile-actions')
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

  it('checks a contact and counts it in the band', async () => {
    setup()

    await userEvent.click(screen.getByLabelText('Select Alice Dupont'))

    expect(screen.getByText('1 selected')).toBeInTheDocument()
  })

  // Cocher n'ouvre pas la fiche : deux choses se produiraient sur un clic.
  it('checking a contact does not open it', async () => {
    const props = setup()

    await userEvent.click(screen.getByLabelText('Select Alice Dupont'))

    expect(props.onSelect).not.toHaveBeenCalled()
  })

  // La case maîtresse porte sur ce qui est à l'écran, donc sur les lignes filtrées.
  it('selects every filtered row from the master box', async () => {
    setup()
    await userEvent.type(screen.getByRole('searchbox'), 'alice')
    await userEvent.click(screen.getByLabelText('Select all'))

    expect(screen.getByText('1 selected')).toBeInTheDocument()
  })

  // Le champ cède la bande au décompte, donc la loupe est le seul chemin vers la recherche pendant
  // une sélection : elle la vide et rend le champ, plutôt que de laisser la recherche inatteignable.
  it('gives the search field back from the loupe, clearing the selection', async () => {
    setup()
    await userEvent.click(screen.getByLabelText('Select Alice Dupont'))
    expect(screen.queryByRole('searchbox')).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Search contacts' }))

    expect(screen.queryByText(/selected/)).not.toBeInTheDocument()
    expect(screen.getByRole('searchbox')).toHaveFocus()
  })

  // La loupe n'est offerte que quand elle sert : au repos le champ est déjà là, et deux portes vers
  // la même chose se lisent comme un défaut.
  it('offers no loupe while the field itself is on the band', () => {
    setup()

    expect(screen.queryByRole('button', { name: 'Search contacts' })).not.toBeInTheDocument()
  })

  // Choix assumé : resetKey inclut le scope, donc en changer vide la sélection.
  it('clears the selection when the scope changes', async () => {
    const { rerender } = render(
      <ContactList contacts={[alice, bruno]} selectedId={null} scope="all"
        onSelect={vi.fn()} onToggleFavorite={vi.fn()} onEdit={vi.fn()} onDelete={vi.fn()}
        onDeleteMany={vi.fn()} />)
    await userEvent.click(screen.getByLabelText('Select Alice Dupont'))
    expect(screen.getByText('1 selected')).toBeInTheDocument()

    rerender(
      <ContactList contacts={[alice, bruno]} selectedId={null} scope="favorites"
        onSelect={vi.fn()} onToggleFavorite={vi.fn()} onEdit={vi.fn()} onDelete={vi.fn()}
        onDeleteMany={vi.fn()} />)

    expect(screen.queryByText(/selected/)).not.toBeInTheDocument()
  })

  it('asks for confirmation before deleting a selection', async () => {
    const props = setup()
    await userEvent.click(screen.getByLabelText('Select Alice Dupont'))
    await userEvent.click(screen.getByLabelText('Delete selection'))

    expect(props.onDeleteMany).not.toHaveBeenCalled()
    await userEvent.click(screen.getByRole('button', { name: 'Delete' }))
    expect(props.onDeleteMany).toHaveBeenCalledWith(['a'])
  })

  it('leaves the delete action disabled while nothing is checked', () => {
    setup()

    expect(screen.getByLabelText('Delete selection')).toBeDisabled()
  })

  it('drags the whole selection when the grabbed tile belongs to it', () => {
    setup()
    fireEvent.click(screen.getByLabelText('Select Alice Dupont'))
    fireEvent.click(screen.getByLabelText('Select Bruno Mertens'))
    const setData = vi.fn()

    fireEvent.dragStart(screen.getByTestId('contact-tile-a'),
      { dataTransfer: { setData, setDragImage: vi.fn() } })

    expect(JSON.parse(setData.mock.calls[0][1])).toEqual({ ids: ['a', 'b'] })
  })

  // The pill is built once, at the start of the drag, and setDragImage never learns which row it
  // ends up over — so its label has to hold for every target this list drops onto, a group row
  // included, rather than naming the one drop it was written against.
  it('carries a neutral label on the drag pill, not the favourites one', () => {
    setup()
    const setDragImage = vi.fn()

    fireEvent.dragStart(screen.getByTestId('contact-tile-a'),
      { dataTransfer: { setData: vi.fn(), setDragImage } })

    const pill = setDragImage.mock.calls[0][0] as HTMLElement
    expect(pill.textContent).toContain('Drag to a list')
    expect(pill.textContent).not.toMatch(/favourites/i)
  })

  // Une tuile non cochée part seule : glisser ne doit jamais déranger une sélection faite pour
  // autre chose.
  it('drags an unchecked tile alone', () => {
    setup()
    fireEvent.click(screen.getByLabelText('Select Alice Dupont'))
    const setData = vi.fn()

    fireEvent.dragStart(screen.getByTestId('contact-tile-b'),
      { dataTransfer: { setData, setDragImage: vi.fn() } })

    expect(JSON.parse(setData.mock.calls[0][1])).toEqual({ ids: ['b'] })
  })

  // Le parent a besoin de la sélection pour le glisser-déposer, et il la reçoit dans l'ordre de
  // l'écran plutôt que dans celui des clics.
  it('reports the selection to its parent', async () => {
    const onSelectionChange = vi.fn()
    setup({ onSelectionChange })
    await userEvent.click(screen.getByLabelText('Select Bruno Mertens'))
    await userEvent.click(screen.getByLabelText('Select Alice Dupont'))

    expect(onSelectionChange).toHaveBeenLastCalledWith(['a', 'b'])
  })

  // Décision 14 : deux libellés distincts, Delete garde son dialogue et Remove from group n'en a
  // pas — l'appartenance à un groupe se remet d'un simple drop, ce n'est pas une perte de données.
  it('shows Remove from group beside Delete only when the caller wires a group scope', () => {
    setup({ scope: 'group:g1', onRemoveFromGroup: vi.fn() })

    expect(screen.getByRole('button', { name: 'Remove from group' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Delete selection' })).toBeInTheDocument()
  })

  it('offers no Remove from group action outside a group scope', () => {
    setup()

    expect(screen.queryByRole('button', { name: 'Remove from group' })).not.toBeInTheDocument()
  })

  it('removes the selection from the group without a dialog, and clears the selection', async () => {
    const onRemoveFromGroup = vi.fn()
    setup({ scope: 'group:g1', onRemoveFromGroup })
    await userEvent.click(screen.getByLabelText('Select Alice Dupont'))

    await userEvent.click(screen.getByRole('button', { name: 'Remove from group' }))

    expect(onRemoveFromGroup).toHaveBeenCalledWith(['a'])
    expect(screen.queryByText(/selected/)).not.toBeInTheDocument()
    expect(screen.queryByText(/delete this contact/i)).not.toBeInTheDocument()
  })
})
