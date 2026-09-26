import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { focusablesIn, tabbablesIn } from '../../lib/layerStack'
import ContactList from './ContactList'
import { contactOf } from './contactTestHarness'
import type { Contact } from './contactTypes'

const contact = contactOf

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
    onDeleteMany: vi.fn().mockResolvedValue(true),
    deletingMany: false,
    ...overrides,
  }
  render(<ContactList {...props} />)
  return props
}

/** The cell that replaced the `role="button"` tile: it carries the name, the keys that open the
    card and, on the open contact, `aria-current`. */
function contentCell(id: string): HTMLElement {
  return screen.getByTestId(`contact-tile-${id}`)
    .querySelector('.contact-tile-content') as HTMLElement
}

/** A getter on the one field nothing but a tile reads: the list reads a contact's id and nothing
    else, and `matches` never looks at this one — so the count is the real component drawing. */
function watched(base: Contact) {
  const one = { ...base }
  const { isFavorite } = base
  const seen = { reads: 0 }
  Object.defineProperty(one, 'isFavorite', { get() { seen.reads += 1; return isFavorite } })
  return { contact: one, seen }
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
  // jsdom computes nothing about; it is measured in the browser pass.
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
  // the name takes the first line with the star closing it on the RIGHT — a cell of its own now,
  // the line it used to end being the content cell's — the address sits under them, and the cluster
  // is the tile's LAST child, out of the flow over the bottom line. The star back at the head of
  // the line is the page-tile idiom this list deliberately left.
  it('keeps the tile anatomy in order: the box, name then star, the address, then the actions', () => {
    setup()

    const [select, content, flag, actions] =
      Array.from(screen.getByTestId('contact-tile-a').children) as [Element, Element, Element, Element]
    const [line, address] = Array.from(content.children) as [Element, Element]

    expect(select.firstElementChild).toHaveClass('contact-tile-check')
    expect(line.firstElementChild).toHaveClass('contact-tile-name')
    expect(flag.firstElementChild).toHaveClass('contact-star')
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

    fireEvent.keyDown(contentCell('b'), { key: 'Enter' })

    expect(props.onSelect).toHaveBeenCalledWith('b')
  })

  it('selects the focused tile on Space', () => {
    const props = setup()

    fireEvent.keyDown(contentCell('b'), { key: ' ' })

    expect(props.onSelect).toHaveBeenCalledWith('b')
  })

  // The star and the actions sit in cells of their own, outside the content cell that carries
  // the key: these two pass by the tile's structure now, not by a target check of its own.
  it('does not open the tile when Enter is pressed on the star inside it', () => {
    const props = setup()
    const tile = screen.getByTestId('contact-tile-b')

    fireEvent.keyDown(within(tile).getByRole('button', { name: /favourite/i }), { key: 'Enter' })

    expect(props.onSelect).not.toHaveBeenCalled()
  })

  it('does not open the tile when Space is pressed on the edit button inside it', () => {
    const props = setup()
    const tile = screen.getByTestId('contact-tile-b')

    fireEvent.keyDown(within(tile).getByRole('button', { name: /edit/i }), { key: ' ' })

    expect(props.onSelect).not.toHaveBeenCalled()
  })

  it('does not open the tile when Enter is pressed on the checkbox inside it', () => {
    const props = setup()
    const tile = screen.getByTestId('contact-tile-b')

    fireEvent.keyDown(within(tile).getByRole('checkbox'), { key: 'Enter' })

    expect(props.onSelect).not.toHaveBeenCalled()
  })

  it('checks a contact and counts it in the band', async () => {
    setup()

    await userEvent.click(screen.getByLabelText('Select Alice Dupont'))

    expect(screen.getByText('1 selected')).toBeInTheDocument()
  })

  // Ticking does not open the card: one click would do two things.
  it('checking a contact does not open it', async () => {
    const props = setup()

    await userEvent.click(screen.getByLabelText('Select Alice Dupont'))

    expect(props.onSelect).not.toHaveBeenCalled()
  })

  // The master box acts on what is on screen, so on the filtered rows.
  it('selects every filtered row from the master box', async () => {
    setup()
    await userEvent.type(screen.getByRole('searchbox'), 'alice')
    await userEvent.click(screen.getByLabelText('Select all'))

    expect(screen.getByText('1 selected')).toBeInTheDocument()
  })

  // The field gives the band to the count, so the loupe is the only road to the search during a
  // selection: it clears the selection and gives the field back, rather than leaving the search
  // unreachable.
  it('gives the search field back from the loupe, clearing the selection', async () => {
    setup()
    await userEvent.click(screen.getByLabelText('Select Alice Dupont'))
    expect(screen.queryByRole('searchbox')).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Search contacts' }))

    expect(screen.queryByText(/selected/)).not.toBeInTheDocument()
    expect(screen.getByRole('searchbox')).toHaveFocus()
  })

  // The loupe is offered only when it serves a purpose: at rest the field is already there, and
  // two doors onto the same thing read as a defect.
  it('offers no loupe while the field itself is on the band', () => {
    setup()

    expect(screen.queryByRole('button', { name: 'Search contacts' })).not.toBeInTheDocument()
  })

  // A deliberate choice: resetKey includes the scope, so changing it clears the selection.
  it('clears the selection when the scope changes', async () => {
    const { rerender } = render(
      <ContactList contacts={[alice, bruno]} selectedId={null} scope="all"
        onSelect={vi.fn()} onToggleFavorite={vi.fn()} onEdit={vi.fn()} onDelete={vi.fn()}
        onDeleteMany={vi.fn().mockResolvedValue(true)} deletingMany={false} />)
    await userEvent.click(screen.getByLabelText('Select Alice Dupont'))
    expect(screen.getByText('1 selected')).toBeInTheDocument()

    rerender(
      <ContactList contacts={[alice, bruno]} selectedId={null} scope="favorites"
        onSelect={vi.fn()} onToggleFavorite={vi.fn()} onEdit={vi.fn()} onDelete={vi.fn()}
        onDeleteMany={vi.fn().mockResolvedValue(true)} deletingMany={false} />)

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

  // A19: the confirm used to close the moment the click fired, ahead of the delete it asked about.
  // It has to stay open — busy — until the promise it was handed settles, success or refusal alike.
  it('keeps the confirm open until the delete settles, then closes it', async () => {
    let resolveDelete: ((ok: boolean) => void) | undefined
    const onDeleteMany = vi.fn(() => new Promise<boolean>(resolve => { resolveDelete = resolve }))
    setup({ onDeleteMany })
    await userEvent.click(screen.getByLabelText('Select Alice Dupont'))
    await userEvent.click(screen.getByLabelText('Delete selection'))

    await userEvent.click(screen.getByRole('button', { name: 'Delete' }))

    expect(onDeleteMany).toHaveBeenCalledWith(['a'])
    expect(screen.getByText('Confirm deletion')).toBeInTheDocument()

    resolveDelete?.(true)
    await waitFor(() => expect(screen.queryByText('Confirm deletion')).not.toBeInTheDocument())
    // The selection is cleared with the same settle, not with the click that started it.
    expect(screen.queryByText(/selected/)).not.toBeInTheDocument()
  })

  // The `loading` prop is the caller's own pending flag (a mutation's `isPending`), not something
  // this list derives: it has to reach the shared modal so the button shows busy rather than idle.
  it('shows the confirm as busy while the caller reports the delete pending', async () => {
    setup({ deletingMany: true })
    await userEvent.click(screen.getByLabelText('Select Alice Dupont'))
    await userEvent.click(screen.getByLabelText('Delete selection'))

    const modal = screen.getByText('Confirm deletion').closest('.modal') as HTMLElement
    const confirmButton = modal.querySelector('.btn-danger-solid') as HTMLButtonElement

    expect(confirmButton).toBeDisabled()
    expect(confirmButton.querySelector('.spinner')).not.toBeNull()
  })

  // A refused batch must leave the selection standing: reselecting the same contacts by hand to
  // retry is a second chore the toast already told the user was needed once.
  it('closes the confirm on a refused delete but keeps the selection', async () => {
    const onDeleteMany = vi.fn().mockResolvedValue(false)
    setup({ onDeleteMany })
    await userEvent.click(screen.getByLabelText('Select Alice Dupont'))
    await userEvent.click(screen.getByLabelText('Delete selection'))

    await userEvent.click(screen.getByRole('button', { name: 'Delete' }))

    await waitFor(() => expect(screen.queryByText('Confirm deletion')).not.toBeInTheDocument())
    expect(screen.getByText('1 selected')).toBeInTheDocument()
  })

  it('leaves the delete action disabled while nothing is checked', () => {
    setup()

    expect(screen.getByLabelText('Delete selection')).toBeDisabled()
  })

  it('drags the whole selection when the grabbed tile belongs to it', () => {
    setup()
    fireEvent.click(screen.getByLabelText('Select Alice Dupont'))
    fireEvent.click(screen.getByLabelText('Select Bruno Mertens'))
    const setData = vi.fn<(format: string, data: string) => void>()

    fireEvent.dragStart(screen.getByTestId('contact-tile-a'),
      { dataTransfer: { setData, setDragImage: vi.fn() } })

    expect(JSON.parse(setData.mock.calls[0]![1])).toEqual({ ids: ['a', 'b'] })
  })

  // The pill is built once, at the start of the drag, and setDragImage never learns which row it
  // ends up over — so its label has to hold for every target this list drops onto, a group row
  // included, rather than naming the one drop it was written against.
  it('carries a neutral label on the drag pill, not the favourites one', () => {
    setup()
    const setDragImage = vi.fn()

    fireEvent.dragStart(screen.getByTestId('contact-tile-a'),
      { dataTransfer: { setData: vi.fn(), setDragImage } })

    const pill = setDragImage.mock.calls[0]![0] as HTMLElement
    expect(pill.textContent).toContain('Drag to a list')
    expect(pill.textContent).not.toMatch(/favourites/i)
  })

  // An unchecked tile leaves alone: dragging it must never disturb a selection made for
  // something else.
  it('drags an unchecked tile alone', () => {
    setup()
    fireEvent.click(screen.getByLabelText('Select Alice Dupont'))
    const setData = vi.fn<(format: string, data: string) => void>()

    fireEvent.dragStart(screen.getByTestId('contact-tile-b'),
      { dataTransfer: { setData, setDragImage: vi.fn() } })

    expect(JSON.parse(setData.mock.calls[0]![1])).toEqual({ ids: ['b'] })
  })

  // Two distinct labels: Delete keeps its dialogue and Remove from group has none — group
  // membership is restored by a simple drop, which is not a loss of data.
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

  /* The headline change, measured rather than assumed: one tick cost 130ms at 2000 tiles
     because every tile redrew. Nothing is mocked — a getter counts the real memoised component. */
  it('does not re-render the other tiles when one checkbox changes', async () => {
    const one = watched(alice)
    const other = watched(bruno)
    setup({ contacts: [one.contact, other.contact] })
    const [drewOne, drewOther] = [one.seen.reads, other.seen.reads]
    expect(drewOther).toBeGreaterThan(0)

    await userEvent.click(screen.getByLabelText('Select Alice Dupont'))

    expect(screen.getByText('1 selected')).toBeInTheDocument()
    expect(other.seen.reads).toBe(drewOther)
    expect(one.seen.reads).toBeGreaterThan(drewOne)
  })

  /* And the interaction the mail list does not have: `useSelection` is keyed on the query, so a
     letter used to rebuild the selection and redraw every tile that survived the filter — twice,
     the reset scheduling a second render of its own. */
  it('does not re-render the surviving tiles when a letter is typed in the search box', async () => {
    const one = watched(alice)
    const other = watched(bruno)
    setup({ contacts: [one.contact, other.contact] })
    const [drewOne, drewOther] = [one.seen.reads, other.seen.reads]

    // A letter both contacts match, so the grid keeps its length and nothing here is about a
    // shorter list.
    await userEvent.type(screen.getByRole('searchbox'), 'e')

    expect(screen.getAllByRole('row')).toHaveLength(2)
    expect(one.seen.reads).toBe(drewOne)
    expect(other.seen.reads).toBe(drewOther)
  })

  /* The content cell renders a constant tabIndex={-1} and the hook writes 0 onto the DOM itself,
     so a tile redrawing must not take the roving stop back. fireEvent.click, never userEvent:
     a real click would move focus to the checkbox and repoint the stop legitimately. */
  it("leaves the hook's tab stop alone when a tile re-renders", async () => {
    setup()
    const cell = contentCell('a')
    fireEvent.focus(cell)
    expect(cell).toHaveAttribute('tabindex', '0')

    fireEvent.click(screen.getByLabelText('Select Alice Dupont'))

    expect(screen.getByText('1 selected')).toBeInTheDocument()
    expect(cell).toHaveAttribute('tabindex', '0')
  })
})

/* -- The list as a grid ---------------------------------------------------------------------- */

/* The tile used to be one `role="button"` with `tabIndex={0}`: crossing the book cost one Tab press
   per contact, and that role being children-presentational, a reader was told the tile was one
   button and never heard of the checkbox, the star, the pencil or the trash plainly on screen. It
   is four `role="gridcell"`s in a `role="row"` now, walked by useGridNav — the message row's own
   shape, which this tile already mirrored on screen. */
describe('the list as a grid', () => {
  const grid = () => screen.getByRole('grid')
  const stopsIn = tabbablesIn
  const rowOf = (id: string) => screen.getByTestId(`contact-tile-${id}`)

  it('is a grid of contact rows', () => {
    setup()

    expect(grid()).toHaveClass('contact-tiles')
    expect(within(grid()).getAllByRole('row')).toHaveLength(2)
    expect(rowOf('a')).toHaveAttribute('role', 'row')
  })

  it('offers one tab stop for the whole list', () => {
    setup()

    // Five widgets a tile: the box, the content cell, the star, the pencil and the trash.
    expect(focusablesIn(grid())).toHaveLength(10)
    expect(stopsIn(grid())).toHaveLength(1)
  })

  it('puts every control in a gridcell, the pencil and the trash sharing one', () => {
    setup()
    const row = rowOf('a')
    // ARIA lets a row own nothing but cells, so every focusable part sits in one of its own.
    const cells = within(row).getAllByRole('gridcell')

    expect(cells).toHaveLength(4)
    const held = [
      within(row).getByRole('checkbox'),
      within(row).getByRole('button', { name: /from favourites/i }),
      within(row).getByRole('button', { name: /^edit alice dupont/i }),
      within(row).getByRole('button', { name: /^delete alice dupont/i }),
    ].map(control => control.closest('[role="gridcell"]'))
    // A Set, not four containments: the pencil and the trash DO share the actions cell, and
    // counting them would have passed on a row where the star shared it too.
    expect(held.every(cell => cells.includes(cell as HTMLElement))).toBe(true)
    expect(new Set(held).size).toBe(3)
  })

  /* Cells are walked in the order they are written, so that has to be the order they are drawn in:
     the star closes the name's line, the cluster sits on the corner below it. */
  it('writes the cells in the order they are drawn', () => {
    setup()

    expect(within(rowOf('a')).getAllByRole('gridcell').map(cell => cell.className))
      .toEqual(['contact-tile-select', 'contact-tile-content',
        'contact-tile-flag', 'contact-tile-actions'])
  })

  it('walks the tiles with the vertical arrows', () => {
    setup()
    contentCell('a').focus()

    fireEvent.keyDown(contentCell('a'), { key: 'ArrowDown' })
    expect(contentCell('b')).toHaveFocus()

    fireEvent.keyDown(document.activeElement as HTMLElement, { key: 'ArrowUp' })
    expect(contentCell('a')).toHaveFocus()
  })

  it('reaches the star and the actions with the horizontal arrows', () => {
    setup()
    contentCell('a').focus()

    fireEvent.keyDown(contentCell('a'), { key: 'ArrowRight' })
    expect(within(rowOf('a')).getByRole('button', { name: /remove alice dupont from favourites/i }))
      .toHaveFocus()

    fireEvent.keyDown(document.activeElement as HTMLElement, { key: 'ArrowRight' })
    expect(within(rowOf('a')).getByRole('button', { name: /^edit alice dupont/i })).toHaveFocus()

    fireEvent.keyDown(document.activeElement as HTMLElement, { key: 'End' })
    expect(within(rowOf('a')).getByRole('button', { name: /^delete alice dupont/i })).toHaveFocus()
  })

  // Tab into the list has to land where Enter means something. `aria-current` and never
  // `aria-selected`: the checkboxes are a real multi-selection here, so "selected" already means
  // something else to this list's user.
  it('opens the tab stop on the contact already open in the card', () => {
    setup({ selectedId: 'b' })

    expect(contentCell('b')).toHaveAttribute('aria-current', 'true')
    expect(contentCell('a')).not.toHaveAttribute('aria-current')
    expect(stopsIn(grid())).toEqual([contentCell('b')])
  })

  it('opens it on the first checkbox when no contact is open', () => {
    setup()

    expect(stopsIn(grid())).toEqual([within(rowOf('a')).getByRole('checkbox')])
  })

  it('keeps the checkbox selection and the band count', async () => {
    setup()

    await userEvent.click(within(rowOf('a')).getByRole('checkbox'))

    expect(screen.getByText('1 selected')).toBeInTheDocument()
  })

  it('keeps the drag handlers on the tile', () => {
    setup()
    const setData = vi.fn<(format: string, data: string) => void>()

    expect(rowOf('a')).toHaveAttribute('draggable', 'true')
    fireEvent.dragStart(rowOf('a'), { dataTransfer: { setData, setDragImage: vi.fn() } })

    expect(JSON.parse(setData.mock.calls[0]![1])).toEqual({ ids: ['a'] })
  })

  /* The filter is what makes this grid change length under the user's hands — `shown` is
     filterContacts(contacts, query) — so the tile holding the stop leaves while the caret is in the
     search field, and a stop left on a detached tile is a list Tab can no longer enter. */
  it('keeps a live tab stop when a filter removes the focused row', async () => {
    setup()
    within(rowOf('b')).getByRole('checkbox').focus()

    await userEvent.type(screen.getByRole('searchbox'), 'dupont')

    expect(screen.queryByTestId('contact-tile-b')).toBeNull()
    expect(stopsIn(grid())).toHaveLength(1)
    expect(rowOf('a')).toContainElement(stopsIn(grid())[0]!)
  })

  // No pager and every contact drawn, so a count here would be a lie about a list that is complete.
  it('carries no aria-rowcount: the list is complete', () => {
    setup()

    expect(grid()).not.toHaveAttribute('aria-rowcount')
    expect(rowOf('a')).not.toHaveAttribute('aria-rowindex')
  })

  // A filter matching nothing draws no grid at all: an empty `role="grid"` owns none of the rows
  // ARIA requires of it, and the hook has nothing to point a stop at.
  it('draws no grid when the filter matches nothing', async () => {
    setup()

    await userEvent.type(screen.getByRole('searchbox'), 'zzz')

    expect(screen.queryByRole('grid')).toBeNull()
  })
})
