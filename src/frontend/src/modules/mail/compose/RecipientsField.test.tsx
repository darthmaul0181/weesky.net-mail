import { describe, it, expect, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import RecipientsField, { isValidAddress } from './RecipientsField'
import type { Contact } from '../../contacts/contactTypes'

function contact(fields: Partial<Contact> & { id: string }): Contact {
  return {
    firstName: null, lastName: null, nickname: null, isFavorite: false, addresses: [], ...fields,
  }
}

function setup(tokens: string[] = []) {
  const onChange = vi.fn()
  render(<RecipientsField id="to" label="To" tokens={tokens} onChange={onChange} />)
  return { onChange }
}

describe('isValidAddress', () => {
  it.each(['a@b.co', 'first.last@sub.domain.org'])('accepts %s', v => expect(isValidAddress(v)).toBe(true))
  it.each(['nope', 'a@b', 'a b@c.d', '@x.y'])('refuses %s', v => expect(isValidAddress(v)).toBe(false))
})

describe('RecipientsField', () => {
  it('commits a token on Enter', () => {
    const { onChange } = setup()
    fireEvent.change(screen.getByLabelText('To'), { target: { value: 'a@b.co' } })
    fireEvent.keyDown(screen.getByLabelText('To'), { key: 'Enter' })
    expect(onChange).toHaveBeenCalledWith(['a@b.co'])
  })

  it('commits on comma, semicolon and blur', () => {
    const { onChange } = setup()
    const input = screen.getByLabelText('To')
    fireEvent.change(input, { target: { value: 'a@b.co' } })
    fireEvent.keyDown(input, { key: ',' })
    expect(onChange).toHaveBeenCalledWith(['a@b.co'])
    fireEvent.change(input, { target: { value: 'c@d.co' } })
    fireEvent.blur(input)
    expect(onChange).toHaveBeenLastCalledWith(['c@d.co'])
  })

  it('splits a paste on separators', () => {
    const { onChange } = setup()
    const input = screen.getByLabelText('To')
    fireEvent.paste(input, { clipboardData: { getData: () => 'a@b.co, c@d.co; e@f.co' } })
    expect(onChange).toHaveBeenCalledWith(['a@b.co', 'c@d.co', 'e@f.co'])
  })

  it('marks an invalid token and removes on its ✕', () => {
    const { onChange } = setup(['bad-token', 'ok@x.co'])
    expect(screen.getByText('bad-token').closest('.recipient-token')).toHaveClass('is-invalid')
    fireEvent.click(screen.getAllByRole('button', { name: /^Remove / })[0])
    expect(onChange).toHaveBeenCalledWith(['ok@x.co'])
  })

  it('Backspace on an empty input removes the last token', () => {
    const { onChange } = setup(['a@b.co'])
    fireEvent.keyDown(screen.getByLabelText('To'), { key: 'Backspace' })
    expect(onChange).toHaveBeenCalledWith([])
  })
})

describe('RecipientsField — contact suggestions', () => {
  const bruno = contact({
    id: 'b', firstName: 'Bruno', lastName: 'Mertens', addresses: ['bruno@x.be', 'b@wk.be'],
  })

  it('offers no dropdown before anything is typed', () => {
    render(<RecipientsField id="to" label="To" tokens={[]} onChange={vi.fn()} contacts={[bruno]} />)

    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  })

  it('opens the dropdown as the user types and lists one row per address', async () => {
    render(<RecipientsField id="to" label="To" tokens={[]} onChange={vi.fn()} contacts={[bruno]} />)

    await userEvent.type(screen.getByLabelText('To'), 'bru')

    expect(screen.getAllByRole('option')).toHaveLength(2)
    expect(screen.getByRole('option', { name: /bruno@x\.be/ })).toHaveTextContent('Bruno Mertens')
  })

  // One row, every owner named: the decision to allow a shared address lands here. Two rows would
  // produce the identical recipient and one name would be an arbitrary pick.
  it('shows a shared address once, naming both contacts', async () => {
    const shared = 'info@x.be'
    const contacts = [
      contact({ id: '1', firstName: 'Alice', lastName: 'Dupont', addresses: [shared] }),
      contact({ id: '2', firstName: 'Compta', addresses: [shared] }),
    ]
    render(<RecipientsField id="to" label="To" tokens={[]} onChange={vi.fn()} contacts={contacts} />)

    await userEvent.type(screen.getByLabelText('To'), 'info')

    const rows = screen.getAllByRole('option')
    expect(rows).toHaveLength(1)
    expect(rows[0]).toHaveTextContent('Alice Dupont')
    expect(rows[0]).toHaveTextContent('Compta')
  })

  it('commits the picked address as a token', async () => {
    const onChange = vi.fn()
    render(<RecipientsField id="to" label="To" tokens={[]} onChange={onChange} contacts={[bruno]} />)
    await userEvent.type(screen.getByLabelText('To'), 'bru')

    await userEvent.click(screen.getByRole('option', { name: /bruno@x\.be/ }))

    expect(onChange).toHaveBeenCalledWith(['bruno@x.be'])
  })

  it('drops an address already tokenised from the options', async () => {
    render(<RecipientsField id="to" label="To" tokens={['bruno@x.be']} onChange={vi.fn()}
      contacts={[bruno]} />)

    await userEvent.type(screen.getByLabelText('To'), 'b')

    expect(screen.queryByRole('option', { name: /bruno@x\.be/ })).not.toBeInTheDocument()
    expect(screen.getByRole('option', { name: /b@wk\.be/ })).toBeInTheDocument()
  })

  it('walks the list with the arrow keys and commits with Enter', async () => {
    const onChange = vi.fn()
    render(<RecipientsField id="to" label="To" tokens={[]} onChange={onChange} contacts={[bruno]} />)
    const input = screen.getByLabelText('To')
    await userEvent.type(input, 'bru')

    await userEvent.keyboard('{ArrowDown}')

    // Focus never leaves the input, so the highlight exists only as these three: the class paints
    // it, aria-selected states it, aria-activedescendant is what a screen reader announces.
    const first = screen.getByRole('option', { name: /bruno@x\.be/ })
    expect(first).toHaveClass('is-active')
    expect(first).toHaveAttribute('aria-selected', 'true')
    expect(input).toHaveAttribute('aria-controls', screen.getByRole('listbox').id)
    expect(input).toHaveAttribute('aria-activedescendant', first.id)

    await userEvent.keyboard('{ArrowDown}{Enter}')

    expect(onChange).toHaveBeenCalledWith(['b@wk.be'])
  })

  // The scrollbar and the padding strip belong to the list too, and the list gets one as soon as
  // it fills up. jsdom moves no focus on mousedown, so cancelling the event is the measurable
  // part; in a browser that cancellation is exactly what stops the blur.
  it('cancels a mousedown on the list itself, so the draft survives', async () => {
    const onChange = vi.fn()
    render(<RecipientsField id="to" label="To" tokens={[]} onChange={onChange} contacts={[bruno]} />)
    await userEvent.type(screen.getByLabelText('To'), 'bru')

    const dispatched = fireEvent.mouseDown(screen.getByRole('listbox'))

    expect(dispatched).toBe(false)
    expect(onChange).not.toHaveBeenCalled()
    expect(screen.getByLabelText('To')).toHaveValue('bru')
  })

  it('reopens the list on an arrow key after Escape', async () => {
    render(<RecipientsField id="to" label="To" tokens={[]} onChange={vi.fn()} contacts={[bruno]} />)
    await userEvent.type(screen.getByLabelText('To'), 'bru')
    await userEvent.keyboard('{Escape}')
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()

    await userEvent.keyboard('{ArrowDown}')

    expect(screen.getByRole('listbox')).toBeInTheDocument()
    expect(screen.getByRole('option', { name: /bruno@x\.be/ })).toHaveClass('is-active')
  })

  // Free typing is what keeps the field usable with zero contacts: the list accelerates, it never
  // gates. Nothing is highlighted until an arrow key says so, so Enter commits what was typed.
  // The query has to match a contact, or the dropdown is shut and Enter has nothing it could have
  // substituted — a field highlighting its first row by default would pass on a query matching
  // nobody.
  it('commits the typed text on Enter when no row is highlighted', async () => {
    const onChange = vi.fn()
    render(<RecipientsField id="to" label="To" tokens={[]} onChange={onChange} contacts={[bruno]} />)
    await userEvent.type(screen.getByLabelText('To'), 'bru')
    expect(screen.getByRole('listbox')).toBeInTheDocument()

    await userEvent.keyboard('{Enter}')

    expect(onChange).toHaveBeenCalledWith(['bru'])
  })

  it('closes on Escape without clearing what was typed', async () => {
    render(<RecipientsField id="to" label="To" tokens={[]} onChange={vi.fn()} contacts={[bruno]} />)
    await userEvent.type(screen.getByLabelText('To'), 'bru')

    await userEvent.keyboard('{Escape}')

    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
    expect(screen.getByLabelText('To')).toHaveValue('bru')
  })

  it('works exactly as before when no contacts are supplied', async () => {
    const onChange = vi.fn()
    render(<RecipientsField id="to" label="To" tokens={[]} onChange={onChange} />)

    await userEvent.type(screen.getByLabelText('To'), 'a@x.be{Enter}')

    expect(onChange).toHaveBeenCalledWith(['a@x.be'])
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  })
})
