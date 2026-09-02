import { describe, it, expect, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import RecipientsField, { isValidAddress } from './RecipientsField'
import type { GroupOption } from '../../contacts/contactSearch'
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

  // A nameless card gets the address line and nothing else — printed as its own name too, the row
  // said the same string twice and read as a person called by their address.
  it('drops the name line for a contact carrying no name', async () => {
    const shadow = contact({ id: 's', nickname: 'ghost@x.be', addresses: ['ghost@x.be'] })
    render(<RecipientsField id="to" label="To" tokens={[]} onChange={vi.fn()} contacts={[shadow]} />)

    await userEvent.type(screen.getByLabelText('To'), 'ghost')

    const row = screen.getByRole('option')
    expect(row.querySelector('.suggestion-names')).toBeNull()
    expect(row).toHaveTextContent('ghost@x.be')
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

  // A refetch on focus, or a group renamed elsewhere, can shrink `suggestions` with no keystroke —
  // the draft, and therefore `active`, never resets. The stale index used to reach past the end of
  // the new array and hand `commitSuggestion` `undefined`.
  it('re-bounds the active index when suggestions shrink without a keystroke', async () => {
    const onChange = vi.fn()
    const many = Array.from({ length: 4 }, (_, i) =>
      contact({ id: `c${i}`, firstName: `C${i}`, addresses: [`c${i}@example.com`] }))
    const { rerender } = render(
      <RecipientsField id="to" label="To" tokens={[]} onChange={onChange} contacts={many} />)
    const input = screen.getByLabelText('To')
    await userEvent.type(input, 'example')
    expect(screen.getAllByRole('option')).toHaveLength(4)

    await userEvent.keyboard('{ArrowDown}{ArrowDown}{ArrowDown}{ArrowDown}')
    expect(screen.getAllByRole('option')[3]).toHaveClass('is-active')

    rerender(
      <RecipientsField id="to" label="To" tokens={[]} onChange={onChange} contacts={many.slice(0, 3)} />)
    const rows = screen.getAllByRole('option')
    expect(rows).toHaveLength(3)
    expect(rows[2]).toHaveClass('is-active')
    expect(input).toHaveAttribute('aria-activedescendant', rows[2].id)

    fireEvent.keyDown(input, { key: 'Enter' })

    expect(onChange).toHaveBeenCalledWith(['c2@example.com'])
  })
})

describe('RecipientsField — a token wears its contact name', () => {
  const bruno = contact({
    id: 'b', firstName: 'Bruno', lastName: 'Mertens', addresses: ['bruno@x.be', 'b@wk.be'],
  })

  function show(tokens: string[], contacts: Contact[]) {
    render(<RecipientsField id="to" label="To" tokens={tokens} onChange={vi.fn()} contacts={contacts} />)
  }

  it('shows the name and keeps the address one hover away', () => {
    show(['bruno@x.be'], [bruno])

    expect(screen.getByText('Bruno Mertens')).toHaveAttribute('title', 'bruno@x.be')
    expect(screen.queryByText('bruno@x.be')).not.toBeInTheDocument()
  })

  // The bubble must carry the address the message will go to, never the contact's primary one.
  it('carries the matched address, not the primary one', () => {
    show(['b@wk.be'], [bruno])

    expect(screen.getByText('Bruno Mertens')).toHaveAttribute('title', 'b@wk.be')
  })

  it('resolves the address whatever its spelling', () => {
    show(['  Bruno@X.BE '], [bruno])

    expect(screen.getByText('Bruno Mertens')).toBeInTheDocument()
  })

  it('shows the address bare, with no bubble, for a contact carrying no name', () => {
    show(['ghost@x.be'], [contact({ id: 'g', addresses: ['ghost@x.be'] })])

    expect(screen.getByText('ghost@x.be')).not.toHaveAttribute('title')
  })

  it('shows the address bare, with no bubble, when the book does not hold it', () => {
    show(['stranger@x.be'], [bruno])

    expect(screen.getByText('stranger@x.be')).not.toHaveAttribute('title')
  })

  it('names the remove button after what the chip shows', () => {
    show(['bruno@x.be'], [bruno])

    expect(screen.getByRole('button', { name: 'Remove Bruno Mertens' })).toBeInTheDocument()
  })

  // The bug this exists for: an imported card carrying its own address in a name column sorts
  // ahead of the real contact — 'd' before 'M' — and first-wins handed the chip an address as a
  // name. A contact that names nobody must never out-name one that does.
  it('ignores a contact whose only name is its own address', () => {
    show(['b@wk.be'], [
      contact({ id: 'shadow', nickname: 'b@wk.be', addresses: ['b@wk.be'] }),
      bruno,
    ])

    expect(screen.getByText('Bruno Mertens')).toHaveAttribute('title', 'b@wk.be')
  })

  // One address, two contacts: the chip keeps the name the dropdown offered it under — favourites
  // first, then alphabetical — so the two surfaces cannot name the same recipient differently.
  it('prefers the favourite when an address is shared', () => {
    show(['info@x.be'], [
      contact({ id: 'o', firstName: 'Adam', addresses: ['info@x.be'] }),
      contact({ id: 's', firstName: 'Zoe', isFavorite: true, addresses: ['info@x.be'] }),
    ])

    expect(screen.getByText('Zoe')).toBeInTheDocument()
  })
})

describe('RecipientsField — group rows', () => {
  const bruno = contact({
    id: 'b', firstName: 'Bruno', lastName: 'Mertens', addresses: ['bruno@x.be', 'b@wk.be'],
  })
  const team: GroupOption = {
    id: 'g1', name: 'Team', memberCount: 2, addresses: ['alice@x.be', 'bruno@x.be'],
  }

  function show(
    tokens: string[], groups: GroupOption[], extra: Partial<{
      onChange: (tokens: string[]) => void; onEmptyGroup: (name: string) => void
    }> = {},
  ) {
    const onChange = extra.onChange ?? vi.fn()
    render(<RecipientsField id="to" label="To" tokens={tokens} onChange={onChange}
      contacts={[bruno]} groups={groups} onEmptyGroup={extra.onEmptyGroup} />)
    return { onChange, input: screen.getByLabelText('To') }
  }

  // 'te' matches Mertens too, so the group is ranged against real address rows rather than being
  // the only thing in the list.
  it('lists the group ahead of the addresses, saying how many members it holds', async () => {
    const { input } = show([], [team])

    await userEvent.type(input, 'te')

    const rows = screen.getAllByRole('option')
    expect(rows).toHaveLength(3)
    expect(rows[0]).toHaveTextContent('Team')
    expect(rows[0]).toHaveTextContent('2 members')
    expect(rows[1]).toHaveTextContent('bruno@x.be')
  })

  it('says « member » in the singular', async () => {
    const { input } = show([], [{ ...team, memberCount: 1, addresses: ['alice@x.be'] }])

    await userEvent.type(input, 'te')

    expect(screen.getAllByRole('option')[0]).toHaveTextContent('1 member')
  })

  // The arrows walk one list, whatever each row holds: the group is the first thing a ArrowDown
  // reaches, and the highlight has to be announced there like anywhere else.
  it('reaches the group row with an arrow key and expands it on Enter', async () => {
    const onChange = vi.fn()
    const { input } = show([], [team], { onChange })
    await userEvent.type(input, 'te')

    await userEvent.keyboard('{ArrowDown}')

    const row = screen.getAllByRole('option')[0]
    expect(row).toHaveClass('is-active')
    expect(input).toHaveAttribute('aria-activedescendant', row.id)

    await userEvent.keyboard('{Enter}')

    expect(onChange).toHaveBeenCalledWith(['alice@x.be', 'bruno@x.be'])
    expect(input).toHaveValue('')
  })

  // Case is free in the field, so a member already standing there in another spelling must not
  // come back as a second chip producing the identical recipient.
  it('adds only the members not already tokenised, whatever their spelling', async () => {
    const onChange = vi.fn()
    const { input } = show([' ALICE@X.BE '], [team], { onChange })
    await userEvent.type(input, 'te')

    await userEvent.click(screen.getAllByRole('option')[0])

    expect(onChange).toHaveBeenCalledWith([' ALICE@X.BE ', 'bruno@x.be'])
  })

  // 'josé@x.com' and 'jose@x.com' are two distinct SMTPUTF8 mailboxes; fold() strips the diacritic
  // and would wrongly treat the accented address as already present. A second, unrelated address
  // keeps the group itself offered (suggestionsFor's own dropdown filter, untouched by this fix,
  // still folds — a group whose every address folds to an existing token stays hidden by design).
  it('inserts an accented address a plain-ASCII token only folds to match', async () => {
    const onChange = vi.fn()
    const accented: GroupOption = {
      id: 'g3', name: 'Jose Accents', memberCount: 2, addresses: ['josé@x.com', 'other@x.com'],
    }
    const { input } = show(['jose@x.com'], [accented], { onChange })
    await userEvent.type(input, 'jose')

    await userEvent.click(screen.getByRole('option'))

    expect(onChange).toHaveBeenCalledWith(['jose@x.com', 'josé@x.com', 'other@x.com'])
  })

  // Nothing is ever inserted in silence (decision 15): a group nobody in the book resolves is a
  // state the user has to be told about, and this is the only road that announcement takes.
  it('raises the empty-group notice instead of adding nothing', async () => {
    const onChange = vi.fn()
    const onEmptyGroup = vi.fn()
    const empty: GroupOption = { id: 'g2', name: 'Nobody', memberCount: 0, addresses: [] }
    const { input } = show([], [empty], { onChange, onEmptyGroup })
    await userEvent.type(input, 'nob')

    await userEvent.click(screen.getByRole('option'))

    expect(onEmptyGroup).toHaveBeenCalledWith('Nobody')
    expect(onChange).not.toHaveBeenCalled()
    // Reset in every case: the query that found the group has been answered either way.
    expect(input).toHaveValue('')
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  })
})
