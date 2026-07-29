import { render, screen, waitFor, fireEvent, act } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { api } from '../../../api.js'
import { mailKeys } from '../../mail/queries'
import AliasesPage from './AliasesPage.jsx'

vi.mock('../../../api.js', () => ({
  api: {
    getAccount: vi.fn(),
    getAliases: vi.fn(),
    createAlias: vi.fn(),
    deleteAlias: vi.fn(),
  },
}))
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

let queryClient

beforeEach(() => {
  vi.clearAllMocks()
  localStorage.clear()
  api.getAccount.mockResolvedValue(ACCOUNT)
  api.getAliases.mockResolvedValue(ALIASES)
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
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
    api.getAliases.mockResolvedValue([])
    renderPage()
    expect(await screen.findByText('No aliases for this domain.')).toBeInTheDocument()
  })

  it('shows an error alert when alias load fails', async () => {
    api.getAliases.mockRejectedValue(new Error('net'))
    renderPage()
    expect(await screen.findByText('Failed to load aliases.')).toBeInTheDocument()
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
    api.getAccount.mockResolvedValue({
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
    api.deleteAlias.mockResolvedValue(null)
    renderPage()
    await screen.findByText('alias1')
    await userEvent.click(screen.getAllByTitle('Delete')[0])
    await userEvent.click(await screen.findByText('Delete', { selector: 'button' }))
    await waitFor(() => expect(api.deleteAlias).toHaveBeenCalledWith('alias1', 'weesky.be'))
    await waitFor(() => expect(screen.queryByText('alias1')).not.toBeInTheDocument())
  })

  it('shows a success toast when an alias is created', async () => {
    api.createAlias.mockResolvedValue(null)
    api.getAliases
      .mockResolvedValueOnce(ALIASES)
      .mockResolvedValue([...ALIASES, { name: 'new', domain: 'weesky.be' }])
    renderPage()
    await screen.findByText('alias1')
    await userEvent.type(screen.getByPlaceholderText('Search or create…'), 'new')
    await userEvent.click(screen.getByRole('button', { name: 'Create alias' }))
    await waitFor(() => expect(api.createAlias).toHaveBeenCalledWith('new', 'weesky.be'))
    expect(await screen.findByText('new@weesky.be added')).toBeInTheDocument()
  })

  it('shows an error toast when alias creation fails', async () => {
    api.createAlias.mockRejectedValue(new Error('Alias exists'))
    renderPage()
    await screen.findByText('alias1')
    await userEvent.type(screen.getByPlaceholderText('Search or create…'), 'bad')
    await userEvent.click(screen.getByRole('button', { name: 'Create alias' }))
    expect(await screen.findByText('Alias exists')).toBeInTheDocument()
  })

  // Both caches hold a 5-minute staleTime, so without these the identity picker misses a
  // fresh alias and the composer's From menu keeps offering a deleted one.
  it('invalidates the alias and identity caches after a create', async () => {
    api.createAlias.mockResolvedValue(null)
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
    api.deleteAlias.mockResolvedValue(null)
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries')
    renderPage()
    await screen.findByText('alias1')
    await userEvent.click(screen.getAllByTitle('Delete')[0])
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
    api.getAliases.mockResolvedValue([
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
    api.deleteAlias.mockResolvedValue(null)
    renderPage()
    await screen.findByText('alias1')
    await userEvent.click(screen.getAllByTitle('Delete')[0])
    await userEvent.click(await screen.findByText('Delete', { selector: 'button' }))
    expect(await screen.findByText('alias1@weesky.be deleted')).toBeInTheDocument()
  })

  it('handles getAccount failure gracefully', async () => {
    api.getAccount.mockRejectedValue(new Error('Server error'))
    renderPage()
    expect(await screen.findByText('alias1')).toBeInTheDocument()
  })

  it('reports the reason and reloads aliases when delete fails', async () => {
    api.deleteAlias.mockRejectedValue(new Error('Not found'))
    renderPage()
    await screen.findByText('alias1')
    await userEvent.click(screen.getAllByTitle('Delete')[0])
    await userEvent.click(await screen.findByText('Delete', { selector: 'button' }))
    expect(await screen.findByText('Not found')).toBeInTheDocument()
    await waitFor(() => expect(api.getAliases).toHaveBeenCalledTimes(2))
  })

  it('uses fallback error message when alias deletion error has no message', async () => {
    api.deleteAlias.mockRejectedValue(new Error())
    renderPage()
    await screen.findByText('alias1')
    await userEvent.click(screen.getAllByTitle('Delete')[0])
    await userEvent.click(await screen.findByText('Delete', { selector: 'button' }))
    expect(await screen.findByText('Failed to delete alias.')).toBeInTheDocument()
  })

  it('fires alpha nav letter click (scrollToLetter)', async () => {
    localStorage.setItem('alias_alpha_mode', 'true')
    api.getAliases.mockResolvedValue([
      { name: 'alpha', domain: 'weesky.be' },
      { name: 'beta', domain: 'weesky.be' },
    ])
    const { container } = renderPage()
    await waitFor(() => expect(container.querySelector('.alpha-nav-letter')).toBeTruthy())
    const navButtons = container.querySelectorAll('.alpha-nav-letter')
    await userEvent.click(navButtons[1]) // click 'B'
    expect(navButtons[1]).toBeInTheDocument()
  })

  it('fires scroll event in alpha mode (handleScroll)', async () => {
    localStorage.setItem('alias_alpha_mode', 'true')
    api.getAliases.mockResolvedValue([
      { name: 'alpha', domain: 'weesky.be' },
      { name: 'beta', domain: 'weesky.be' },
    ])
    const { container } = renderPage()
    await waitFor(() => expect(container.querySelector('.alias-scroll-area')).toBeTruthy())
    fireEvent.scroll(container.querySelector('.alias-scroll-area'))
    expect(container.querySelector('.alias-group-letter')).toBeTruthy()
  })

  it('clears alias highlight after animation ends (non-alpha mode)', async () => {
    api.createAlias.mockResolvedValue(null)
    api.getAliases
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
    // Invoke the onAnimationEnd handler directly via React internal props
    const propsKey = Object.keys(newTile).find(k => k.startsWith('__reactProps'))
    if (propsKey) {
      await act(async () => { newTile[propsKey].onAnimationEnd() })
    }
    await waitFor(() => expect(container.querySelector('.alias-tile-new')).toBeNull())
  })

  it('changes the selected domain in the domain toolbar', async () => {
    api.getAccount.mockResolvedValue({
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
    api.deleteAlias.mockResolvedValue(null)
    api.getAliases.mockResolvedValue([{ name: 'alpha', domain: 'weesky.be' }])
    renderPage()
    await screen.findByText('alpha')
    await userEvent.click(screen.getByTitle('Delete'))
    await userEvent.click(await screen.findByText('Delete', { selector: 'button' }))
    await waitFor(() => expect(api.deleteAlias).toHaveBeenCalledWith('alpha', 'weesky.be'))
  })

  it('removes an error toast when its close button is clicked', async () => {
    api.createAlias.mockRejectedValue(new Error('Alias exists'))
    renderPage()
    await screen.findByText('alias1')
    await userEvent.type(screen.getByPlaceholderText('Search or create…'), 'bad')
    await userEvent.click(screen.getByRole('button', { name: 'Create alias' }))
    const closeBtn = await screen.findByRole('button', { name: '✕' })
    await userEvent.click(closeBtn)
    await waitFor(() => expect(screen.queryByText('Alias exists')).not.toBeInTheDocument())
  })

  it('uses fallback error message when alias creation error has no message', async () => {
    api.createAlias.mockRejectedValue(new Error())
    renderPage()
    await screen.findByText('alias1')
    await userEvent.type(screen.getByPlaceholderText('Search or create…'), 'bad')
    await userEvent.click(screen.getByRole('button', { name: 'Create alias' }))
    expect(await screen.findByText('Failed to create alias.')).toBeInTheDocument()
  })

  it('handles getAliases returning null', async () => {
    api.getAliases.mockResolvedValue(null)
    renderPage()
    expect(await screen.findByText('No aliases for this domain.')).toBeInTheDocument()
  })

  it('handles account with no domains', async () => {
    api.getAccount.mockResolvedValue({
      userName: null,
      fullName: null,
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
    api.getAliases.mockResolvedValue([])
    renderPage()
    expect(await screen.findByText('No aliases for this domain.')).toBeInTheDocument()
  })

  it('clears alias highlight after animation ends (alpha mode)', async () => {
    localStorage.setItem('alias_alpha_mode', 'true')
    api.createAlias.mockResolvedValue(null)
    api.getAliases
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
      await act(async () => { newTile[propsKey].onAnimationEnd() })
    }
    await waitFor(() => expect(container.querySelector('.alias-tile-new')).toBeNull())
  })
})
