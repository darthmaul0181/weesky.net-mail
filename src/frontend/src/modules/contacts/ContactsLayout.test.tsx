import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, MemoryRouter, Route, RouterProvider, Routes } from 'react-router-dom'
import { afterEach, describe, expect, it, vi, beforeEach } from 'vitest'
import ContactsLayout from './ContactsLayout'
import type { Contact } from './contactTypes'
import { mockViewport, resetViewport, settle } from '../../test-utils'

afterEach(resetViewport)

vi.mock('../../api.js', () => ({
  api: {
    getContacts: vi.fn(), createContact: vi.fn(), updateContact: vi.fn(),
    deleteContact: vi.fn(), setContactFavorite: vi.fn(),
    deleteContacts: vi.fn(), setContactsFavorite: vi.fn(),
    importContacts: vi.fn(), exportContacts: vi.fn(),
  },
  ApiError: class extends Error {},
}))
vi.mock('../../hooks/useAccountId', () => ({ useAccountId: () => 'primary' }))

const { api } = await import('../../api.js') as unknown as {
  api: Record<'getContacts' | 'createContact' | 'updateContact' | 'deleteContact'
    | 'setContactFavorite' | 'deleteContacts' | 'setContactsFavorite'
    | 'importContacts' | 'exportContacts', ReturnType<typeof vi.fn>>
}

function contact(fields: Partial<Contact> & { id: string }): Contact {
  return {
    firstName: null, lastName: null, nickname: null, isFavorite: false, addresses: [], ...fields,
  }
}

function renderAt(path: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/contacts" element={<ContactsLayout />} />
          <Route path="/contacts/new" element={<ContactsLayout />} />
          <Route path="/contacts/:id/edit" element={<ContactsLayout />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

const routes = [
  { path: '/contacts', element: <ContactsLayout /> },
  { path: '/contacts/new', element: <ContactsLayout /> },
  { path: '/contacts/:id/edit', element: <ContactsLayout /> },
]

/** Both edit routes are the same route object, so the layout is not remounted between them —
    which is what makes the editor's own key the only thing that can reseed the form. */
function renderRouter(path: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter(routes, { initialEntries: [path] })
  render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>)
  return router
}

function goTo(router: ReturnType<typeof createMemoryRouter>, path: string) {
  return act(async () => { await router.navigate(path) })
}

/** Every tile's star is named "… to favourites" as well: the scope lives in the band alone. */
function scopeButton(name: RegExp) {
  return within(screen.getByRole('navigation')).getByRole('button', { name })
}

/** The card and the confirm dialog both carry a button named exactly "Delete". */
function confirmDeletion() {
  const modal = screen.getByText('Confirm deletion').closest('.modal') as HTMLElement
  return userEvent.click(within(modal).getByRole('button', { name: /^delete$/i }))
}

describe('ContactsLayout', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    // Two favourites, and one of them is not the first sorted row: with a single favourite the
    // scope filter is indistinguishable from "keep the first row" — the sort files favourites first.
    api.getContacts.mockResolvedValue({
      contacts: [
        contact({ id: 'a', firstName: 'Alice', isFavorite: true, addresses: ['alice@x.be'] }),
        contact({ id: 'b', firstName: 'Bruno', addresses: ['bruno@x.be'] }),
        contact({ id: 'c', firstName: 'Carla', isFavorite: true, addresses: ['carla@x.be'] }),
      ],
    })
  })

  it('counts the whole book and its favourites in the band', async () => {
    renderAt('/contacts')

    await waitFor(() =>
      expect(scopeButton(/all contacts/i)).toHaveTextContent('3'))
    expect(scopeButton(/favourites/i)).toHaveTextContent('2')
  })

  // The editor swaps the two content columns and leaves the band standing — the mechanism
  // /mail/compose uses, so a scope stays one click away while a contact is being written.
  it('swaps the content columns for the editor and keeps the band', async () => {
    renderAt('/contacts/new')

    await waitFor(() => expect(scopeButton(/all contacts/i)).toBeInTheDocument())
    expect(screen.queryByTestId('contact-list')).not.toBeInTheDocument()
    expect(screen.getByTestId('contact-editor')).toBeInTheDocument()
  })

  it('shows list and card outside the editor routes', async () => {
    renderAt('/contacts')

    await waitFor(() => expect(screen.getByTestId('contact-list')).toBeInTheDocument())
    expect(screen.getByTestId('contact-card')).toBeInTheDocument()
    expect(screen.queryByTestId('contact-editor')).not.toBeInTheDocument()
  })

  it('narrows the list to favourites when that scope is picked', async () => {
    renderAt('/contacts')
    await waitFor(() => expect(screen.getByText('Alice')).toBeInTheDocument())

    await userEvent.click(scopeButton(/favourites/i))

    expect(screen.getByText('Alice')).toBeInTheDocument()
    expect(screen.getByText('Carla')).toBeInTheDocument()
    expect(screen.queryByText('Bruno')).not.toBeInTheDocument()
  })

  it('opens the picked contact in the card', async () => {
    renderAt('/contacts')
    await waitFor(() => expect(screen.getByText('Bruno')).toBeInTheDocument())

    await userEvent.click(screen.getByText('Bruno'))

    expect(screen.getByRole('heading', { name: 'Bruno' })).toBeInTheDocument()
  })

  it('toggles a favourite through the API and keeps the card open', async () => {
    api.setContactFavorite.mockResolvedValue(undefined)
    renderAt('/contacts')
    await waitFor(() => expect(screen.getByText('Bruno')).toBeInTheDocument())
    await userEvent.click(screen.getByText('Bruno'))

    await userEvent.click(screen.getByRole('button', { name: /add bruno to favourites/i }))

    await waitFor(() => expect(api.setContactFavorite).toHaveBeenCalledWith('b', true))
    // The star is not a navigation: the contact it belongs to stays open behind it.
    expect(screen.getByRole('heading', { name: 'Bruno' })).toBeInTheDocument()
  })

  // Deleting never happens on the first click anywhere in this app.
  it('confirms before deleting', async () => {
    api.deleteContact.mockResolvedValue(undefined)
    renderAt('/contacts')
    await waitFor(() => expect(screen.getByText('Bruno')).toBeInTheDocument())

    await userEvent.click(screen.getByRole('button', { name: /delete bruno/i }))
    expect(api.deleteContact).not.toHaveBeenCalled()

    await confirmDeletion()

    await waitFor(() => expect(api.deleteContact).toHaveBeenCalledWith('b'))
  })

  // Le lot passe par un seul appel, et un refus laisse l'écran sur l'état du serveur en le disant.
  it('deletes the selection and toasts on refusal', async () => {
    api.deleteContacts.mockRejectedValueOnce(new Error('refused'))
    renderAt('/contacts')
    await waitFor(() => expect(screen.getByText('Alice')).toBeInTheDocument())

    await userEvent.click(screen.getByLabelText('Select Alice'))
    await userEvent.click(screen.getByLabelText('Delete selection'))
    await confirmDeletion()

    await waitFor(() => expect(api.deleteContacts).toHaveBeenCalledWith(['a']))
    expect(await screen.findByText(/could not be deleted/i)).toBeInTheDocument()
  })

  it('creates a contact and returns to the list', async () => {
    api.createContact.mockResolvedValue({ id: 'n' })
    renderAt('/contacts/new')
    await waitFor(() => expect(screen.getByRole('heading', { name: /new contact/i })).toBeInTheDocument())

    await userEvent.type(screen.getByLabelText(/first name/i), 'Chloé')
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    await waitFor(() => expect(api.createContact).toHaveBeenCalledWith(
      expect.objectContaining({ firstName: 'Chloé' })))
    // Saved is only half of it: a save that left the editor standing would strand the user in a
    // form whose contact already exists.
    await waitFor(() => expect(screen.getByTestId('contact-list')).toBeInTheDocument())
    expect(screen.queryByTestId('contact-editor')).not.toBeInTheDocument()
  })

  // The other half of save(): an edit route amends the contact it names, it never posts a new one.
  it('saves an edit through the update endpoint, on the contact named in the route', async () => {
    api.updateContact.mockResolvedValue(undefined)
    renderAt('/contacts/b/edit')
    await waitFor(() => expect(screen.getByLabelText(/first name/i)).toHaveValue('Bruno'))

    await userEvent.type(screen.getByLabelText(/last name/i), 'Weiss')
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    await waitFor(() => expect(api.updateContact).toHaveBeenCalledWith('b', {
      firstName: 'Bruno', lastName: 'Weiss', nickname: null, isFavorite: false,
      addresses: ['bruno@x.be'],
    }))
    expect(api.createContact).not.toHaveBeenCalled()
  })

  // One edit route straight after another: the layout stays mounted, so only the editor's key can
  // stop the previous contact's values from standing in the next one's form.
  it('reseeds the form when one edit route follows another', async () => {
    const router = renderRouter('/contacts/b/edit')
    await waitFor(() => expect(screen.getByLabelText(/first name/i)).toHaveValue('Bruno'))

    await goTo(router, '/contacts/a/edit')

    await waitFor(() => expect(screen.getByLabelText(/first name/i)).toHaveValue('Alice'))
  })

  // A refusal belongs to the form it happened in: the next contact's editor must open clean.
  // Server prose never reaches the alert; the local fallback does — see apiErrorMessage.
  it('does not carry a refused save into the next contact edited', async () => {
    api.updateContact.mockRejectedValue(new Error("'nope' is not a valid email address"))
    const router = renderRouter('/contacts/b/edit')
    await waitFor(() => expect(screen.getByLabelText(/first name/i)).toHaveValue('Bruno'))
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Could not save the contact'))

    await goTo(router, '/contacts')
    await goTo(router, '/contacts/a/edit')

    await waitFor(() => expect(screen.getByLabelText(/first name/i)).toHaveValue('Alice'))
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('seeds the editor from the contact named in the route', async () => {
    renderAt('/contacts/b/edit')

    await waitFor(() => expect(screen.getByLabelText(/first name/i)).toHaveValue('Bruno'))
  })

  // A refused save has to leave the user in the form with the reason, never bounce them back to
  // a list that silently kept nothing. Server prose never reaches the alert; the local fallback
  // does — see apiErrorMessage.
  it('keeps the editor open and shows the local fallback when a save is refused', async () => {
    api.createContact.mockRejectedValue(new Error("'nope' is not a valid email address"))
    renderAt('/contacts/new')
    await waitFor(() => expect(screen.getByLabelText(/first name/i)).toBeInTheDocument())

    await userEvent.type(screen.getByLabelText(/first name/i), 'Bruno')
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Could not save the contact'))
    expect(screen.getByRole('heading', { name: /new contact/i })).toBeInTheDocument()
  })

  // The open card must not survive its contact: the book still answers with Bruno on the refetch,
  // so only dropping the selected id can close the card.
  it('closes the card when the contact it shows is deleted', async () => {
    api.deleteContact.mockResolvedValue(undefined)
    renderAt('/contacts')
    await waitFor(() => expect(screen.getByText('Bruno')).toBeInTheDocument())
    await userEvent.click(screen.getByText('Bruno'))

    await userEvent.click(screen.getByRole('button', { name: /delete bruno/i }))
    await confirmDeletion()

    await waitFor(() => expect(api.deleteContact).toHaveBeenCalledWith('b'))
    await waitFor(() =>
      expect(screen.queryByRole('heading', { name: 'Bruno' })).not.toBeInTheDocument())
  })

  // An obsolete bookmark must not turn an edit route into a create: saving from there would
  // fabricate a second contact while the user believes they are amending one.
  it('sends an edit route naming no known contact back to the list', async () => {
    renderAt('/contacts/zzz/edit')

    await waitFor(() => expect(screen.getByTestId('contact-list')).toBeInTheDocument())
    expect(screen.queryByRole('heading', { name: /new contact/i })).not.toBeInTheDocument()
    expect(screen.getByText(/contact not found/i)).toBeInTheDocument()
  })

  // No route id at all stays a perfectly valid create — only an id resolving to nothing is at fault.
  it('opens an empty create form on the new-contact route', async () => {
    renderAt('/contacts/new')

    await waitFor(() =>
      expect(screen.getByRole('heading', { name: /new contact/i })).toBeInTheDocument())
    expect(screen.getByLabelText(/first name/i)).toHaveValue('')
    expect(screen.queryByText(/contact not found/i)).not.toBeInTheDocument()
  })

  // A contact filtered out of the new scope must not stay open, the same reason choosing a folder
  // drops the open message's uid.
  it('drops the open contact when the scope changes', async () => {
    renderAt('/contacts')
    await waitFor(() => expect(screen.getByText('Bruno')).toBeInTheDocument())
    await userEvent.click(screen.getByText('Bruno'))
    expect(screen.getByRole('heading', { name: 'Bruno' })).toBeInTheDocument()

    await userEvent.click(scopeButton(/favourites/i))

    expect(screen.queryByRole('heading', { name: 'Bruno' })).not.toBeInTheDocument()
    expect(screen.getByText(/select a contact/i)).toBeInTheDocument()
  })
})

describe('the transfer footer', () => {
  it('offers import and export under the scopes', async () => {
    api.getContacts.mockResolvedValue({ contacts: [contact({ id: '1', firstName: 'Bruno' })] })
    renderAt('/contacts')

    expect(await screen.findByRole('button', { name: 'Import…' })).toBeInTheDocument()
    // Export starts disabled: it depends on the same book, which the footer does not wait for.
    await waitFor(() => expect(screen.getByRole('button', { name: 'Export' })).toBeEnabled())
  })

  // The editor takes the two content columns and leaves the band standing, footer included — the
  // same rule the scopes follow.
  it('keeps the footer while the editor is open', async () => {
    api.getContacts.mockResolvedValue({ contacts: [] })
    renderAt('/contacts/new')

    expect(await screen.findByRole('button', { name: 'Import…' })).toBeInTheDocument()
  })

  // Server prose never reaches the toast; the local fallback does — see apiErrorMessage.
  it('surfaces an import failure carrying no code as the local fallback toast', async () => {
    api.getContacts.mockResolvedValue({ contacts: [] })
    api.importContacts.mockRejectedValue(new Error('No recognised column in this file.'))
    renderAt('/contacts')

    await screen.findByRole('button', { name: 'Import…' })
    fireEvent.change(screen.getByTestId('contacts-import-input'),
      { target: { files: [new File(['x'], 'contacts.csv', { type: 'text/csv' })] } })

    expect(await screen.findByText('Could not import the file')).toBeInTheDocument()
  })

  // csv_no_recognised_column is a named stable code: the refusal must stay specific, translated
  // rather than shown as the generic fallback above.
  it('surfaces the translated csv_no_recognised_column toast', async () => {
    api.getContacts.mockResolvedValue({ contacts: [] })
    api.importContacts.mockRejectedValue(
      Object.assign(new Error('csv_no_recognised_column'), { code: 'csv_no_recognised_column' }))
    renderAt('/contacts')

    await screen.findByRole('button', { name: 'Import…' })
    fireEvent.change(screen.getByTestId('contacts-import-input'),
      { target: { files: [new File(['x'], 'contacts.csv', { type: 'text/csv' })] } })

    expect(await screen.findByText(
      'No recognised column in this file. It needs a header row naming a name or an e-mail column.'))
      .toBeInTheDocument()
  })

  // Settled, not success: a refused import must leave the screen on the server's book.
  it('refetches the book after an import, refused or not', async () => {
    api.getContacts.mockResolvedValue({ contacts: [] })
    api.importContacts.mockRejectedValue(new Error('nope'))
    renderAt('/contacts')

    await screen.findByRole('button', { name: 'Import…' })
    api.getContacts.mockClear()
    fireEvent.change(screen.getByTestId('contacts-import-input'),
      { target: { files: [new File(['x'], 'contacts.csv', { type: 'text/csv' })] } })

    await waitFor(() => expect(api.getContacts).toHaveBeenCalled())
  })
})

/** `ContactsLayout` holds a loading line until `useContacts()` answers, and every case below reads
    something the book produced. `settle()` drains a single macrotask — it covered that query on an
    idle machine and raced it under load, which is exactly the distinction CLAUDE.md draws between
    the two; it stays the right tool only where the assertion is that nothing happened. */
const bookLoaded = () => screen.findAllByText(/^(Alice|Bruno)$/)

describe('ContactsLayout on a phone', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.getContacts.mockResolvedValue({
      contacts: [
        contact({ id: 'a', firstName: 'Alice', isFavorite: true, addresses: ['alice@x.be'] }),
        contact({ id: 'b', firstName: 'Bruno', addresses: ['bruno@x.be'] }),
      ],
    })
  })

  it('puts the scope column in a drawer and renders no splitter', async () => {
    mockViewport('phone')
    const { container } = renderAt('/contacts')
    await bookLoaded()

    expect(container.querySelector('.context-drawer .contacts-scopes-column')).toBeTruthy()
    // Once, not twice: an implementation drawing it inline as well would pass the line above.
    expect(container.querySelectorAll('.contacts-scopes-column')).toHaveLength(1)
    expect(container.querySelector('.pane-splitter')).toBeNull()
  })

  it('shows the list alone until a contact is picked', async () => {
    mockViewport('phone')
    const { container } = renderAt('/contacts')
    await bookLoaded()

    expect(container.querySelector('[data-testid="contact-list"]')).toBeTruthy()
    expect(container.querySelector('[data-testid="contact-list"]')).not.toHaveClass('is-hidden')
    expect(container.querySelector('[data-testid="contact-card"]')).toBeNull()
  })

  // The other half of taking turns: the card owns the screen once an id is in the URL, or the two
  // would share a 360px row and neither would be readable. Hidden, never unmounted — MailLayout's
  // rule, and the next case is what it buys.
  it('gives the card the screen once a contact is picked', async () => {
    mockViewport('phone')
    const { container } = renderAt('/contacts?id=b')
    await bookLoaded()

    expect(container.querySelector('[data-testid="contact-card"]')).toBeTruthy()
    expect(container.querySelector('[data-testid="contact-list"]')).toHaveClass('is-hidden')
  })

  // The search lives in ContactList's own state: unmounting the column to show the card throws it
  // away, and the user comes back to the whole book with their query gone.
  it('keeps the list search across opening a contact and coming back', async () => {
    mockViewport('phone')
    renderAt('/contacts')
    // findBy, not settle(): the heading holding this box only exists once `useContacts()` has
    // answered, and settle() drains one macrotask — it covered the query on this machine and
    // raced it under load, which is the distinction CLAUDE.md draws between the two.
    await userEvent.type(await screen.findByLabelText(/search contacts/i), 'bru')
    expect(screen.queryByText('Alice')).not.toBeInTheDocument()

    await userEvent.click(screen.getByText('Bruno'))
    await userEvent.click(screen.getByRole('button', { name: /back to the list/i }))

    expect(screen.getByLabelText(/search contacts/i)).toHaveValue('bru')
    expect(screen.queryByText('Alice')).not.toBeInTheDocument()
  })

  // The card replaced the list, and with it the hamburger: without a ← the only ways out are the
  // browser's own Back and the tab bar.
  it('gives the card a way back, on the button and on Escape', async () => {
    mockViewport('phone')
    const { container } = renderAt('/contacts?id=b')
    await bookLoaded()
    expect(screen.getByRole('heading', { name: 'Bruno' })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: /back to the list/i }))
    expect(container.querySelector('[data-testid="contact-list"]')).not.toHaveClass('is-hidden')

    await userEvent.click(screen.getByText('Bruno'))
    expect(container.querySelector('[data-testid="contact-list"]')).toHaveClass('is-hidden')
    fireEvent.keyDown(window, { key: 'Escape' })

    expect(container.querySelector('[data-testid="contact-list"]')).not.toHaveClass('is-hidden')
  })

  // A scope survives the trip too: backing out of a favourite must not silently widen the list to
  // the whole book.
  it('keeps the scope when the card is closed', async () => {
    mockViewport('phone')
    renderAt('/contacts?scope=favorites&id=a')
    await bookLoaded()

    await userEvent.click(screen.getByRole('button', { name: /back to the list/i }))

    expect(screen.getByText('Alice')).toBeInTheDocument()
    expect(screen.queryByText('Bruno')).not.toBeInTheDocument()
  })

  // The confirm owns Escape while it is open: backing out from under it would leave a dialog
  // acting on a contact the screen no longer shows.
  it('withholds the card back while the delete confirm is open', async () => {
    mockViewport('phone')
    const { container } = renderAt('/contacts?id=b')
    await bookLoaded()
    await userEvent.click(screen.getByRole('button', { name: /^delete$/i }))

    expect(screen.getByText('Confirm deletion')).toBeInTheDocument()
    fireEvent.keyDown(window, { key: 'Escape' })

    expect(container.querySelector('[data-testid="contact-list"]')).toHaveClass('is-hidden')
  })

  // Desktop never draws it: the list is right there, so a ← would be a second way to do nothing.
  it('draws no back control on a desktop', async () => {
    mockViewport('desktop')
    renderAt('/contacts?id=b')
    await bookLoaded()

    expect(screen.queryByRole('button', { name: /back to the list/i })).not.toBeInTheDocument()
  })

  // The hamburger is the only way back to the scopes once the column is behind the drawer, and it
  // is the list heading that has to carry it — the column it used to sit beside is gone.
  it('hands the list the hamburger and opens the drawer with it', async () => {
    mockViewport('phone')
    const { container } = renderAt('/contacts')
    await bookLoaded()

    await userEvent.click(screen.getByRole('button', { name: /open navigation/i }))

    expect(container.querySelector('.context-drawer.is-open')).toBeTruthy()
  })

  // The column's own Add button is behind the drawer from here down, so the floating one is the
  // module's primary action — except over the editor, which is already that surface.
  it('floats the add action outside the editor and withholds it inside', async () => {
    mockViewport('phone')
    const { container, unmount } = renderAt('/contacts')
    await bookLoaded()
    expect(container.querySelector('.floating-action')).toBeTruthy()
    unmount()

    const editor = renderAt('/contacts/new')
    await settle()

    expect(editor.container.querySelector('.floating-action')).toBeNull()
  })

  // It is anchored 73px up from an edge the card's own action band now owns, so it would sit over
  // Modifier and Supprimer. Withheld rather than nudged: the card has replaced the list, and the
  // add action belongs to the list. Above the phone tier the card sits beside it and both stay.
  it('withholds the floating add while a card owns the phone screen', async () => {
    mockViewport('phone')
    const { container, unmount } = renderAt('/contacts?id=b')
    await bookLoaded()
    expect(container.querySelector('.floating-action')).toBeNull()
    expect(container.querySelector('.actionbar')).toBeTruthy()
    unmount()

    mockViewport('desktop')
    const wide = renderAt('/contacts?id=b')
    await bookLoaded()

    expect(wide.container.querySelector('.floating-action')).toBeTruthy()
    expect(wide.container.querySelector('.actionbar')).toBeNull()
  })

  it('keeps the scope column inline on a desktop', async () => {
    mockViewport('desktop')
    const { container } = renderAt('/contacts')
    await bookLoaded()

    expect(container.querySelector('.context-drawer')).toBeNull()
    expect(container.querySelector('.contacts-layout > .contacts-scopes-column')).toBeTruthy()
    expect(container.querySelector('.pane-splitter')).toBeTruthy()
  })
})
