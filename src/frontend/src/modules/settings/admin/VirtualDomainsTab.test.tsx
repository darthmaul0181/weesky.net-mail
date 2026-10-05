import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import VirtualDomainsTab from './VirtualDomainsTab'
import type { AdminUser } from './adminTypes'
import { createTestQueryClient, settle, setupUser } from '../../../test-utils'
import { BOB, MOCK_USERS, MOCK_VIRTUAL_DOMAINS } from './adminTestFixtures'

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

const adminUser = (id: number, userName: string, fullName: string): AdminUser => ({
  id, userName, fullName, domainId: 'WSY', domainName: 'weesky.be', quotaMb: 1024, active: true, admin: false, lastLogins: [],
})
const users = [adminUser(1, 'ada', 'Ada Lovelace'), adminUser(2, 'grace', 'Grace Hopper'), adminUser(3, 'alan', 'Alan Turing')]

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
    await setupUser().keyboard('{Enter}')

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

    await setupUser().keyboard('{Enter}')

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

describe('VirtualDomainsTab — owners of the admin fixtures', () => {
  let user: ReturnType<typeof setupUser>
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getUsers.mockResolvedValue(MOCK_USERS)
    mocks.getVirtualDomains.mockResolvedValue(MOCK_VIRTUAL_DOMAINS)
    user = setupUser()
  })

  async function renderLoaded(addToast = vi.fn()) {
    render(<VirtualDomainsTab addToast={addToast} />, { wrapper })
    await screen.findByText('extra.com')
    return addToast
  }

  async function searchOrphan(term: string) {
    await user.click(pencils()[1]!)
    await user.type(field(), term)
  }

  it('loads once and does not reload on a re-render', async () => {
    const addToast = vi.fn()
    const { rerender } = render(<VirtualDomainsTab addToast={addToast} />, { wrapper })
    await screen.findByText('extra.com')

    rerender(<VirtualDomainsTab addToast={addToast} />)
    await settle()

    expect(mocks.getVirtualDomains).toHaveBeenCalledOnce()
    expect(mocks.getUsers).toHaveBeenCalledOnce()
  })

  it('renders domain names after loading', async () => {
    render(<VirtualDomainsTab addToast={vi.fn()} />, { wrapper })
    expect(await screen.findByText('extra.com')).toBeInTheDocument()
    expect(screen.getByText('orphan.net')).toBeInTheDocument()
  })

  it('renders owner email for owned domains', async () => {
    render(<VirtualDomainsTab addToast={vi.fn()} />, { wrapper })
    expect(await screen.findByText('alice@weesky.be')).toBeInTheDocument()
  })

  it('renders — for unowned domains', async () => {
    await renderLoaded()
    expect(screen.getByText('—')).toBeInTheDocument()
  })

  it('shows "No alias domains" when list is empty', async () => {
    mocks.getVirtualDomains.mockResolvedValue([])
    render(<VirtualDomainsTab addToast={vi.fn()} />, { wrapper })
    expect(await screen.findByText('No virtual alias domains')).toBeInTheDocument()
  })

  it('offers only the users matching what is typed', async () => {
    mocks.getUsers.mockResolvedValue([...MOCK_USERS, BOB])
    await renderLoaded()
    await searchOrphan('alice')
    expect(await screen.findByRole('button', { name: /alice@weesky\.be/ })).toBeVisible()
    expect(screen.queryByRole('button', { name: /bob@weesky\.be/ })).not.toBeInTheDocument()
  })

  it('calls adminAddVirtualDomainOwner when a user is selected from dropdown', async () => {
    mocks.addOwner.mockResolvedValue({ domainId: 'ORF', domainName: 'orphan.net', owners: [{ ownerId: 1, ownerEmail: 'alice@weesky.be' }] })
    await renderLoaded()
    await searchOrphan('alice')
    const choice = await screen.findByRole('button', { name: /alice@weesky\.be/ })
    // The press only holds the caret in the box; the click is what picks, so the keyboard can too.
    fireEvent.click(choice)
    await waitFor(() => expect(mocks.addOwner).toHaveBeenCalledWith('ORF', 1))
  })

  it('shows remove button only for owned domains', async () => {
    await renderLoaded()
    await user.click(pencils()[0]!)
    expect(screen.getByTitle('Remove owner')).toBeInTheDocument()
  })

  it('does not show remove button for unowned domains', async () => {
    await renderLoaded()
    await user.click(pencils()[1]!)
    expect(screen.queryByTitle('Remove owner')).not.toBeInTheDocument()
  })

  it('calls adminRemoveVirtualDomainOwner when Remove owner is clicked', async () => {
    mocks.removeOwner.mockResolvedValue(null)
    await renderLoaded()
    await user.click(pencils()[0]!)
    // The press only holds the caret in the box; the click is what removes, keyboard included.
    fireEvent.click(screen.getByTitle('Remove owner'))
    await waitFor(() => expect(mocks.removeOwner).toHaveBeenCalledWith('EXT', 1))
  })

  // Two surfaces, two Escapes: the list the search opened is the nearer one, and cancelling the
  // whole edit on the key that dismisses it throws away the query with it.
  it('closes the user list on Escape and cancels the edit only on the next one', async () => {
    await renderLoaded()
    await searchOrphan('alice')
    const input = field()
    await screen.findByRole('button', { name: /alice@weesky\.be/ })

    await user.keyboard('{Escape}')

    expect(screen.queryByRole('button', { name: /alice@weesky\.be/ })).not.toBeInTheDocument()
    expect(input).toBeInTheDocument()
    expect(input).toHaveValue('alice')

    await user.keyboard('{Escape}')

    expect(input).not.toBeInTheDocument()
  })

  // Server prose never reaches the toast; the local fallback does — see apiErrorMessage.
  it('shows the local fallback when add owner fails', async () => {
    mocks.addOwner.mockRejectedValue(new Error('Domain not found'))
    const addToast = await renderLoaded()
    await searchOrphan('alice')
    fireEvent.click(await screen.findByRole('button', { name: /alice@weesky\.be/ }))
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Failed to set owner', 'error'))
  })

  it('re-reads the virtual domains after a refused owner change', async () => {
    mocks.addOwner.mockRejectedValue(new Error('Domain not found'))
    await renderLoaded()
    await searchOrphan('alice')
    fireEvent.click(await screen.findByRole('button', { name: /alice@weesky\.be/ }))
    await waitFor(() => expect(mocks.getVirtualDomains).toHaveBeenCalledTimes(2))
    expect(screen.getByText('orphan.net')).toBeInTheDocument()
  })

  it('draws the empty state when adminGetVirtualDomains answers null', async () => {
    mocks.getVirtualDomains.mockResolvedValue(null)
    render(<VirtualDomainsTab addToast={vi.fn()} />, { wrapper })
    expect(await screen.findByText('No virtual alias domains')).toBeInTheDocument()
    expect(screen.queryByText('extra.com')).not.toBeInTheDocument()
  })

  it('lists the virtual domains when only the users fail, naming the users', async () => {
    mocks.getUsers.mockRejectedValue(new Error('Server error'))
    const addToast = await renderLoaded()
    await settle()
    expect(addToast.mock.calls).toEqual([['Failed to load the account list', 'error']])
  })
})
