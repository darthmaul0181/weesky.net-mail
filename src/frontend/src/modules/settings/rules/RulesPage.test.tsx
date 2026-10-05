import { act, render, screen, waitFor, fireEvent } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { Profiler } from 'react'
import type { ReactNode } from 'react'
import {
  QueryClientProvider, defaultScheduler, focusManager, notifyManager, onlineManager,
  type QueryClient,
} from '@tanstack/react-query'
import { api } from '../../../api.js'
import { createTestQueryClient, holdNextCall, settle, setupUser } from '../../../test-utils'
import RulesPage from './RulesPage'
import { RequestTimeoutError } from '../../../lib/withTimeout'
import { checkboxIn, fileIntoRule, found, ruleSet, wizardNameInput } from './rulesTestFixtures'
import type { SieveRuleSet } from './rulesTypes'

vi.mock('../../../api.js', () => ({
  api: {
    getRules: vi.fn(),
    saveRules: vi.fn(),
    deleteRules: vi.fn(),
    checkCompatibility: vi.fn(),
    getMailFolders: vi.fn(),
  },
}))

// Mutable so a test can render the page under a connected account.
const auth = vi.hoisted(() => ({ activeAccountId: 'primary' }))

vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({ activeAccountId: auth.activeAccountId }),
}))

let queryClient: QueryClient
/** Called on every commit of the page, so a test can read the DOM of each committed frame. */
let onCommit: (() => void) | null = null

function WithClient({ children }: { children: ReactNode }) {
  return (
    <QueryClientProvider client={queryClient}>
      <Profiler id="rules" onRender={() => onCommit?.()}>{children}</Profiler>
    </QueryClientProvider>
  )
}

/** Every committed frame's account and whether a dialog stood in it. */
function recordDialogFrames(): string[] {
  const frames: string[] = []
  onCommit = () => { frames.push(`${auth.activeAccountId}:${document.querySelector('.modal') ? 'dialog' : 'none'}`) }
  return frames
}

function renderPage() {
  return render(<RulesPage />, { wrapper: WithClient })
}

let user: ReturnType<typeof setupUser>
beforeEach(() => {
  vi.clearAllMocks()
  user = setupUser()
  queryClient = createTestQueryClient()
  onCommit = null
  auth.activeAccountId = 'primary'
  vi.mocked(api.getMailFolders).mockResolvedValue([])
  vi.mocked(api.saveRules).mockResolvedValue(null)
  vi.mocked(api.deleteRules).mockResolvedValue(null)
})

// ── Slider state derived from provider ────────────────────────

describe('Extended rules slider', () => {
  it.each([
    ['OFF', 'rainloop', false],
    ['ON', 'weesky', true],
  ])('is %s when the active provider is %s', async (_state, provider, checked) => {
    vi.mocked(api.getRules).mockResolvedValue(ruleSet(provider, [fileIntoRule('a', 'r1')]))
    renderPage()

    const toggle = await screen.findByTitle('Extended rules')
    expect(checkboxIn(toggle).checked).toBe(checked)
  })

  it('turning ON switches to weesky without a compatibility check', async () => {
    vi.mocked(api.getRules).mockResolvedValue(ruleSet('rainloop', [fileIntoRule('a', 'r1')]))
    renderPage()

    const toggle = await screen.findByTitle('Extended rules')
    fireEvent.click(checkboxIn(toggle))

    await waitFor(() =>
      expect(api.saveRules).toHaveBeenCalledWith(expect.any(Array), 'weesky', null, { accountId: 'primary' }))
    expect(api.checkCompatibility).not.toHaveBeenCalled()
  })

  it('turning OFF with compatible rules switches straight to rainloop', async () => {
    vi.mocked(api.getRules).mockResolvedValue(ruleSet('weesky', [fileIntoRule('a', 'r1')]))
    vi.mocked(api.checkCompatibility).mockResolvedValue({ compatible: true, incompatible: [] })
    renderPage()

    const toggle = await screen.findByTitle('Extended rules')
    fireEvent.click(checkboxIn(toggle))

    await waitFor(() =>
      expect(api.checkCompatibility).toHaveBeenCalledWith('rainloop', expect.any(Array), { accountId: 'primary' }))
    await waitFor(() =>
      expect(api.saveRules).toHaveBeenCalledWith(expect.any(Array), 'rainloop', null, { accountId: 'primary' }))
  })

  it('turning OFF with incompatible rules shows the conversion modal and drops them on confirm', async () => {
    const rules = [fileIntoRule('keep-me', 'Keeper'), fileIntoRule('lose-me', 'Loser')]
    vi.mocked(api.getRules).mockResolvedValue(ruleSet('weesky', rules))
    vi.mocked(api.checkCompatibility).mockResolvedValue({
      compatible: false,
      incompatible: [{ id: 'lose-me', name: 'Loser', reason: 'uses extended flags' }],
    })
    renderPage()

    const toggle = await screen.findByTitle('Extended rules')
    fireEvent.click(checkboxIn(toggle))

    expect(await screen.findByText('Turn off extended rules?')).toBeInTheDocument()
    expect(screen.getByText('uses extended flags')).toBeInTheDocument()

    fireEvent.click(screen.getByText('Delete & switch'))

    await waitFor(() =>
      expect(api.saveRules).toHaveBeenCalledWith(
        [expect.objectContaining({ id: 'keep-me' })], 'rainloop', null, { accountId: 'primary' }))
  })

  it('cancelling the conversion modal keeps the provider unchanged', async () => {
    vi.mocked(api.getRules).mockResolvedValue(ruleSet('weesky', [fileIntoRule('a', 'r1')]))
    vi.mocked(api.checkCompatibility).mockResolvedValue({
      compatible: false,
      incompatible: [{ id: 'a', name: 'r1', reason: 'nope' }],
    })
    renderPage()

    const toggle = await screen.findByTitle('Extended rules')
    fireEvent.click(checkboxIn(toggle))

    fireEvent.click(await screen.findByRole('button', { name: 'Close' }))

    await waitFor(() =>
      expect(screen.queryByText('Turn off extended rules?')).not.toBeInTheDocument())
    expect(api.saveRules).not.toHaveBeenCalled()
  })
})

// ── RulesPage — initial load ──────────────────────────────────

describe('RulesPage — initial load', () => {
  it('announces the load while the rules are on their way', () => {
    vi.mocked(api.getRules).mockReturnValue(new Promise(() => {}))
    renderPage()
    expect(screen.getByRole('status', { name: 'Loading' })).toBeInTheDocument()
  })

  it('words a timed-out load in the reader’s language, not in the error’s', async () => {
    vi.mocked(api.getRules).mockRejectedValue(new RequestTimeoutError())
    renderPage()
    await screen.findByText('The server is not responding. Try again in a moment.')
  })

  it('shows empty state when rules list is empty', async () => {
    vi.mocked(api.getRules).mockResolvedValue(ruleSet('weesky', []))
    renderPage()
    await screen.findByText(/No rules yet/)
  })

  // The script is the active mailbox's: without the id every read and write would land on the
  // primary's ManageSieve target.
  it('names the active account on the read and on the save', async () => {
    auth.activeAccountId = 'linked-1'
    vi.mocked(api.getRules).mockResolvedValue(ruleSet('weesky', [fileIntoRule('a', 'Existing Rule')]))
    renderPage()
    await screen.findByText('Existing Rule')

    const signal: unknown = expect.any(AbortSignal)
    expect(api.getRules).toHaveBeenCalledWith({ accountId: 'linked-1', signal })
    fireEvent.click(checkboxIn(screen.getByTitle('Disable')))
    await waitFor(() => expect(api.saveRules).toHaveBeenCalledWith(
      expect.any(Array), 'weesky', undefined, { accountId: 'linked-1' }))
  })

  // One Sieve script per mailbox: rules left on screen from another account are one toggle away
  // from being PUT over this account's script, destroying filters nobody asked to touch.
  it('drops the previous account’s rules when the new account’s load fails', async () => {
    vi.mocked(api.getRules).mockResolvedValueOnce(ruleSet('weesky', [fileIntoRule('a', 'Primary Rule')]))
    const { rerender } = renderPage()
    await screen.findByText('Primary Rule')

    auth.activeAccountId = 'linked-1'
    vi.mocked(api.getRules).mockRejectedValueOnce(new Error('timeout'))
    rerender(<RulesPage />)

    await screen.findByText(/Failed to load rules|timeout/)
    expect(screen.queryByText('Primary Rule')).not.toBeInTheDocument()
  })

  // Between the switch and the new account's answer, nothing on screen is the new account's: the
  // previous set must neither show nor be writable, and a dialog left open must not write it.
  it('shows none of the previous account’s rules while the new set loads', async () => {
    vi.mocked(api.getRules).mockResolvedValueOnce(ruleSet('weesky', [fileIntoRule('a', 'Primary Rule')]))
    const { rerender } = renderPage()
    await screen.findByText('Primary Rule')

    const linked = holdNextCall(vi.mocked(api.getRules))
    auth.activeAccountId = 'linked-1'
    rerender(<RulesPage />)

    expect(screen.queryByText('Primary Rule')).not.toBeInTheDocument()
    expect(screen.getByRole('status', { name: 'Loading' })).toBeInTheDocument()
    await act(async () => { linked.resolve(ruleSet('weesky', [])) })
  })

  // One read per visit, as the effect it replaced: a failed load leaves no data, which TanStack
  // counts as stale whatever the staleTime, so a focus or a reconnect must not read it again.
  it('reads once per visit: a failed load is not retried on focus or reconnect', async () => {
    vi.mocked(api.getRules).mockRejectedValue(new Error('timeout'))
    renderPage()
    await screen.findByText('timeout')

    try {
      await act(async () => {
        focusManager.setFocused(false)
        focusManager.setFocused(true)
        onlineManager.setOnline(false)
        onlineManager.setOnline(true)
      })
      await settle()
    } finally {
      focusManager.setFocused(undefined)
    }

    expect(screen.getAllByText('timeout')).toHaveLength(1)
    expect(api.getRules).toHaveBeenCalledTimes(1)
  })

  // A load abandoned by the switch answers for the account left behind: it says nothing here.
  it('keeps a load that fails after the switch from toasting under the new account', async () => {
    const primary = holdNextCall(vi.mocked(api.getRules))
    const { rerender } = renderPage()
    vi.mocked(api.getRules).mockResolvedValueOnce(ruleSet('weesky', [fileIntoRule('b', 'Linked Rule')]))
    auth.activeAccountId = 'linked-1'
    rerender(<RulesPage />)
    await screen.findByText('Linked Rule')

    await act(async () => { primary.fail() })
    await settle()

    expect(screen.queryByText('Server error')).not.toBeInTheDocument()
  })

  // A write's outcome and its busy state belong to the account it was made for: neither its
  // spinner, its disabled switch nor its toast reaches the account that replaced it.
  it('keeps a save in flight at the switch from busying or toasting the new account', async () => {
    vi.mocked(api.getRules)
      .mockResolvedValueOnce(ruleSet('weesky', [fileIntoRule('a', 'Primary Rule')]))
      .mockResolvedValueOnce(ruleSet('weesky', [fileIntoRule('b', 'Linked Rule')]))
    const save = holdNextCall(vi.mocked(api.saveRules))
    const { rerender } = renderPage()
    fireEvent.click(checkboxIn(await screen.findByTitle('Disable')))

    auth.activeAccountId = 'linked-1'
    rerender(<RulesPage />)
    await screen.findByText('Linked Rule')

    expect(checkboxIn(screen.getByTitle('Extended rules'))).toBeEnabled()
    expect(document.querySelector('.rules-count .spinner')).not.toBeInTheDocument()
    await act(async () => { save.resolve(null) })
    await settle()
    expect(screen.queryByText('Rules saved')).not.toBeInTheDocument()
  })

  // Each account keeps its own writes in flight: coming back to one whose save is still out finds
  // it busy, and another account's save landing meanwhile does not lower that.
  it('keeps an account busy across a round trip while the other account also writes', async () => {
    vi.mocked(api.getRules)
      .mockResolvedValueOnce(ruleSet('weesky', [fileIntoRule('a', 'Primary Rule')]))
      .mockResolvedValueOnce(ruleSet('weesky', [fileIntoRule('b', 'Linked Rule')]))
      .mockResolvedValueOnce(ruleSet('weesky', [fileIntoRule('a', 'Primary Rule')]))
    const saveA = holdNextCall(vi.mocked(api.saveRules))
    const { rerender } = renderPage()
    fireEvent.click(checkboxIn(await screen.findByTitle('Disable')))

    auth.activeAccountId = 'linked-1'
    rerender(<RulesPage />)
    const saveB = holdNextCall(vi.mocked(api.saveRules))
    fireEvent.click(checkboxIn(await screen.findByTitle('Disable')))
    auth.activeAccountId = 'primary'
    rerender(<RulesPage />)
    await screen.findByText('Primary Rule')
    const busy = () => document.querySelector('.rules-count .spinner') !== null
      && checkboxIn(screen.getByTitle('Extended rules')).disabled

    expect(busy()).toBe(true)
    await act(async () => { saveB.resolve(null) })
    await settle()
    expect(busy()).toBe(true)
    await act(async () => { saveA.resolve(null) })
    await settle()
    expect(document.querySelector('.rules-count .spinner')).not.toBeInTheDocument()
    expect(checkboxIn(screen.getByTitle('Extended rules'))).toBeEnabled()
  })

  it('keeps a failed save in flight at the switch from toasting under the new account', async () => {
    vi.mocked(api.getRules)
      .mockResolvedValueOnce(ruleSet('weesky', [fileIntoRule('a', 'Primary Rule')]))
      .mockResolvedValueOnce(ruleSet('weesky', [fileIntoRule('b', 'Linked Rule')]))
    const save = holdNextCall(vi.mocked(api.saveRules))
    const { rerender } = renderPage()
    fireEvent.click(checkboxIn(await screen.findByTitle('Disable')))

    auth.activeAccountId = 'linked-1'
    rerender(<RulesPage />)
    await screen.findByText('Linked Rule')
    await act(async () => { save.fail() })
    await settle()

    expect(screen.queryByText('Server error')).not.toBeInTheDocument()
  })

  // No set on screen is no set to write: the page says the load failed rather than drawing an
  // empty list whose every write would be refused. The toast is the one announcement.
  it('says the rules failed to load, with no write in reach, when they never arrived', async () => {
    vi.mocked(api.getRules).mockRejectedValueOnce(new Error('timeout'))
    renderPage()
    await screen.findByText('timeout')

    const note = await screen.findByText('Could not load the rules.')
    expect(note).not.toHaveAttribute('role')
    expect(note).not.toHaveAttribute('aria-live')
    expect(screen.queryByRole('button', { name: /New rule/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('checkbox', { name: 'Extended rules' })).not.toBeInTheDocument()
    expect(screen.queryByText(/No rules yet/)).not.toBeInTheDocument()
    expect(screen.queryByText(/0 rules/)).not.toBeInTheDocument()
    expect(api.saveRules).not.toHaveBeenCalled()
  })

  // Every dialog was opened for the previous account's rules: none of them stands in any frame
  // committed under the new one.
  it.each([
    ['the editor', () => user.click(screen.getByTitle('Edit'))],
    ['a rule’s delete confirmation', () => user.click(screen.getByTitle('Delete'))],
  ])('closes %s with the account it was opened for', async (_, open) => {
    vi.mocked(api.getRules)
      .mockResolvedValueOnce(ruleSet('weesky', [fileIntoRule('a', 'Primary Rule')]))
      .mockResolvedValueOnce(ruleSet('weesky', [fileIntoRule('b', 'Linked Rule')]))
    const { rerender } = renderPage()
    await screen.findByText('Primary Rule')
    await open()
    expect(document.querySelector('.modal')).toBeInTheDocument()

    const frames = recordDialogFrames()
    auth.activeAccountId = 'linked-1'
    rerender(<RulesPage />)
    await screen.findByText('Linked Rule')

    expect(frames.length).toBeGreaterThan(0)
    expect(frames).not.toContain('linked-1:dialog')
    expect(api.saveRules).not.toHaveBeenCalled()
  })

  it('closes the script delete confirmation with the account it was opened for', async () => {
    vi.mocked(api.getRules)
      .mockResolvedValueOnce({ kind: 'Advanced', providerId: 'weesky', scriptName: 'custom', rules: [], rawScript: '' })
      .mockResolvedValueOnce(ruleSet('weesky', [fileIntoRule('b', 'Linked Rule')]))
    const { rerender } = renderPage()
    await user.click(await screen.findByText('Delete script'))
    expect(screen.getByText('Confirm deletion')).toBeInTheDocument()

    const frames = recordDialogFrames()
    auth.activeAccountId = 'linked-1'
    rerender(<RulesPage />)
    await screen.findByText('Linked Rule')

    expect(frames.length).toBeGreaterThan(0)
    expect(frames).not.toContain('linked-1:dialog')
    expect(api.deleteRules).not.toHaveBeenCalled()
  })

  // Two loads in flight can land out of order: the previous account's slow answer is its own and
  // never paints under the account that replaced it.
  it('never paints the previous account’s late answer under the new one', async () => {
    let releasePrimary: (value: SieveRuleSet) => void = () => {}
    vi.mocked(api.getRules)
      .mockImplementationOnce(() => new Promise(resolve => { releasePrimary = resolve }))
      .mockResolvedValueOnce(ruleSet('weesky', [fileIntoRule('b', 'Linked Rule')]))

    const { rerender } = renderPage()
    auth.activeAccountId = 'linked-1'
    rerender(<RulesPage />)
    await screen.findByText('Linked Rule')

    await act(async () => { releasePrimary(ruleSet('weesky', [fileIntoRule('a', 'Primary Rule')])) })
    await settle()

    expect(screen.queryByText('Primary Rule')).not.toBeInTheDocument()
    expect(screen.getByText('Linked Rule')).toBeInTheDocument()
  })

  // A save that returns after the switch belongs to the account it was made for, and its rules
  // never land on the screen of the account that replaced it.
  it('keeps a save that returns after the switch off the new account’s screen', async () => {
    vi.mocked(api.getRules)
      .mockResolvedValueOnce(ruleSet('weesky', [fileIntoRule('a', 'Primary Rule')]))
      .mockResolvedValueOnce(ruleSet('weesky', [fileIntoRule('b', 'Linked Rule')]))
    const save = holdNextCall(vi.mocked(api.saveRules))
    const { rerender } = renderPage()
    fireEvent.click(checkboxIn(await screen.findByTitle('Disable')))

    auth.activeAccountId = 'linked-1'
    rerender(<RulesPage />)
    await screen.findByText('Linked Rule')
    await act(async () => { save.resolve(null) })
    await settle()

    expect(api.saveRules).toHaveBeenCalledWith(expect.any(Array), 'weesky', undefined, { accountId: 'primary' })
    expect(screen.queryByText('Primary Rule')).not.toBeInTheDocument()
    expect(screen.getByText('Linked Rule')).toBeInTheDocument()
  })

  // The conversion dialog lists the previous account's rules: confirming it under the new one
  // would convert the new account's rules to Rainloop without their compatibility ever checked.
  it('closes the conversion dialog with the account it was opened for', async () => {
    vi.mocked(api.getRules)
      .mockResolvedValueOnce(ruleSet('weesky', [fileIntoRule('a', 'Primary Rule')]))
      .mockResolvedValueOnce(ruleSet('weesky', [fileIntoRule('b', 'Linked Rule')]))
    vi.mocked(api.checkCompatibility).mockResolvedValue({
      compatible: false, incompatible: [{ id: 'a', name: 'Primary Rule', reason: 'nope' }],
    })
    const { rerender } = renderPage()
    fireEvent.click(checkboxIn(await screen.findByTitle('Extended rules')))
    await screen.findByText('Turn off extended rules?')

    const frames = recordDialogFrames()
    auth.activeAccountId = 'linked-1'
    rerender(<RulesPage />)
    await screen.findByText('Linked Rule')
    await settle()

    expect(frames.length).toBeGreaterThan(0)
    expect(frames).not.toContain('linked-1:dialog')
    expect(api.saveRules).not.toHaveBeenCalled()
  })

  // A compatibility check still in flight at the switch answers for the previous account: its
  // dialog must not open over the new one.
  it('never opens a conversion dialog from a check that answers after the switch', async () => {
    vi.mocked(api.getRules)
      .mockResolvedValueOnce(ruleSet('weesky', [fileIntoRule('a', 'Primary Rule')]))
      .mockResolvedValueOnce(ruleSet('weesky', [fileIntoRule('b', 'Linked Rule')]))
    const check = holdNextCall(vi.mocked(api.checkCompatibility))
    const { rerender } = renderPage()
    fireEvent.click(checkboxIn(await screen.findByTitle('Extended rules')))

    auth.activeAccountId = 'linked-1'
    rerender(<RulesPage />)
    await screen.findByText('Linked Rule')
    const frames = recordDialogFrames()
    await act(async () => {
      check.resolve({ compatible: false, incompatible: [{ id: 'a', name: 'Primary Rule', reason: 'nope' }] })
    })
    await settle()

    expect(frames).not.toContain('linked-1:dialog')
    expect(screen.queryByText('Turn off extended rules?')).not.toBeInTheDocument()
    expect(api.saveRules).not.toHaveBeenCalled()
  })

  it('shows provider badge for weesky', async () => {
    vi.mocked(api.getRules).mockResolvedValue(ruleSet('weesky', []))
    renderPage()
    await waitFor(() => expect(document.querySelector('.provider-badge')).toBeInTheDocument())
    expect(document.querySelector('.provider-badge')?.textContent).toBe('Weesky')
  })
})

// ── RulesPage — CRUD ──────────────────────────────────────────

describe('RulesPage — CRUD', () => {
  beforeEach(() => {
    vi.mocked(api.getRules).mockResolvedValue(ruleSet('weesky', [fileIntoRule('a', 'Existing Rule')]))
  })

  it('New rule opens an empty editor, and creating calls saveRules with the rule appended', async () => {
    renderPage()
    await screen.findByText('Existing Rule')

    await user.click(screen.getByRole('button', { name: /New rule/i }))
    expect(wizardNameInput().value).toBe('')
    await user.type(wizardNameInput(), 'New Test Rule')
    await user.type(screen.getByPlaceholderText('Value'), 'spam')
    await user.type(screen.getByPlaceholderText('Folder name'), 'Spam')
    await user.click(screen.getByText('Create rule'))

    await waitFor(() => expect(api.saveRules).toHaveBeenCalledWith(
      expect.arrayContaining([
        expect.objectContaining({ name: 'Existing Rule' }),
        expect.objectContaining({ name: 'New Test Rule' }),
      ]),
      'weesky', undefined, { accountId: 'primary' }
    ))
  })

  it('click Edit opens editor pre-filled with rule name', async () => {
    renderPage()
    await screen.findByText('Existing Rule')

    await user.click(screen.getByTitle('Edit'))
    expect(wizardNameInput().value).toBe('Existing Rule')
  })

  it('edit and save calls saveRules with updated rule', async () => {
    renderPage()
    await screen.findByText('Existing Rule')

    await user.click(screen.getByTitle('Edit'))
    const nameInput = wizardNameInput()
    await user.clear(nameInput)
    await user.type(nameInput, 'Renamed Rule')
    await user.click(screen.getByText('Save changes'))

    await waitFor(() => expect(api.saveRules).toHaveBeenCalledWith(
      [expect.objectContaining({ name: 'Renamed Rule' })],
      'weesky', undefined, { accountId: 'primary' }
    ))
  })

  it('Delete asks for confirmation, then calls saveRules without the deleted rule', async () => {
    renderPage()
    await screen.findByText('Existing Rule')

    await user.click(screen.getByTitle('Delete'))
    expect(screen.getByText('Confirm deletion')).toBeInTheDocument()
    await user.click(screen.getByText('Delete', { selector: 'button' }))

    await waitFor(() => expect(api.saveRules).toHaveBeenCalledWith([], 'weesky', undefined, { accountId: 'primary' }))
  })

  // The cache tells its observers a macrotask late, and in a browser React commits the save's own
  // state first: no frame may drop the spinner while the list still shows the set before the save.
  it('never commits a frame with the spinner gone and the list from before the save', async () => {
    const save = holdNextCall(vi.mocked(api.saveRules))
    renderPage()
    fireEvent.click(checkboxIn(await screen.findByTitle('Disable')))
    const frames: string[] = []
    onCommit = () => {
      const spinner = document.querySelector('.rules-count .spinner') ? 'spinner' : 'idle'
      frames.push(`${spinner}:${document.querySelector('.rule-card-disabled') ? 'saved' : 'before'}`)
    }

    notifyManager.setScheduler(cb => setTimeout(cb, 20))
    try {
      await act(async () => { save.resolve(null) })
      await act(async () => { await new Promise(resolve => setTimeout(resolve, 50)) })
    } finally {
      notifyManager.setScheduler(defaultScheduler)
    }

    expect(frames).not.toContain('idle:before')
    expect(frames[frames.length - 1]).toBe('idle:saved')
  })

  it('toggle enabled calls saveRules with updated enabled flag', async () => {
    renderPage()
    await screen.findByText('Existing Rule')

    fireEvent.click(checkboxIn(screen.getByTitle('Disable')))

    await waitFor(() => expect(api.saveRules).toHaveBeenCalledWith(
      [expect.objectContaining({ name: 'Existing Rule', enabled: false })],
      'weesky', undefined, { accountId: 'primary' }
    ))
  })

  it('save error shows error toast', async () => {
    vi.mocked(api.saveRules).mockRejectedValue(new Error('connection refused'))
    renderPage()
    await screen.findByText('Existing Rule')

    await user.click(screen.getByTitle('Edit'))
    await user.click(screen.getByText('Save changes'))

    await screen.findByText('connection refused')
  })
})

// ── RulesPage — reordering ────────────────────────────────────

describe('RulesPage — reordering', () => {
  beforeEach(() => {
    vi.mocked(api.getRules).mockResolvedValue(ruleSet('weesky', [
      fileIntoRule('a', 'First'),
      fileIntoRule('b', 'Second'),
    ]))
  })

  it.each([
    ['Move up', 'the one above it', 1],
    ['Move down', 'the one below it', 0],
  ])('%s swaps the rule with %s', async (title, _other, card) => {
    renderPage()
    await screen.findByText('Second')

    await user.click(screen.getAllByTitle(title)[card]!)

    await waitFor(() => expect(api.saveRules).toHaveBeenCalledWith(
      [expect.objectContaining({ name: 'Second' }), expect.objectContaining({ name: 'First' })],
      'weesky', undefined, { accountId: 'primary' }
    ))
  })
})

// ── RulesPage — reordering across a concurrent delete ──────────

// The drag is captured by the rule's own id, not its render-time index: a card stays draggable
// while another save (here, a delete) is in flight, and `rules` can shrink under the drag before
// the drop lands. An index captured at drag start would then resolve to whatever rule the shrunk
// array now holds at that position — silently the wrong rule, or past the end.
describe('RulesPage — reordering across a concurrent delete', () => {
  function cardOf(name: string): HTMLElement {
    return found(screen.getByText(name).closest<HTMLElement>('.rule-card'), `${name} card`)
  }

  it('drops nothing and saves nothing when the dragged rule is deleted mid-drag', async () => {
    vi.mocked(api.getRules).mockResolvedValue(ruleSet('weesky', [
      fileIntoRule('a', 'First'), fileIntoRule('b', 'Second'), fileIntoRule('c', 'Third'),
    ]))
    renderPage()
    await screen.findByText('Third')

    // Delete First: the confirm closes at once, the save (held) is still in flight.
    await user.click(screen.getAllByTitle('Delete')[0]!)
    const hold = holdNextCall(vi.mocked(api.saveRules))
    await user.click(screen.getByText('Delete', { selector: 'button' }))

    // First's own card is still on screen — nothing has been patched yet — and still draggable.
    fireEvent.dragStart(cardOf('First'), { dataTransfer: {} })

    hold.resolve(null)
    await waitFor(() => expect(screen.queryByText('First')).toBeNull())

    // Dropped on Third, past where First used to sit — a stale index would land here too.
    fireEvent.dragOver(cardOf('Third'), { dataTransfer: {} })
    fireEvent.drop(cardOf('Third'), { dataTransfer: {} })

    await settle()
    // Exactly the delete's own save — the drop found no rule by First's id and gave up.
    expect(api.saveRules).toHaveBeenCalledTimes(1)
    expect(screen.getAllByText('Rules saved')).toHaveLength(1)
  })

  it('moves the dragged rule by id, not by its stale index, when an earlier rule is deleted', async () => {
    vi.mocked(api.getRules).mockResolvedValue(ruleSet('weesky', [
      fileIntoRule('a', 'First'), fileIntoRule('b', 'Second'),
      fileIntoRule('c', 'Third'), fileIntoRule('d', 'Fourth'),
    ]))
    renderPage()
    await screen.findByText('Fourth')

    await user.click(screen.getAllByTitle('Delete')[0]!)
    const hold = holdNextCall(vi.mocked(api.saveRules))
    await user.click(screen.getByText('Delete', { selector: 'button' }))

    // Dragging Third, captured by id before First's deletion shrinks the list under it.
    fireEvent.dragStart(cardOf('Third'), { dataTransfer: {} })

    hold.resolve(null)
    await waitFor(() => expect(screen.queryByText('First')).toBeNull())

    // Dropped onto Second's new position (index 0): a stale index (Third's original index, 2)
    // would now name Fourth in the shrunk [Second, Third, Fourth] list and move it instead.
    fireEvent.dragOver(cardOf('Second'), { dataTransfer: {} })
    fireEvent.drop(cardOf('Second'), { dataTransfer: {} })

    await waitFor(() => expect(api.saveRules).toHaveBeenCalledTimes(2))
    expect(api.saveRules).toHaveBeenLastCalledWith(
      [
        expect.objectContaining({ name: 'Third' }),
        expect.objectContaining({ name: 'Second' }),
        expect.objectContaining({ name: 'Fourth' }),
      ],
      'weesky', undefined, { accountId: 'primary' }
    )
  })
})

// ── RulesPage — delete all script ────────────────────────────

// ── RulesPage — delete one rule ───────────────────────────────

describe('RulesPage — delete one rule', () => {
  // The confirm closes in the click's own commit while the card leaves only once the save returns,
  // so the card's trash button is still reachable at the close and gone a macrotask later.
  it('hands focus to the page heading when the card leaves after the save returns', async () => {
    vi.mocked(api.getRules).mockResolvedValue(ruleSet('weesky', [fileIntoRule('a', 'r1')]))
    vi.mocked(api.saveRules).mockImplementation(() => new Promise(resolve => setTimeout(() => resolve(null), 30)))
    renderPage()
    await screen.findByText('r1')
    await user.click(screen.getAllByTitle('Delete')[0]!)

    await user.click(screen.getByText('Delete', { selector: 'button' }))

    await waitFor(() => expect(screen.queryByText('Confirm deletion')).toBeNull())
    await waitFor(() => expect(screen.queryByText('r1')).toBeNull())
    expect(screen.getByRole('heading', { name: /Rules/ })).toHaveFocus()
  })
})

describe('RulesPage — delete all script', () => {
  function advancedRuleSet(): SieveRuleSet {
    return { kind: 'Advanced', providerId: 'weesky', scriptName: 'custom', rules: [], rawScript: '' }
  }

  it('confirm calls deleteRules and switches to structured view', async () => {
    vi.mocked(api.getRules).mockResolvedValue(advancedRuleSet())
    renderPage()
    await screen.findByText(/cannot be parsed/)

    await user.click(screen.getByText('Delete script'))
    await user.click(screen.getByText('Delete', { selector: 'button' }))

    await waitFor(() => expect(api.deleteRules).toHaveBeenCalledWith({ accountId: 'primary' }))
    await screen.findByText('Script deleted')
    expect(document.querySelector('.rules-toolbar')).toBeInTheDocument()
  })

  // Confirming replaces the whole Advanced notice — the Delete script button included — with the
  // ordinary rules toolbar, so the opener is gone and the page's name takes the focus.
  it('hands focus to the page heading when the notice goes with the script', async () => {
    vi.mocked(api.getRules).mockResolvedValue(advancedRuleSet())
    renderPage()
    await screen.findByText(/cannot be parsed/)
    await user.click(screen.getByText('Delete script'))

    await user.click(screen.getByText('Delete', { selector: 'button' }))

    await waitFor(() => expect(screen.queryByText('Delete script')).toBeNull())
    expect(screen.getByRole('heading', { name: /Rules/ })).toHaveFocus()
  })

  it('delete error shows error toast', async () => {
    vi.mocked(api.getRules).mockResolvedValue(advancedRuleSet())
    vi.mocked(api.deleteRules).mockRejectedValue(new Error('IMAP connection lost'))
    renderPage()
    await screen.findByText(/cannot be parsed/)

    await user.click(screen.getByText('Delete script'))
    await user.click(screen.getByText('Delete', { selector: 'button' }))

    await screen.findByText('IMAP connection lost')
  })
})
