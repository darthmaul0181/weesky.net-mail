import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ContactsTransfer from './ContactsTransfer'
import type { Contact } from './contactTypes'

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
  { id: '1', firstName: 'Bruno', lastName: null, nickname: null, isFavorite: false, addresses: [] },
]

function renderTransfer(contacts: Contact[] | undefined, onError = vi.fn()) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <ContactsTransfer contacts={contacts} onError={onError} />
    </QueryClientProvider>,
  )
  return onError
}

// The input is hidden, so userEvent.upload cannot reach it; the change event is what the component
// actually listens to.
function choose(file: File) {
  const input = screen.getByTestId('contacts-import-input') as HTMLInputElement
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

  it('reports a refused import to its caller', async () => {
    api.importContacts.mockRejectedValue(new Error('No recognised column in this file.'))
    const onError = renderTransfer(book)

    choose(new File(['x'], 'contacts.csv', { type: 'text/csv' }))

    await waitFor(() => expect(onError).toHaveBeenCalledWith('No recognised column in this file.'))
    expect(screen.queryByText(/added/i)).not.toBeInTheDocument()
  })

  // The framework answers a 413 with no envelope at all, so its message is a bare status text.
  it('names the size limit when the file is refused as too large', async () => {
    api.importContacts.mockRejectedValue(
      Object.assign(new ApiError('Payload Too Large'), { status: 413 }))
    const onError = renderTransfer(book)

    choose(new File(['x'], 'contacts.csv', { type: 'text/csv' }))

    await waitFor(() =>
      expect(onError).toHaveBeenCalledWith('That file is too large — the limit is 5 MB.'))
  })

  it('downloads the export under the served name', async () => {
    const blob = new Blob(['x'])
    api.exportContacts.mockResolvedValue({ blob, fileName: 'contacts-2026-07-27.csv' })
    renderTransfer(book)

    await userEvent.click(screen.getByRole('button', { name: 'Export' }))

    await waitFor(() => expect(downloadBlob).toHaveBeenCalledWith(blob, 'contacts-2026-07-27.csv'))
  })

  // A file with no rows in it reads as a failure, so the door is shut rather than opened onto one.
  it('disables the export on an empty book', () => {
    renderTransfer([])

    expect(screen.getByRole('button', { name: 'Export' })).toBeDisabled()
  })

  it('disables the export while the book is still loading', () => {
    renderTransfer(undefined)

    expect(screen.getByRole('button', { name: 'Export' })).toBeDisabled()
  })

  it('reports a refused export to its caller', async () => {
    api.exportContacts.mockRejectedValue(new Error('Server error'))
    const onError = renderTransfer(book)

    await userEvent.click(screen.getByRole('button', { name: 'Export' }))

    await waitFor(() => expect(onError).toHaveBeenCalledWith('Server error'))
  })
})
