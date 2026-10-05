import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { Contact } from '../contacts/contactTypes'
import AttendeesField from './AttendeesField'
import { setupUser } from '../../test-utils'

let user: ReturnType<typeof setupUser>
beforeEach(() => { user = setupUser() })

const contacts: Contact[] = [
  { id: 'c1', firstName: 'Marc', isFavorite: false, addresses: ['marc@example.org'] },
  { id: 'c2', firstName: 'Julie', lastName: 'Martin', isFavorite: false, addresses: ['julie@example.net'] },
]
vi.mock('../contacts/queries', () => ({ useContacts: () => ({ data: contacts }) }))

describe('AttendeesField', () => {
  it('offers a contact, keeps its name on the guest, and removes a chip', async () => {
    const onChange = vi.fn()
    render(<AttendeesField value={[{ email: 'marc@example.org', name: 'Marc' }]} onChange={onChange} />)
    // The chip shows the contact's name when the book has one.
    expect(screen.getByText('Marc')).toBeInTheDocument()
    await user.type(screen.getByLabelText('Attendees'), 'jul')
    await user.click(await screen.findByText(/Julie/))
    expect(onChange).toHaveBeenLastCalledWith([
      { email: 'marc@example.org', name: 'Marc' }, { email: 'julie@example.net', name: 'Julie Martin' }])
    await user.click(screen.getByRole('button', { name: 'Remove Marc' }))
    expect(onChange).toHaveBeenLastCalledWith([])
  })

  // The name the guest came with stays theirs; the book only names someone the list has not.
  it('keeps the name a guest came with, and names a typed address from the book', async () => {
    const onChange = vi.fn()
    render(<AttendeesField value={[{ email: 'lea@example.net', name: 'Léa' }]} onChange={onChange} />)
    await user.type(screen.getByLabelText('Attendees'), 'Julie@Example.net{Enter}')
    expect(onChange).toHaveBeenLastCalledWith([
      { email: 'lea@example.net', name: 'Léa' }, { email: 'Julie@Example.net', name: 'Julie Martin' }])
  })

  it('draws every chip of a guest listed twice', () => {
    const twice = { email: 'paul@example.org' }
    render(<AttendeesField value={[twice, twice]} onChange={vi.fn()} />)
    expect(screen.getAllByText('paul@example.org')).toHaveLength(2)
  })
})
