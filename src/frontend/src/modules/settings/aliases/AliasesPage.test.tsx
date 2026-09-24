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
    await userEvent.type(screen.getByPlaceholderText('Search or create…'), 'alias1')
    expect(screen.getByText('alias1')).toBeInTheDocument()
    expect(screen.queryByText('alias2')).not.toBeInTheDocument()
  })

  it('shows an error toast when search term exceeds 30 characters', async () => {
    renderPage()
    await screen.findByText('alias1')
    await userEvent.type(screen.getByPlaceholderText('Search or create…'), 'a'.repeat(31))
    expect(await screen.findByText('An alias cannot exceed 30 characters')).toBeInTheDocument()
  })

  it('hides the domain select with a single domain', async () => {
    renderPage()
    await screen.findByText('alias1')
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument()
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
    await waitFor(() => expect(screen.getByRole('combobox')).toBeInTheDocument())
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
    await userEvent.type(screen.getByPlaceholderText('Search or create…'), 'new')
    await userEvent.click(screen.getByRole('button', { name: 'Create alias' }))
    await waitFor(() => expect(mocks.createAlias).toHaveBeenCalledWith('new', 'weesky.be'))
    expect(await screen.findByText('new@weesky.be added')).toBeInTheDocument()
  })

  // Server prose never reaches the toast; the local fallback does — see apiErrorMessage.
  it('shows the local fallback when alias creation fails', async () => {
    mocks.createAlias.mockRejectedValue(new Error('Alias exists'))
    renderPage()
    await screen.findByText('alias1')
    await userEvent.type(screen.getByPlaceholderText('Search or create…'), 'bad')
    await userEvent.click(screen.getByRole('button', { name: 'Create alias' }))
    expect(await screen.findByText('Failed to create alias.')).toBeInTheDocument()
  })

  // Both caches hold a 5-minute staleTime, so without these the identity picker misses a
  // fresh alias and the composer's From menu keeps offering a deleted one.
  it('invalidates the alias and identity caches after a create', async () => {
    mocks.createAlias.mockResolvedValue(null)
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries')
    renderPage()
    await screen.findByText('alias1')
    await userEvent.type(screen.getByPlaceholderText('Search or create…'), 'new')
    await userEvent.click(screen.getByRole('button', { name: 'Create alias' }))
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

  it('persists the alphabetical toggle locally when toggled', async () => {
    renderPage()
    const toggle = await screen.findByRole('checkbox', { name: /alphabetical/i })
    fireEvent.click(toggle)
    expect(localStorage.getItem('alias_alpha_mode')).toBe('true')
  })

  it('reads alpha mode from localStorage on initial render', async () => {
    localStorage.setItem('alias_alpha_mode', 'true')
    mocks.getAliases.mockResolvedValue([
      { name: 'beta', domain: 'weesky.be' },
      { name: 'alpha', domain: 'weesky.be' },
    ])
    const { container } = renderPage()
    // alpha mode renders group letters as .alias-group-letter elements
    await waitFor(() => expect(container.querySelector('.alias-group-letter')).toBeTruthy())
    const letters = [...container.querySelectorAll('.alias-group-letter')].map(el => el.textContent)
    expect(letters).toContain('A')
    expect(letters).toContain('B')
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

  it('fires alpha nav letter click (scrollToLetter)', async () => {
    localStorage.setItem('alias_alpha_mode', 'true')
    mocks.getAliases.mockResolvedValue([
      { name: 'alpha', domain: 'weesky.be' },
      { name: 'beta', domain: 'weesky.be' },
    ])
    const { container } = renderPage()
    await waitFor(() => expect(container.querySelector('.alpha-nav-letter')).toBeTruthy())
    const navButtons = container.querySelectorAll('.alpha-nav-letter')
    await userEvent.click(navButtons[1]!) // click 'B'
    expect(navButtons[1]!).toBeInTheDocument()
  })

  it('fires scroll event in alpha mode (handleScroll)', async () => {
    localStorage.setItem('alias_alpha_mode', 'true')
    mocks.getAliases.mockResolvedValue([
      { name: 'alpha', domain: 'weesky.be' },
      { name: 'beta', domain: 'weesky.be' },
    ])
    const { container } = renderPage()
    await waitFor(() => expect(container.querySelector('.alias-scroll-area')).toBeTruthy())
    const scrollArea = container.querySelector('.alias-scroll-area')
    if (!scrollArea) throw new Error('scroll area not yet rendered')
    fireEvent.scroll(scrollArea)
    expect(container.querySelector('.alias-group-letter')).toBeTruthy()
  })

  it('clears alias highlight after animation ends (non-alpha mode)', async () => {
    mocks.createAlias.mockResolvedValue(null)
    mocks.getAliases
      .mockResolvedValueOnce(ALIASES)
      .mockResolvedValue([...ALIASES, { name: 'newone', domain: 'weesky.be' }])
    const { container } = renderPage()
    await screen.findByText('alias1')
    await userEvent.type(screen.getByPlaceholderText('Search or create…'), 'newone')
    await waitFor(() => expect(screen.getByRole('button', { name: 'Create alias' })).not.toBeDisabled())
    await userEvent.click(screen.getByRole('button', { name: 'Create alias' }))
    const newTile = await waitFor(
      () => {
        const el = container.querySelector('.alias-tile-new')
        if (!el) throw new Error('tile not yet highlighted')
        return el
      },
      { timeout: 3000 }
    )
    // Invoke the onAnimationEnd handler directly via React internal props. The cast is to the
    // untyped internal shape jsdom never fires animation events for; there is no public type for it.
    const propsKey = Object.keys(newTile).find(k => k.startsWith('__reactProps'))
    if (propsKey) {
      await act(async () => {
        (newTile as unknown as Record<string, { onAnimationEnd: () => void }>)[propsKey]!.onAnimationEnd()
      })
    }
    await waitFor(() => expect(container.querySelector('.alias-tile-new')).toBeNull())
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
    await waitFor(() => expect(screen.getByRole('combobox')).toBeInTheDocument())
    await userEvent.selectOptions(screen.getByRole('combobox'), 'example.com')
    expect(screen.getByRole('combobox')).toHaveValue('example.com')
  })

  it('deletes an alias in alpha mode', async () => {
    localStorage.setItem('alias_alpha_mode', 'true')
    mocks.deleteAlias.mockResolvedValue(null)
    mocks.getAliases.mockResolvedValue([{ name: 'alpha', domain: 'weesky.be' }])
    renderPage()
    await screen.findByText('alpha')
    await userEvent.click(screen.getByTitle('Delete'))
    await userEvent.click(await screen.findByText('Delete', { selector: 'button' }))
    await waitFor(() => expect(mocks.deleteAlias).toHaveBeenCalledWith('alpha', 'weesky.be'))
  })

  it('removes an error toast when its close button is clicked', async () => {
    mocks.createAlias.mockRejectedValue(new Error('Alias exists'))
    renderPage()
    await screen.findByText('alias1')
    await userEvent.type(screen.getByPlaceholderText('Search or create…'), 'bad')
    await userEvent.click(screen.getByRole('button', { name: 'Create alias' }))
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

  it('renders alpha mode with no aliases (empty state)', async () => {
    localStorage.setItem('alias_alpha_mode', 'true')
    mocks.getAliases.mockResolvedValue([])
    renderPage()
    expect(await screen.findByText('No aliases for this domain.')).toBeInTheDocument()
  })

  it('clears alias highlight after animation ends (alpha mode)', async () => {
    localStorage.setItem('alias_alpha_mode', 'true')
    mocks.createAlias.mockResolvedValue(null)
    mocks.getAliases
      .mockResolvedValueOnce([{ name: 'alpha', domain: 'weesky.be' }])
      .mockResolvedValue([{ name: 'alpha', domain: 'weesky.be' }, { name: 'newone', domain: 'weesky.be' }])
    const { container } = renderPage()
    await waitFor(() => expect(container.querySelector('.alias-group-letter')).toBeTruthy())
    await userEvent.type(screen.getByPlaceholderText('Search or create…'), 'newone')
    await waitFor(() => expect(screen.getByRole('button', { name: 'Create alias' })).not.toBeDisabled())
    await userEvent.click(screen.getByRole('button', { name: 'Create alias' }))
    const newTile = await waitFor(
      () => {
        const el = container.querySelector('.alias-tile-new')
        if (!el) throw new Error('tile not yet highlighted')
        return el
      },
      { timeout: 3000 }
    )
    const propsKey = Object.keys(newTile).find(k => k.startsWith('__reactProps'))
    if (propsKey) {
      await act(async () => {
        (newTile as unknown as Record<string, { onAnimationEnd: () => void }>)[propsKey]!.onAnimationEnd()
      })
    }
    await waitFor(() => expect(container.querySelector('.alias-tile-new')).toBeNull())
  })
})
