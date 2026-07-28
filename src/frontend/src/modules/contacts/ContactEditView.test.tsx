import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import ContactEditView from './ContactEditView'
import type { Contact } from './contactTypes'

const bruno: Contact = {
  id: 'b', firstName: 'Bruno', lastName: 'Mertens', nickname: 'bru',
  isFavorite: false, addresses: ['bruno@x.be', 'b.mertens@wk.be'],
}

const solo: Contact = {
  id: 's', firstName: null, lastName: null, nickname: null,
  isFavorite: false, addresses: ['solo@x.be'],
}

// Three rows are what tells "swap with the previous row" apart from "promote to the top" or
// "reverse the list" — with only two rows (bruno, above) all three read the same.
const trio: Contact = {
  id: 't', firstName: null, lastName: null, nickname: null,
  isFavorite: false, addresses: ['a@x.be', 'b@x.be', 'c@x.be'],
}

const addressless: Contact = {
  id: 'z', firstName: 'Zoe', lastName: null, nickname: null,
  isFavorite: false, addresses: [],
}

function setup(overrides: Partial<Parameters<typeof ContactEditView>[0]> = {}) {
  const props = {
    contact: null as Contact | null, saving: false, error: null as string | null,
    onSave: vi.fn(), onCancel: vi.fn(), ...overrides,
  }
  render(<ContactEditView {...props} />)
  return props
}

describe('ContactEditView', () => {
  // Both halves, side by side in the document: one component serves the two modes, so the heading
  // is the only thing telling the user which one they are in.
  it('heads a create as New contact and an edit as Edit contact', () => {
    setup()
    setup({ contact: bruno })

    expect(screen.getByRole('heading', { name: /new contact/i })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /edit contact/i })).toBeInTheDocument()
  })

  it('seeds every field from the contact being edited', () => {
    setup({ contact: bruno })

    expect(screen.getByLabelText(/first name/i)).toHaveValue('Bruno')
    expect(screen.getByLabelText(/last name/i)).toHaveValue('Mertens')
    expect(screen.getByLabelText(/nickname/i)).toHaveValue('bru')
    expect(screen.getByLabelText(/address 1/i)).toHaveValue('bruno@x.be')
    expect(screen.getByLabelText(/address 2/i)).toHaveValue('b.mertens@wk.be')
  })

  it('starts a create with one empty address row', () => {
    setup()

    expect(screen.getByLabelText(/address 1/i)).toHaveValue('')
    expect(screen.queryByLabelText(/address 2/i)).not.toBeInTheDocument()
  })

  // The server allows a contact with only a name, so an edited contact's `addresses` can arrive
  // empty too, not just a brand-new create — the same empty-row seed has to cover both.
  it('seeds one empty address row when the contact being edited has none at all', () => {
    setup({ contact: addressless })

    expect(screen.getByLabelText(/address 1/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/address 1/i)).toHaveValue('')
  })

  // Position 0 is the primary by definition: the badge is on the first row, and it moves when
  // the rows are reordered rather than being a flag of its own.
  it('badges the first address row as the primary', () => {
    setup({ contact: bruno })

    expect(screen.getByTestId('address-row-0')).toHaveTextContent(/primary/i)
    expect(screen.getByTestId('address-row-1')).not.toHaveTextContent(/primary/i)
  })

  it('adds an address row on demand', async () => {
    setup()

    await userEvent.click(screen.getByRole('button', { name: /add an address/i }))

    expect(screen.getByLabelText(/address 2/i)).toBeInTheDocument()
  })

  it('removes an address row', async () => {
    setup({ contact: bruno })

    await userEvent.click(screen.getByRole('button', { name: /remove address 2/i }))

    expect(screen.queryByLabelText(/address 2/i)).not.toBeInTheDocument()
    expect(screen.getByLabelText(/address 1/i)).toHaveValue('bruno@x.be')
  })

  // The floor the two-address removal test above cannot reach: with only one row left, removing
  // it must not empty the list, or a create-mode user is left with no box to type into at all.
  it('never drops to zero address rows: removing the last one leaves an empty row', async () => {
    setup({ contact: solo })

    await userEvent.click(screen.getByRole('button', { name: /remove address 1/i }))

    expect(screen.getByLabelText(/address 1/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/address 1/i)).toHaveValue('')
  })

  it('moves an address up, which makes it the primary', async () => {
    setup({ contact: bruno })

    await userEvent.click(screen.getByRole('button', { name: /move address 2 up/i }))

    expect(screen.getByLabelText(/address 1/i)).toHaveValue('b.mertens@wk.be')
    expect(screen.getByTestId('address-row-0')).toHaveTextContent(/primary/i)
  })

  // With three rows, "move up" has to mean swap-with-previous specifically: promoting the row to
  // the top, or reversing the whole list, would both pass the two-row test above unnoticed.
  it('moves the third address up one slot, swapping it with the second', async () => {
    setup({ contact: trio })

    await userEvent.click(screen.getByRole('button', { name: /move address 3 up/i }))

    expect(screen.getByLabelText(/address 1/i)).toHaveValue('a@x.be')
    expect(screen.getByLabelText(/address 2/i)).toHaveValue('c@x.be')
    expect(screen.getByLabelText(/address 3/i)).toHaveValue('b@x.be')
  })

  it('offers no move up on the first row', () => {
    setup({ contact: bruno })

    expect(screen.queryByRole('button', { name: /move address 1 up/i })).not.toBeInTheDocument()
  })

  // The gate the backend also enforces. Refusing here is what keeps the user from a round trip
  // whose only outcome is an error banner.
  it('keeps save disabled while neither a name nor an address is filled', () => {
    setup()

    expect(screen.getByRole('button', { name: /save contact/i })).toBeDisabled()
  })

  it('enables save on a name alone', async () => {
    setup()

    await userEvent.type(screen.getByLabelText(/first name/i), 'Bruno')

    expect(screen.getByRole('button', { name: /save contact/i })).toBeEnabled()
  })

  it('enables save on an address alone', async () => {
    setup()

    await userEvent.type(screen.getByLabelText(/address 1/i), 'bruno@x.be')

    expect(screen.getByRole('button', { name: /save contact/i })).toBeEnabled()
  })

  it('submits the draft, blank address rows dropped', async () => {
    const props = setup()
    await userEvent.type(screen.getByLabelText(/first name/i), 'Bruno')
    await userEvent.click(screen.getByRole('button', { name: /add an address/i }))
    await userEvent.type(screen.getByLabelText(/address 1/i), 'bruno@x.be')

    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(props.onSave).toHaveBeenCalledWith({
      firstName: 'Bruno', lastName: null, nickname: null, isFavorite: false,
      addresses: ['bruno@x.be'],
    })
  })

  it('sends null rather than an empty string for a blank name', async () => {
    const props = setup()
    await userEvent.type(screen.getByLabelText(/address 1/i), 'a@x.be')

    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(props.onSave).toHaveBeenCalledWith(expect.objectContaining({ firstName: null, nickname: null }))
  })

  it('carries the favourite flag through', async () => {
    const props = setup({ contact: { ...bruno, isFavorite: true } })

    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(props.onSave).toHaveBeenCalledWith(expect.objectContaining({ isFavorite: true }))
  })

  // The column widths, spelled out rather than read from the component: a bound that drifts from
  // VARCHAR(100)/VARCHAR(320) sends the write into a strict-mode MariaDB error, i.e. a 500.
  it('bounds every field to its column width', () => {
    setup({ contact: bruno })

    expect(screen.getByLabelText(/first name/i)).toHaveAttribute('maxlength', '100')
    expect(screen.getByLabelText(/last name/i)).toHaveAttribute('maxlength', '100')
    expect(screen.getByLabelText(/nickname/i)).toHaveAttribute('maxlength', '100')
    expect(screen.getByLabelText(/address 1/i)).toHaveAttribute('maxlength', '320')
    expect(screen.getByLabelText(/address 2/i)).toHaveAttribute('maxlength', '320')
  })

  it('surfaces a server error at the top of the form', () => {
    setup({ error: "'nope' is not a valid email address" })

    expect(screen.getByRole('alert')).toHaveTextContent('not a valid email address')
  })

  it('disables save and shows a spinner while saving', () => {
    setup({ contact: bruno, saving: true })

    expect(screen.getByRole('button', { name: /save contact/i })).toBeDisabled()
    expect(screen.getByTestId('editor-spinner')).toBeInTheDocument()
  })

  it('cancels through the ✕', async () => {
    const props = setup()

    await userEvent.click(screen.getByRole('button', { name: /close the editor/i }))

    expect(props.onCancel).toHaveBeenCalled()
  })
})
