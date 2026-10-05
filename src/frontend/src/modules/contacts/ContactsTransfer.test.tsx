import { QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ContactsTransfer from './ContactsTransfer'
import type { Contact } from './contactTypes'
import { createTestQueryClient, setupUser } from '../../test-utils'

let user: ReturnType<typeof setupUser>
beforeEach(() => { user = setupUser() })

vi.mock('../../api.js', () => ({
  api: { importContacts: vi.fn(), exportContacts: vi.fn() },
  ApiError: class extends Error {},
}))
vi.mock('../../hooks/useAccountId', () => ({ useAccountId: () => 'primary' }))
vi.mock('../../lib/downloadBlob', () => ({ downloadBlob: vi.fn() }))

const { api } = await import('../../api.js') as unknown as {
  api: Record<'importContacts' | 'exportContacts', ReturnType<typeof vi.fn>>
}
const { downloadBlob } = await import('../../lib/downloadBlob') as unknown as {
  downloadBlob: ReturnType<typeof vi.fn>
}
const { ApiError } = await import('../../api.js') as unknown as {
  ApiError: new (message: string) => Error
}

const book: Contact[] = [
  { id: '1', firstName: 'Bruno', isFavorite: false, addresses: [] },
]

function renderTransfer(contacts: Contact[] | undefined, onError = vi.fn()) {
  const client = createTestQueryClient({ mutations: { retry: false } })
  render(
    <QueryClientProvider client={client}>
      <ContactsTransfer contacts={contacts} onError={onError}
        triggerClassName="btn btn-primary column-actions-square" />
    </QueryClientProvider>,
  )
  return onError
}

// DropdownMenu mounts its rows only while open, so every assertion about them opens it first.
async function openMenu() {
  await user.click(screen.getByRole('button', { name: 'Import and export' }))
}

// The input is hidden, so user.upload cannot reach it; the change event is what the component
// actually listens to.
function choose(file: File) {
  const input = screen.getByTestId<HTMLInputElement>('contacts-import-input')
  fireEvent.change(input, { target: { files: [file] } })
  return input
}

describe('ContactsTransfer', () => {
  beforeEach(() => vi.clearAllMocks())

  it('sends the chosen file and shows the report', async () => {
    api.importContacts.mockResolvedValue({
      created: 3, merged: 1, skipped: 0, failed: 0, totalErrors: 0, errors: [],
    })
    renderTransfer(book)

    const file = new File(['First Name\r\nBruno'], 'contacts.csv', { type: 'text/csv' })
    choose(file)

    await waitFor(() => expect(api.importContacts).toHaveBeenCalledWith(file))
    expect(await screen.findByText('3')).toBeInTheDocument()
  })

  // Without clearing it, choosing the same file twice fires no change event at all.
  it('clears the input so the same file can be chosen twice', async () => {
    api.importContacts.mockResolvedValue({
      created: 1, merged: 0, skipped: 0, failed: 0, totalErrors: 0, errors: [],
    })
    renderTransfer(book)

    const input = choose(new File(['x'], 'contacts.csv', { type: 'text/csv' }))

    await waitFor(() => expect(input.value).toBe(''))
  })

  // Server prose never reaches the caller; the local fallback does — see apiErrorMessage.
  it('reports the local fallback for a refused import carrying no code', async () => {
    api.importContacts.mockRejectedValue(new Error('No recognised column in this file.'))
    const onError = renderTransfer(book)

    choose(new File(['x'], 'contacts.csv', { type: 'text/csv' }))

    await waitFor(() => expect(onError).toHaveBeenCalledWith('Could not import the file'))
    expect(screen.queryByText(/added/i)).not.toBeInTheDocument()
  })

  // csv_no_recognised_column is a named stable code: the refusal must stay specific, translated
  // rather than shown as the generic fallback above.
  it('reports the translated csv_no_recognised_column message', async () => {
    api.importContacts.mockRejectedValue(
      Object.assign(new Error('csv_no_recognised_column'), { code: 'csv_no_recognised_column' }))
    const onError = renderTransfer(book)

    choose(new File(['x'], 'contacts.csv', { type: 'text/csv' }))

    await waitFor(() => expect(onError).toHaveBeenCalledWith(
      'No recognised column in this file. It needs a header row naming a name or an e-mail column.'))
    expect(screen.queryByText(/added/i)).not.toBeInTheDocument()
  })

  // The framework answers a 413 with no envelope at all, so its message is a bare status text.
  it('names the size limit when the file is refused as too large', async () => {
    api.importContacts.mockRejectedValue(
      Object.assign(new ApiError('Payload Too Large'), { status: 413 }))
    const onError = renderTransfer(book)

    choose(new File(['x'], 'contacts.csv', { type: 'text/csv' }))

    await waitFor(() =>
      expect(onError).toHaveBeenCalledWith('That file is too large — the limit is 20 MB.'))
  })

  it('downloads the export under the served name', async () => {
    const blob = new Blob(['x'])
    api.exportContacts.mockResolvedValue({ blob, fileName: 'contacts-2026-07-27.csv' })
    renderTransfer(book)

    await openMenu()
    await user.click(screen.getByRole('menuitem', { name: 'Export' }))

    await waitFor(() => expect(downloadBlob).toHaveBeenCalledWith(blob, 'contacts-2026-07-27.csv'))
  })

  // A file with no rows in it reads as a failure, so the door is shut rather than opened onto one.
  // The tooltip that carried the reason went with the two buttons; the row's title carries it now.
  it('disables the export on an empty book and names why', async () => {
    renderTransfer([])

    await openMenu()

    const item = screen.getByRole('menuitem', { name: 'Export' })
    expect(item).toBeDisabled()
    expect(item).toHaveAttribute('title', 'Nothing to export')
  })

  it('disables the export while the book is still loading', async () => {
    renderTransfer(undefined)

    await openMenu()

    expect(screen.getByRole('menuitem', { name: 'Export' })).toBeDisabled()
  })

  // Server prose never reaches the caller; the local fallback does — see apiErrorMessage.
  it('reports a refused export to its caller', async () => {
    api.exportContacts.mockRejectedValue(new Error('Server error'))
    const onError = renderTransfer(book)

    await openMenu()
    await user.click(screen.getByRole('menuitem', { name: 'Export' }))

    await waitFor(() => expect(onError).toHaveBeenCalledWith('Could not export the contacts'))
  })

  it('opens the file dialog from the menu', async () => {
    renderTransfer(book)
    const input = screen.getByTestId('contacts-import-input')
    const click = vi.spyOn(input, 'click')

    await openMenu()
    await user.click(screen.getByRole('menuitem', { name: 'Import…' }))

    expect(click).toHaveBeenCalledTimes(1)
  })
})
