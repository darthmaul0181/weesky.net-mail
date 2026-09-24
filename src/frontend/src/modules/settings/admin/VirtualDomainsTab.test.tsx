import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import VirtualDomainsTab from './VirtualDomainsTab'
import type { AdminUser } from './adminTypes'
import { createTestQueryClient, settle } from '../../../test-utils'

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

const user = (id: number, userName: string, fullName: string): AdminUser => ({
  id, userName, fullName, domainId: 'WSY', domainName: 'weesky.be', quotaMb: 1024, active: true, admin: false, lastLogins: [],
})
const users = [user(1, 'ada', 'Ada Lovelace'), user(2, 'grace', 'Grace Hopper'), user(3, 'alan', 'Alan Turing')]

function wrapper({ children }: { children: ReactNode }) {
  const client = createTestQueryClient()
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

const field = () => screen.getByPlaceholderText(/Search user/)
const option = (name: string) => screen.getByRole('button', { name: new RegExp(name) })
const pencils = () => screen.getAllByTitle('Edit owner')

async function renderTab() {
  render(<VirtualDomainsTab addToast={vi.fn()} />, { wrapper })
  return waitFor(() => expect(pencils()).toHaveLength(2))
}

/** Opens the given row's owner editor and filters the user list down to three matches. */
async function search(row: number, term: string) {
  fireEvent.click(pencils()[row]!)
  fireEvent.change(field(), { target: { value: term } })
  await waitFor(() => expect(option('ada@weesky.be')).toBeInTheDocument())
}

describe('VirtualDomainsTab', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getUsers.mockResolvedValue(users)
    mocks.getVirtualDomains.mockResolvedValue([
      { domainId: 'd1', domainName: 'alpha.com', owners: [] },
      { domainId: 'd2', domainName: 'beta.com', owners: [{ ownerId: 1, ownerEmail: 'ada@weesky.be' }] },
    ])
  })

  // Both lists fail: the users toast names a list nothing on screen shows either, since the
  // virtual domains list — this tab's primary — already drew its own failure note.
  it('shows only the primary toast when both lists fail', async () => {
    mocks.getVirtualDomains.mockRejectedValue(new Error('Server error'))
    mocks.getUsers.mockRejectedValue(new Error('Server error'))
    const addToast = vi.fn()
    render(<VirtualDomainsTab addToast={addToast} />, { wrapper })

    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Failed to load virtual domains', 'error'))
    await settle()
    expect(addToast).toHaveBeenCalledTimes(1)
  })

  it('opens the owner editor with the focus in its search box', async () => {
    await renderTab()

    fireEvent.click(pencils()[0]!)

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
      owners: [{ ownerId: 2, ownerEmail: 'grace@weesky.be' }],
    })
    await renderTab()
    await search(0, 'weesky')

    fireEvent.keyDown(field(), { key: 'ArrowDown' })
    fireEvent.keyDown(option('ada@weesky.be'), { key: 'ArrowDown' })
    await userEvent.keyboard('{Enter}')

    await waitFor(() => expect(mocks.addOwner).toHaveBeenCalledWith('d1', 2))
    // The picked match left with the list it was in; the box is still there and takes the focus.
    expect(field()).toHaveFocus()
  })

  // Escape unmounts the editor holding the focus, and the pencil it was opened from is not on
  // screen at that moment: it has to claim the focus back as it is drawn again.
  it('hands the focus back to the pencil when Escape closes the editor', async () => {
    await renderTab()
    fireEvent.click(pencils()[0]!)

    fireEvent.keyDown(field(), { key: 'Escape' })

    expect(screen.queryByPlaceholderText(/Search user/)).not.toBeInTheDocument()
    expect(pencils()[0]).toHaveFocus()
  })

  // Tab lands on the chip's ✕, so Enter has to remove the owner — and the chip leaves with it.
  it('removes an owner from the keyboard and keeps the focus in the editor', async () => {
    mocks.removeOwner.mockResolvedValue(null)
    await renderTab()
    fireEvent.click(pencils()[1]!)
    const remove = screen.getByTitle('Remove owner')
    remove.focus()

    await userEvent.keyboard('{Enter}')

    await waitFor(() => expect(mocks.removeOwner).toHaveBeenCalledWith('d2', 1))
    expect(field()).toHaveFocus()
  })

  // The query belonged to the row that was being edited: another row opening on it offers matches
  // for a search nobody made here.
  it('opens a second row with an empty search box', async () => {
    await renderTab()
    await search(0, 'weesky')

    fireEvent.click(pencils()[0]!) // the other row's pencil: the edited row draws none

    expect(field()).toHaveValue('')
    expect(screen.queryByRole('button', { name: /ada@weesky\.be/ })).not.toBeInTheDocument()
  })
})
