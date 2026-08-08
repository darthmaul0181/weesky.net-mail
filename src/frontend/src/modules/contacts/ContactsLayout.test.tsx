import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, MemoryRouter, Route, RouterProvider, Routes } from 'react-router-dom'
import { describe, expect, it, vi, beforeEach } from 'vitest'
import ContactsLayout from './ContactsLayout'
import type { Contact } from './contactTypes'

vi.mock('../../api.js', () => ({
  api: {
    getContacts: vi.fn(), createContact: vi.fn(), updateContact: vi.fn(),
    deleteContact: vi.fn(), setContactFavorite: vi.fn(),
    importContacts: vi.fn(), exportContacts: vi.fn(),
  },
  ApiError: class extends Error {},
}))
vi.mock('../../hooks/useAccountId', () => ({ useAccountId: () => 'primary' }))

const { api } = await import('../../api.js') as unknown as {
  api: Record<'getContacts' | 'createContact' | 'updateContact' | 'deleteContact'
    | 'setContactFavorite' | 'importContacts' | 'exportContacts', ReturnType<typeof vi.fn>>
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
