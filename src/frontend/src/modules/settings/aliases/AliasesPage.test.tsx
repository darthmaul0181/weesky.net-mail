import { render, screen, waitFor, fireEvent, act } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { MemoryRouter } from 'react-router'
import { QueryClientProvider, type QueryClient } from '@tanstack/react-query'
import { createTestQueryClient, holdNextCall, settle } from '../../../test-utils'
import { mailKeys } from '../../mail/queries'
import AliasesPage from './AliasesPage'

const mocks = vi.hoisted(() => ({
  getAccount: vi.fn(),
  getAliases: vi.fn(),
  createAlias: vi.fn(),
  deleteAlias: vi.fn(),
}))
vi.mock('../../../api.js', () => ({ api: mocks }))
// The page reads the active account through useAccountId, which is the real hook here — only
// its auth source is stubbed, so the cache keys under test are the ones the app really uses.
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({ activeAccount: { id: 'primary' }, activeAccountId: 'primary' }),
}))

const ACCOUNT = {
  userName: 'john',
  fullName: 'John Doe',
  mailbox: 'WSY',
  domains: [{ id: 'WSY', name: 'weesky.be' }],
  isAdmin: false,
}
const ALIASES = [
  { name: 'alias1', domain: 'weesky.be' },
  { name: 'alias2', domain: 'weesky.be' },
]

let queryClient: QueryClient

beforeEach(() => {
  vi.clearAllMocks()
  localStorage.clear()
  mocks.getAccount.mockResolvedValue(ACCOUNT)
  mocks.getAliases.mockResolvedValue(ALIASES)
  queryClient = createTestQueryClient()
})

// ── AliasesPage ───────────────────────────────────────────────

describe('AliasesPage', () => {
  function renderPage() {
    return render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <AliasesPage />
        </MemoryRouter>
      </QueryClientProvider>
    )
  }

  it('shows alias tiles after loading', async () => {
    renderPage()
    expect(await screen.findByText('alias1')).toBeInTheDocument()
    expect(screen.getByText('alias2')).toBeInTheDocument()
  })

  it("counts the domain's addresses under the title", async () => {
    renderPage()
    expect(await screen.findByText('2 receiving addresses · repeated prefixes are written once')).toBeInTheDocument()
  })

  it('shows the empty state when there are no aliases', async () => {
    mocks.getAliases.mockResolvedValue([])
    renderPage()
    expect(await screen.findByText('No aliases for this domain.')).toBeInTheDocument()
  })

  it('shows an error alert when alias load fails', async () => {
    mocks.getAliases.mockRejectedValue(new Error('net'))
    renderPage()
    expect(await screen.findByText('Failed to load aliases.')).toBeInTheDocument()
  })

  // R2's user-visible change, on a page rather than a tab: a mount with the alias list already
  // in the query cache (a route left and returned to) shows it at once, behind no spinner.
  it('shows the cached list at once when returning to Aliases', async () => {
    const { unmount } = renderPage()
    await screen.findByText('alias1')
    unmount()

    const { container } = renderPage()
    expect(screen.getByText('alias1')).toBeInTheDocument()
    expect(container.querySelector('.loading-center')).not.toBeInTheDocument()
    expect(mocks.getAliases).toHaveBeenCalledTimes(1)
  })

  // The page is where aliases are managed, so it reads them fresher than the mail's five-minute
  // cache: past thirty seconds a visit asks again, within them it keeps what it has.
  describe('freshness', () => {
    beforeEach(() => { vi.useFakeTimers({ toFake: ['Date'] }) })
    afterEach(() => { vi.useRealTimers() })

    it.each([
      ['keeps the cached list within thirty seconds', 29_000, 1],
      ['reads the list again past thirty seconds', 31_000, 2],
    ])('%s', async (_, elapsed, calls) => {
      const { unmount } = renderPage()
      await screen.findByText('alias1')
      unmount()

      vi.setSystemTime(Date.now() + elapsed)
      renderPage()
      await settle()
      expect(mocks.getAliases).toHaveBeenCalledTimes(calls)
    })
  })

  // A refetch of a list holding no data puts it back to pending, so a spinner drawn on `isLoading`
  // alone would reappear on every retry; and the failure stays failed until data arrives, so the
  // role="alert" banner — the page's one announcement — must not be torn down and redrawn either.
  it('shows no spinner and no second error announcement on a refetch after a failed load', async () => {
    mocks.getAliases.mockRejectedValue(new Error('net'))
    renderPage()
    await screen.findByText('Failed to load aliases.')
    const banner = screen.getByRole('alert')

    const refetch = holdNextCall(mocks.getAliases)
    act(() => { void queryClient.invalidateQueries({ queryKey: mailKeys.aliases('primary') }) })
    await settle()

    expect(document.querySelector('.loading-center')).not.toBeInTheDocument()
    // The same DOM node, not a new one: a role="alert" only announces on insertion or a text
    // change, so this is what proves the refetch drew no second announcement.
    expect(screen.getByRole('alert')).toBe(banner)

    await act(async () => { refetch.fail() })
    await settle()

    expect(mocks.getAliases).toHaveBeenCalledTimes(2)
    expect(screen.getByRole('alert')).toBe(banner)
  })

  it('filters visible aliases by search term', async () => {
    renderPage()
    await screen.findByText('alias1')
    await userEvent.type(screen.getByPlaceholderText('Type to filter the index, or to create an address'), 'alias1')
    expect(screen.getByText('alias1')).toBeInTheDocument()
    expect(screen.queryByText('alias2')).not.toBeInTheDocument()
    expect(screen.getByText('1 of 2 addresses match')).toBeInTheDocument()
  })

  it('shows an error toast when search term exceeds 30 characters', async () => {
    renderPage()
    await screen.findByText('alias1')
    await userEvent.type(screen.getByPlaceholderText('Type to filter the index, or to create an address'), 'a'.repeat(31))
    expect(await screen.findByText('An alias cannot exceed 30 characters')).toBeInTheDocument()
  })

  it('hides the domain select with a single domain', async () => {
    renderPage()
    await screen.findByText('alias1')
    expect(screen.queryByRole('button', { name: 'Domain' })).not.toBeInTheDocument()
  })

  it('shows the domain select with multiple domains', async () => {
    mocks.getAccount.mockResolvedValue({
      ...ACCOUNT,
      domains: [
        { id: 'WSY', name: 'weesky.be' },
        { id: 'EXM', name: 'example.com' },
      ],
    })
    renderPage()
    expect(await screen.findByRole('button', { name: 'Domain' })).toHaveTextContent('@weesky.be')
  })

  it('deletes an alias when the delete button is clicked', async () => {
    mocks.deleteAlias.mockResolvedValue(null)
    // The delete's own success already patches the cache; the settled invalidate that follows it
    // re-reads the server, which has to answer with the alias gone too, or the refetch would
    // stomp the very removal it is meant to reconcile.
    mocks.getAliases.mockResolvedValueOnce(ALIASES).mockResolvedValue(ALIASES.filter(a => a.name !== 'alias1'))
    renderPage()
    await screen.findByText('alias1')
    await userEvent.click(screen.getAllByTitle('Delete')[0]!)
    await userEvent.click(await screen.findByText('Delete', { selector: 'button' }))
    await waitFor(() => expect(mocks.deleteAlias).toHaveBeenCalledWith('alias1', 'weesky.be'))
    await waitFor(() => expect(screen.queryByText('alias1')).not.toBeInTheDocument())
  })

  // The tile's own delete goes with the tile in the very commit the confirm closes in, so focus
  // falls back to what the page is called rather than to <body>.
  it('hands focus to the page heading when the deleted tile takes its button', async () => {
    mocks.deleteAlias.mockResolvedValue(null)
    mocks.getAliases.mockResolvedValueOnce(ALIASES).mockResolvedValue(ALIASES.filter(a => a.name !== 'alias1'))
    renderPage()
    await screen.findByText('alias1')
    await userEvent.click(screen.getAllByTitle('Delete')[0]!)

    await userEvent.click(await screen.findByText('Delete', { selector: 'button' }))

    await waitFor(() => expect(screen.queryByText('alias1')).not.toBeInTheDocument())
    expect(screen.getByRole('heading', { name: 'Aliases' })).toHaveFocus()
  })

  // The control for the latency cases in the other modules: this page drops the tile from its own
  // state on the same round trip, so a slow DELETE must change nothing about where focus lands.
  it('hands focus to the page heading even when the delete answers late', async () => {
    mocks.deleteAlias.mockImplementation(() => new Promise(resolve => setTimeout(() => resolve(null), 30)))
    mocks.getAliases.mockResolvedValueOnce(ALIASES).mockResolvedValue(ALIASES.filter(a => a.name !== 'alias1'))
    renderPage()
    await screen.findByText('alias1')
    await userEvent.click(screen.getAllByTitle('Delete')[0]!)

    await userEvent.click(await screen.findByText('Delete', { selector: 'button' }))

    await waitFor(() => expect(screen.queryByText('alias1')).not.toBeInTheDocument())
    expect(screen.getByRole('heading', { name: 'Aliases' })).toHaveFocus()
  })

  it('shows a success toast when an alias is created', async () => {
    mocks.createAlias.mockResolvedValue(null)
    mocks.getAliases
      .mockResolvedValueOnce(ALIASES)
      .mockResolvedValue([...ALIASES, { name: 'new', domain: 'weesky.be' }])
    renderPage()
    await screen.findByText('alias1')
    await userEvent.type(screen.getByPlaceholderText('Type to filter the index, or to create an address'), 'new')
    await userEvent.click(screen.getByRole('button', { name: 'Create' }))
    await waitFor(() => expect(mocks.createAlias).toHaveBeenCalledWith('new', 'weesky.be'))
    expect(await screen.findByText('new@weesky.be added')).toBeInTheDocument()
  })

  // Server prose never reaches the toast; the local fallback does — see apiErrorMessage.
  it('shows the local fallback when alias creation fails', async () => {
    mocks.createAlias.mockRejectedValue(new Error('Alias exists'))
    renderPage()
    await screen.findByText('alias1')
    await userEvent.type(screen.getByPlaceholderText('Type to filter the index, or to create an address'), 'bad')
    await userEvent.click(screen.getByRole('button', { name: 'Create' }))
    expect(await screen.findByText('Failed to create alias.')).toBeInTheDocument()
  })

  // Both caches hold a 5-minute staleTime, so without these the identity picker misses a
  // fresh alias and the composer's From menu keeps offering a deleted one.
  it('invalidates the alias and identity caches after a create', async () => {
    mocks.createAlias.mockResolvedValue(null)
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries')
    renderPage()
    await screen.findByText('alias1')
    await userEvent.type(screen.getByPlaceholderText('Type to filter the index, or to create an address'), 'new')
    await userEvent.click(screen.getByRole('button', { name: 'Create' }))
    await waitFor(() =>
      expect(invalidate).toHaveBeenCalledWith({ queryKey: mailKeys.aliases('primary') }))
    expect(invalidate).toHaveBeenCalledWith({ queryKey: mailKeys.identities('primary') })
  })

  it('invalidates the alias and identity caches after a delete', async () => {
    mocks.deleteAlias.mockResolvedValue(null)
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries')
    renderPage()
    await screen.findByText('alias1')
    await userEvent.click(screen.getAllByTitle('Delete')[0]!)
    await userEvent.click(await screen.findByText('Delete', { selector: 'button' }))
    await waitFor(() =>
      expect(invalidate).toHaveBeenCalledWith({ queryKey: mailKeys.aliases('primary') }))
    expect(invalidate).toHaveBeenCalledWith({ queryKey: mailKeys.identities('primary') })
  })

  it('files names under their letter, one family of three per line, in lower case', async () => {
    mocks.getAliases.mockResolvedValue([
      { name: 'darth_ebay', domain: 'weesky.be' }, { name: 'Dorian', domain: 'weesky.be' },
      { name: 'darth_amazon', domain: 'weesky.be' }, { name: 'darth_ups', domain: 'weesky.be' },
      { name: 'abuse', domain: 'weesky.be' },
    ])
    renderPage()
    await screen.findByText('abuse')
    const rows = screen.getAllByRole('row').map(row =>
      [...row.querySelectorAll('.alias-cell-name')].map(name => name.textContent).join(','))
    expect(rows).toEqual(['abuse', 'dorian', 'amazon,ebay,ups'])
    expect(screen.getByRole('rowheader', { name: 'D, darth_… (3 aliases)' })).toBeInTheDocument()
    expect(screen.getByRole('gridcell', { name: 'darth_ups@weesky.be' })).toBeInTheDocument()
  })

  it('copies the full address from the bubble', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })
    renderPage()
    await screen.findByText('alias1')
    await userEvent.click(screen.getAllByTitle('Copy the address')[1]!)
    expect(writeText).toHaveBeenCalledWith('alias2@weesky.be')
    expect(await screen.findByText('alias2@weesky.be copied')).toBeInTheDocument()
  })

  it('says so when the clipboard refuses the address', async () => {
    Object.defineProperty(navigator, 'clipboard',
      { value: { writeText: vi.fn().mockRejectedValue(new Error('denied')) }, configurable: true })
    renderPage()
    await screen.findByText('alias1')
    await userEvent.click(screen.getAllByTitle('Copy the address')[0]!)
    expect(await screen.findByText('Could not copy the address.')).toBeInTheDocument()
  })

  // One stop for the whole index: Tab lands on the first name, the arrows walk the names, and F2
  // is the way into one — where its copy and delete are.
  it('walks the names with the arrows and enters one with F2', async () => {
    renderPage()
    await screen.findByText('alias1')
    screen.getByPlaceholderText('Type to filter the index, or to create an address').focus()
    await userEvent.tab()
    expect(screen.getByRole('gridcell', { name: 'alias1@weesky.be' })).toHaveFocus()
    await userEvent.keyboard('{ArrowRight}{F2}')
    expect(screen.getAllByTitle('Copy the address')[1]).toHaveFocus()
    await userEvent.keyboard('{Escape}')
    expect(screen.getByRole('gridcell', { name: 'alias2@weesky.be' })).toHaveFocus()
  })

  it('shows a success toast after deleting an alias', async () => {
    mocks.deleteAlias.mockResolvedValue(null)
    renderPage()
    await screen.findByText('alias1')
    await userEvent.click(screen.getAllByTitle('Delete')[0]!)
    await userEvent.click(await screen.findByText('Delete', { selector: 'button' }))
    expect(await screen.findByText('alias1@weesky.be deleted')).toBeInTheDocument()
  })

  it('handles getAccount failure gracefully', async () => {
    mocks.getAccount.mockRejectedValue(new Error('Server error'))
    renderPage()
    expect(await screen.findByText('alias1')).toBeInTheDocument()
  })

  // Server prose never reaches the toast; the local fallback does — see apiErrorMessage.
  it('shows the local fallback and reloads aliases when delete fails', async () => {
    mocks.deleteAlias.mockRejectedValue(new Error('Not found'))
    renderPage()
    await screen.findByText('alias1')
    await userEvent.click(screen.getAllByTitle('Delete')[0]!)
    await userEvent.click(await screen.findByText('Delete', { selector: 'button' }))
    expect(await screen.findByText('Failed to delete alias.')).toBeInTheDocument()
    await waitFor(() => expect(mocks.getAliases).toHaveBeenCalledTimes(2))
  })

  it('scrolls to a letter from the letter rail', async () => {
    mocks.getAliases.mockResolvedValue([
      { name: 'alpha', domain: 'weesky.be' },
      { name: 'beta', domain: 'weesky.be' },
    ])
    const { container } = renderPage()
    await screen.findByText('beta')
    const area = container.querySelector<HTMLElement>('.alias-scroll-area')!
    expect(screen.getByRole('button', { name: 'A' })).toHaveClass('is-active')
    // jsdom lays nothing out, so every letter reads as reached and the last one lights.
    await userEvent.click(screen.getByRole('button', { name: 'B' }))
    fireEvent.scroll(area)
    expect(screen.getByRole('button', { name: 'B' })).toHaveClass('is-active')
  })

  it('clears the new alias highlight after its animation ends', async () => {
    mocks.createAlias.mockResolvedValue(null)
    mocks.getAliases
      .mockResolvedValueOnce(ALIASES)
      .mockResolvedValue([...ALIASES, { name: 'newone', domain: 'weesky.be' }])
    const { container } = renderPage()
    await screen.findByText('alias1')
    await userEvent.type(screen.getByPlaceholderText('Type to filter the index, or to create an address'), 'NewOne')
    await userEvent.click(screen.getByRole('button', { name: 'Create' }))
    const cell = await waitFor(() => {
      const el = container.querySelector('.alias-cell.is-new')
      if (!el) throw new Error('name not yet highlighted')
      return el
    })
    // jsdom never fires animation events React listens for, so the handler is called through
    // React's own props; there is no public type for that internal shape.
    const propsKey = Object.keys(cell).find(k => k.startsWith('__reactProps'))!
    act(() => { (cell as unknown as Record<string, { onAnimationEnd: () => void }>)[propsKey]!.onAnimationEnd() })
    await waitFor(() => expect(container.querySelector('.alias-cell.is-new')).toBeNull())
  })

  it('changes the selected domain in the domain toolbar', async () => {
    mocks.getAccount.mockResolvedValue({
      ...ACCOUNT,
      domains: [
        { id: 'WSY', name: 'weesky.be' },
        { id: 'EXM', name: 'example.com' },
      ],
    })
    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Domain' }))
    await userEvent.click(screen.getByRole('menuitem', { name: '@example.com' }))
    expect(screen.getByRole('button', { name: 'Domain' })).toHaveTextContent('@example.com')
    expect(screen.getByText('@example.com', { selector: '.alias-composer-domain' })).toBeInTheDocument()
  })

  it('removes an error toast when its close button is clicked', async () => {
    mocks.createAlias.mockRejectedValue(new Error('Alias exists'))
    renderPage()
    await screen.findByText('alias1')
    await userEvent.type(screen.getByPlaceholderText('Type to filter the index, or to create an address'), 'bad')
    await userEvent.click(screen.getByRole('button', { name: 'Create' }))
    await screen.findByText('Failed to create alias.')
    const closeBtn = await screen.findByRole('button', { name: 'Close' })
    await userEvent.click(closeBtn)
    await waitFor(() => expect(screen.queryByText('Failed to create alias.')).not.toBeInTheDocument())
  })

  it('handles getAliases returning null', async () => {
    mocks.getAliases.mockResolvedValue(null)
    renderPage()
    expect(await screen.findByText('No aliases for this domain.')).toBeInTheDocument()
  })

  it('handles account with no domains', async () => {
    mocks.getAccount.mockResolvedValue({
      userName: null,
      mailbox: null,
      domains: [],
      isAdmin: false,
    })
    renderPage()
    // page renders without crashing; aliases still show via the default mock
    expect(await screen.findByText('alias1')).toBeInTheDocument()
  })
})
