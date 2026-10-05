import { act, screen, waitFor } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { createTestQueryClient, holdNextCall, settle, setupUser } from '../../../test-utils'
import { DomainsTab } from './DomainsTab'
import { MOCK_DOMAINS, confirmButton, render } from './adminTestFixtures'

const mocks = vi.hoisted(() => ({
  adminGetDomains: vi.fn(),
  adminCreateDomain: vi.fn(),
  adminUpdateDomain: vi.fn(),
  adminDeleteDomain: vi.fn(),
}))

vi.mock('../../../api.js', () => ({ api: mocks, clearSession: vi.fn() }))

let user: ReturnType<typeof setupUser>
beforeEach(() => {
  vi.clearAllMocks()
  mocks.adminGetDomains.mockResolvedValue(MOCK_DOMAINS)
  user = setupUser()
})

async function renderLoaded(addToast = vi.fn()) {
  render(<DomainsTab addToast={addToast} />)
  await screen.findByText('WSY')
  return addToast
}

describe('DomainsTab', () => {
  it('loads once and does not reload on a re-render', async () => {
    const addToast = vi.fn()
    const { rerender } = render(<DomainsTab addToast={addToast} />)
    await screen.findByText('WSY')

    rerender(<DomainsTab addToast={addToast} />)
    await settle()

    expect(mocks.adminGetDomains).toHaveBeenCalledOnce()
  })

  it('renders the domain list', async () => {
    render(<DomainsTab addToast={vi.fn()} />)
    expect(await screen.findByText('WSY')).toBeInTheDocument()
    expect(screen.getByText('weesky.be')).toBeInTheDocument()
  })

  it('shows success toast after domain is created', async () => {
    mocks.adminCreateDomain.mockResolvedValue({})
    const addToast = await renderLoaded()
    await user.click(screen.getByRole('button', { name: /Add/ }))
    const [idInput, nameInput] = screen.getAllByRole('textbox') as [HTMLElement, HTMLElement]
    await user.type(idInput, 'TST')
    await user.type(nameInput, 'test.com')
    await user.click(screen.getByRole('button', { name: 'Create domain' }))
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Domain created'))
  })

  it('shows success toast after domain is updated', async () => {
    mocks.adminUpdateDomain.mockResolvedValue({})
    const addToast = await renderLoaded()
    await user.click(screen.getByTitle('Edit'))
    await user.click(screen.getByRole('button', { name: 'Save changes' }))
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Domain updated'))
  })

  it('calls adminDeleteDomain and shows toast after deletion', async () => {
    mocks.adminDeleteDomain.mockResolvedValue(null)
    const addToast = await renderLoaded()
    await user.click(screen.getByTitle('Delete'))
    await user.click(confirmButton())
    await waitFor(() => expect(mocks.adminDeleteDomain).toHaveBeenCalledWith('WSY', false))
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Domain weesky.be deleted'))
  })

  // The aliases cascade away with the domain and no screen anywhere lists another user's, so the
  // count on the confirmation is the only warning the admin will ever get.
  it('says how many aliases go with the domain before deleting it', async () => {
    mocks.adminGetDomains.mockResolvedValue([{ id: 'WSY', name: 'weesky.be', aliasCount: 3 }])
    await renderLoaded()

    await user.click(screen.getByTitle('Delete'))

    expect(screen.getByText(/3 aliases/)).toBeInTheDocument()
    expect(screen.getByText(/stop being delivered/)).toBeInTheDocument()
  })

  it('reads the count as singular when one alias goes', async () => {
    mocks.adminGetDomains.mockResolvedValue([{ id: 'WSY', name: 'weesky.be', aliasCount: 1 }])
    await renderLoaded()

    await user.click(screen.getByTitle('Delete'))

    expect(screen.getByText(/1 alias(?!es)/)).toBeInTheDocument()
    expect(screen.getByText(/that address/)).toBeInTheDocument()
  })

  // Confirming *is* the acknowledgement: without it on the wire the API refuses, and the admin
  // would be shown a warning that changed nothing.
  it('acknowledges the cascade once the admin has confirmed', async () => {
    mocks.adminGetDomains.mockResolvedValue([{ id: 'WSY', name: 'weesky.be', aliasCount: 3 }])
    mocks.adminDeleteDomain.mockResolvedValue(null)
    await renderLoaded()

    await user.click(screen.getByTitle('Delete'))
    await user.click(confirmButton())

    await waitFor(() => expect(mocks.adminDeleteDomain).toHaveBeenCalledWith('WSY', true))
  })

  it('keeps the plain wording when the domain holds no alias', async () => {
    mocks.adminGetDomains.mockResolvedValue([{ id: 'WSY', name: 'weesky.be', aliasCount: 0 }])
    await renderLoaded()

    await user.click(screen.getByTitle('Delete'))

    expect(screen.getByText(/This action cannot be undone/)).toBeInTheDocument()
    expect(screen.queryByText(/stop being delivered/)).not.toBeInTheDocument()
  })

  // Server prose never reaches the toast, nor does its absence: the local fallback does — see
  // apiErrorMessage.
  it.each([['a server message', 'Has users'], ['no message', undefined]])(
    'shows the local fallback when delete fails with %s', async (_case, message) => {
      mocks.adminDeleteDomain.mockRejectedValue(new Error(message))
      const addToast = await renderLoaded()
      await user.click(screen.getByTitle('Delete'))
      await user.click(confirmButton())
      await waitFor(() => expect(addToast).toHaveBeenCalledWith('Failed to delete domain', 'error'))
    })

  it('✕ closes delete modal without deleting', async () => {
    await renderLoaded()
    await user.click(screen.getByTitle('Delete'))
    await user.click(screen.getByRole('button', { name: 'Close' }))
    expect(mocks.adminDeleteDomain).not.toHaveBeenCalled()
    expect(screen.queryByText('Confirm deletion')).not.toBeInTheDocument()
  })

  it('opens the add domain modal on Add and closes it when ✕ is clicked', async () => {
    await renderLoaded()
    await user.click(screen.getByRole('button', { name: /Add/ }))
    expect(screen.getByRole('button', { name: 'Create domain' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Close' }))
    expect(screen.queryByRole('button', { name: 'Create domain' })).not.toBeInTheDocument()
  })

  it('opens the edit domain modal on Edit and closes it when ✕ is clicked', async () => {
    await renderLoaded()
    await user.click(screen.getByTitle('Edit'))
    expect(screen.getByRole('button', { name: 'Save changes' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Close' }))
    expect(screen.queryByRole('button', { name: 'Save changes' })).not.toBeInTheDocument()
  })

  it('draws an empty list when adminGetDomains answers null', async () => {
    mocks.adminGetDomains.mockResolvedValue(null)
    render(<DomainsTab addToast={vi.fn()} />)
    expect(await screen.findByText('Domains (0)')).toBeInTheDocument()
    expect(screen.queryByText('WSY')).not.toBeInTheDocument()
  })
})

describe('DomainsTab — load failures', () => {
  it('draws no spinner and no second toast when a tab refetches its own failed list', async () => {
    mocks.adminGetDomains.mockRejectedValue(new Error('Server error'))
    const addToast = vi.fn()
    const client = createTestQueryClient()
    render(<DomainsTab addToast={addToast} />, client)
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Failed to load domains', 'error'))

    const refetch = holdNextCall(mocks.adminGetDomains)

    act(() => { void client.invalidateQueries({ queryKey: ['admin'] }) })
    await settle()
    expect(screen.queryByRole('status', { name: 'Loading' })).not.toBeInTheDocument()
    await act(async () => { refetch.fail() })
    await settle()

    expect(mocks.adminGetDomains).toHaveBeenCalledTimes(2)
    expect(addToast).toHaveBeenCalledTimes(1)
  })

  // The failure belongs to the cached list, not to this mount: a tab opened again over a list
  // that failed earlier is a first load for the admin, and the old failure is not news.
  it('treats a remount over a failed list as a first load', async () => {
    mocks.adminGetDomains.mockRejectedValue(new Error('Server error'))
    const client = createTestQueryClient()
    const firstToast = vi.fn()
    const { unmount } = render(<DomainsTab addToast={firstToast} />, client)
    await waitFor(() => expect(firstToast).toHaveBeenCalledTimes(1))
    unmount()

    const again = holdNextCall(mocks.adminGetDomains)
    const addToast = vi.fn()

    render(<DomainsTab addToast={addToast} />, client)
    await settle()
    expect(screen.getByRole('status', { name: 'Loading' })).toBeInTheDocument()
    expect(addToast).not.toHaveBeenCalled()
    await act(async () => { again.resolve(MOCK_DOMAINS) })

    expect(await screen.findByText('WSY')).toBeInTheDocument()
    expect(addToast).not.toHaveBeenCalled()
  })
})
