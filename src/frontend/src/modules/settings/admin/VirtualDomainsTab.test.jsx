import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import VirtualDomainsTab from './VirtualDomainsTab'

const mocks = vi.hoisted(() => ({
  getVirtualDomains: vi.fn(),
  getUsers: vi.fn(),
  addOwner: vi.fn(),
  removeOwner: vi.fn(),
}))

vi.mock('../../../api.js', () => ({
  api: {
    adminGetVirtualDomains: mocks.getVirtualDomains,
    adminGetUsers: mocks.getUsers,
    adminAddVirtualDomainOwner: mocks.addOwner,
    adminRemoveVirtualDomainOwner: mocks.removeOwner,
  },
}))

const users = [
  { id: 'u1', userName: 'ada', domainName: 'weesky.be', fullName: 'Ada Lovelace' },
  { id: 'u2', userName: 'grace', domainName: 'weesky.be', fullName: 'Grace Hopper' },
  { id: 'u3', userName: 'alan', domainName: 'weesky.be', fullName: 'Alan Turing' },
]

const field = () => screen.getByPlaceholderText(/Search user/)
const option = name => screen.getByRole('button', { name: new RegExp(name) })
const pencils = () => screen.getAllByTitle('Edit owner')

async function renderTab() {
  render(<VirtualDomainsTab addToast={vi.fn()} />)
  return waitFor(() => expect(pencils()).toHaveLength(2))
}

/** Opens the given row's owner editor and filters the user list down to three matches. */
async function search(row, term) {
  fireEvent.click(pencils()[row])
  fireEvent.change(field(), { target: { value: term } })
  await waitFor(() => expect(option('ada@weesky.be')).toBeInTheDocument())
}

describe('VirtualDomainsTab', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getUsers.mockResolvedValue(users)
    mocks.getVirtualDomains.mockResolvedValue([
      { domainId: 'd1', domainName: 'alpha.com', owners: [] },
      { domainId: 'd2', domainName: 'beta.com', owners: [] },
    ])
  })

  it('opens the owner editor with the focus in its search box', async () => {
    await renderTab()

    fireEvent.click(pencils()[0])

    expect(field()).toHaveFocus()
  })

  /* The field and its list are one surface: ↓ walks out of the box into the matches, and Home and
     End stay the caret's while it holds the focus. */
  it('walks the matches on the vertical arrows, wrapping at both ends', async () => {
    await renderTab()
    await search(0, 'weesky')

    fireEvent.keyDown(field(), { key: 'ArrowDown' })
    expect(option('ada@weesky.be')).toHaveFocus()

    fireEvent.keyDown(option('ada@weesky.be'), { key: 'ArrowDown' })
    expect(option('grace@weesky.be')).toHaveFocus()

    fireEvent.keyDown(option('grace@weesky.be'), { key: 'End' })
    expect(option('alan@weesky.be')).toHaveFocus()

    fireEvent.keyDown(option('alan@weesky.be'), { key: 'ArrowDown' })
    expect(field()).toHaveFocus()

    fireEvent.keyDown(field(), { key: 'ArrowUp' })
    expect(option('alan@weesky.be')).toHaveFocus()
  })

  it('leaves Home and End to the caret while the search box holds the focus', async () => {
    await renderTab()
    await search(0, 'weesky')

    expect(fireEvent.keyDown(field(), { key: 'Home' })).toBe(true)
    expect(fireEvent.keyDown(field(), { key: 'End' })).toBe(true)
    expect(field()).toHaveFocus()
  })

  // Reached by the arrows, a match has to answer the key that means "this one".
  it('adds the owner the keyboard activated', async () => {
    mocks.addOwner.mockResolvedValue({
      domainId: 'd1', domainName: 'alpha.com',
      owners: [{ ownerId: 'u2', ownerEmail: 'grace@weesky.be' }],
    })
    await renderTab()
    await search(0, 'weesky')

    fireEvent.keyDown(field(), { key: 'ArrowDown' })
    fireEvent.keyDown(option('ada@weesky.be'), { key: 'ArrowDown' })
    await userEvent.keyboard('{Enter}')

    await waitFor(() => expect(mocks.addOwner).toHaveBeenCalledWith('d1', 'u2'))
    // The picked match left with the list it was in; the box is still there and takes the focus.
    expect(field()).toHaveFocus()
  })

  // The query belonged to the row that was being edited: another row opening on it offers matches
  // for a search nobody made here.
  it('opens a second row with an empty search box', async () => {
    await renderTab()
    await search(0, 'weesky')

    fireEvent.click(pencils()[0]) // the other row's pencil: the edited row draws none

    expect(field()).toHaveValue('')
    expect(screen.queryByRole('button', { name: /ada@weesky\.be/ })).not.toBeInTheDocument()
  })
})
