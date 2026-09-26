import { act, render, screen, waitFor, fireEvent } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { Profiler } from 'react'
import type { ComponentProps, ReactNode } from 'react'
import {
  QueryClientProvider, defaultScheduler, focusManager, notifyManager, onlineManager,
  type QueryClient,
} from '@tanstack/react-query'
import { api } from '../../../api.js'
import { createTestQueryClient, holdNextCall, settle } from '../../../test-utils'
import RulesPage from './RulesPage'
import { RequestTimeoutError } from '../../../lib/withTimeout'
import { RuleEditorModal } from './RuleEditorModal'
import { ConvertConfirmModal } from './ConvertConfirmModal'
import { RuleCard } from './RuleCard'
import { isConditionValid, isActionValid } from './ruleDraft'
import type { MailFolderNode } from '../../mail/api/mailTypes'
import type { SieveCondition, SieveRule, SieveRuleSet, SieveRuleWrite } from './rulesTypes'

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

function fileIntoRule(id: string, name: string): SieveRule {
  return {
    id,
    name,
    enabled: true,
    matchAll: false,
    stopAfter: false,
    conditions: [{ field: 'Subject', operator: 'Contains', value: 'x' }],
    actions: [{ type: 'FileInto', argument: 'X', autoCreate: false }],
  }
}

function ruleSet(providerId: string, rules: SieveRule[]): SieveRuleSet {
  return { kind: 'Structured', providerId, rules, rawScript: '' }
}

/** A lookup the test depends on: a miss fails here, by name, rather than as a TypeError later. */
function found<T>(value: T | null | undefined, what: string): T {
  if (value === null || value === undefined) throw new Error(`${what} not found`)
  return value
}

function wizardNameInput(): HTMLInputElement {
  return found(document.querySelector<HTMLInputElement>('.rule-wizard-input'), 'name input')
}

function checkboxIn(element: Element): HTMLInputElement {
  return found(element.querySelector<HTMLInputElement>('input[type="checkbox"]'), 'checkbox')
}

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

beforeEach(() => {
  vi.clearAllMocks()
  queryClient = createTestQueryClient()
  onCommit = null
  auth.activeAccountId = 'primary'
  vi.mocked(api.getMailFolders).mockResolvedValue([])
  vi.mocked(api.saveRules).mockResolvedValue(null)
  vi.mocked(api.deleteRules).mockResolvedValue(null)
})

// ── Slider state derived from provider ────────────────────────

describe('Extended rules slider', () => {
  it('is OFF when the active provider is rainloop', async () => {
    vi.mocked(api.getRules).mockResolvedValue(ruleSet('rainloop', [fileIntoRule('a', 'r1')]))
    renderPage()

    const toggle = await screen.findByTitle('Extended rules')
    const checkbox = checkboxIn(toggle)
    expect(checkbox.checked).toBe(false)
  })

  it('is ON when the active provider is weesky', async () => {
    vi.mocked(api.getRules).mockResolvedValue(ruleSet('weesky', [fileIntoRule('a', 'r1')]))
    renderPage()

    const toggle = await screen.findByTitle('Extended rules')
    const checkbox = checkboxIn(toggle)
    expect(checkbox.checked).toBe(true)
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

// ── Editor folder picker ───────────────────────────

describe('RuleEditorModal folder picker', () => {
  const tree: MailFolderNode[] = [
    { path: 'Archive', name: 'Archive', selectable: true, subscribed: true, uidValidity: 1, children: [
      { path: 'Archive/2026', name: '2026', selectable: true, subscribed: true, uidValidity: 1, children: [] },
    ] },
    { path: 'Containers', name: 'Containers', selectable: false, subscribed: true, uidValidity: 1, children: [] },
  ]

  // The rule is written to the active mailbox's script: a picker listing another mailbox's
  // folders files mail into a folder that may not exist there, and nothing errors.
  it('offers the active account folders, containers excluded', async () => {
    auth.activeAccountId = 'linked-1'
    vi.mocked(api.getMailFolders).mockResolvedValue(tree)
    render(<RuleEditorModal rule={fileIntoRule('a', 'r1')} onSave={() => {}} onClose={() => {}} />)

    await waitFor(() => expect(api.getMailFolders).toHaveBeenCalledWith({ accountId: 'linked-1' }))
    await waitFor(() => expect(document.querySelector('#rule-editor-folders')).toBeInTheDocument())
    const offered = [...document.querySelectorAll<HTMLOptionElement>('#rule-editor-folders option')].map(o => o.value)
    expect(offered).toEqual(['Archive', 'Archive/2026'])
  })
})

describe('RuleEditorModal help button', () => {
  // A tab stop called "?" is read as "question mark, button": it carries the same name the shared
  // HelpTooltip's own trigger was given.
  it('names itself Help rather than ?', () => {
    render(<RuleEditorModal rule={fileIntoRule('a', 'r1')} onSave={() => {}} onClose={() => {}} />)

    expect(screen.getByRole('button', { name: 'Help' })).toHaveTextContent('?')
  })
})

describe('RuleEditorModal validation error', () => {
  // The submit button gates on the same three checks, so it is disabled rather than clickable
  // with an empty name; the form is submitted directly to reach handleSubmit's own guard.
  it('announces the validation error assertively', () => {
    const { container } = render(
      <RuleEditorModal rule={fileIntoRule('a', '')} onSave={() => {}} onClose={() => {}} />)

    fireEvent.submit(found(container.querySelector('form'), 'form'))

    expect(screen.getByRole('alert')).toHaveTextContent('Name is required')
  })

  // PlusIcon is the same ad hoc-svg-in-this-file shape as RuleCard's icons.
  it('hides its PlusIcon from assistive tech', () => {
    const { container } = render(
      <RuleEditorModal rule={fileIntoRule('a', 'r1')} onSave={() => {}} onClose={() => {}} />)
    const svgs = container.querySelectorAll('svg')

    expect(svgs.length).toBeGreaterThan(0)
    svgs.forEach(svg => {
      expect(svg).toHaveAttribute('aria-hidden', 'true')
      expect(svg).toHaveAttribute('focusable', 'false')
    })
  })
})

describe('RuleEditorModal close buttons', () => {
  it('names the editor ✕ and the help ✕, each closing its own dialog', async () => {
    const onClose = vi.fn()
    render(<RuleEditorModal rule={fileIntoRule('a', 'r1')} onSave={() => {}} onClose={onClose} />)
    await userEvent.click(screen.getByTitle('Help'))
    expect(screen.getAllByRole('button', { name: 'Close' })).toHaveLength(2)

    await userEvent.click(screen.getAllByRole('button', { name: 'Close' })[1]!)
    expect(screen.queryByText('Rule editor — help')).not.toBeInTheDocument()
    expect(onClose).not.toHaveBeenCalled()

    await userEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(onClose).toHaveBeenCalled()
  })
})

// ── Editor gating ─────────────────────────────────────────────

describe('RuleEditorModal action gating', () => {
  it('hides the add-action button when not extended and one action exists', () => {
    render(
      <RuleEditorModal
        rule={fileIntoRule('a', 'r1')}
        extended={false}
        onSave={() => {}}
        onClose={() => {}}
      />)

    const addButtons = screen.getAllByRole('button', { name: /Add/ })
    // Only the "Add" for conditions remains; the actions one is hidden.
    expect(addButtons).toHaveLength(1)
  })

  it('shows the add-action button when extended', () => {
    render(
      <RuleEditorModal
        rule={fileIntoRule('a', 'r1')}
        extended={true}
        onSave={() => {}}
        onClose={() => {}}
      />)

    const addButtons = screen.getAllByRole('button', { name: /Add/ })
    expect(addButtons).toHaveLength(2)
  })
})

// ── Extended action types (Keep) ──────────────────────────────

describe('ActionRow extended types', () => {
  it('shows Keep in inbox option in extended mode', () => {
    render(
      <RuleEditorModal
        rule={fileIntoRule('a', 'r1')}
        extended={true}
        onSave={() => {}}
        onClose={() => {}}
      />)

    const selects = document.querySelectorAll('select')
    const actionSelect = found(Array.from(selects).find(s =>
      Array.from(s.options).some(o => o.value === 'FileInto')), 'select')
    expect(Array.from(actionSelect.options).some(o => o.value === 'Keep')).toBe(true)
  })

  it('hides Keep in inbox option in non-extended mode', () => {
    render(
      <RuleEditorModal
        rule={fileIntoRule('a', 'r1')}
        extended={false}
        onSave={() => {}}
        onClose={() => {}}
      />)

    const selects = document.querySelectorAll('select')
    const actionSelect = found(Array.from(selects).find(s =>
      Array.from(s.options).some(o => o.value === 'FileInto')), 'select')
    expect(Array.from(actionSelect.options).some(o => o.value === 'Keep')).toBe(false)
  })
})

// ── Mark as flagged checkbox ───────────────────────────────────

describe('Mark as flagged', () => {
  it('shows mark-as-flagged checkbox in extended mode', () => {
    render(
      <RuleEditorModal
        rule={fileIntoRule('a', 'r1')}
        extended={true}
        onSave={() => {}}
        onClose={() => {}}
      />)

    expect(screen.getByText('Mark as flagged ⭐')).toBeInTheDocument()
  })

  it('hides mark-as-flagged checkbox in non-extended mode', () => {
    render(
      <RuleEditorModal
        rule={fileIntoRule('a', 'r1')}
        extended={false}
        onSave={() => {}}
        onClose={() => {}}
      />)

    expect(screen.queryByText('Mark as flagged ⭐')).not.toBeInTheDocument()
  })

  it('initialises markAsFlagged=true when rule has \\Flagged action', () => {
    const rule: SieveRuleWrite = {
      ...fileIntoRule('a', 'r1'),
      actions: [
        { type: 'SetFlag', argument: '\\Flagged' },
        { type: 'FileInto', argument: 'X' },
      ],
    }
    render(
      <RuleEditorModal rule={rule} extended={true} onSave={() => {}} onClose={() => {}} />)

    const checkbox = found(screen.getByText('Mark as flagged ⭐').previousElementSibling?.querySelector('input')
      ?? document.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')[1], 'checkbox')
    expect(checkbox.checked).toBe(true)
  })

  it('includes \\Flagged action on save when markAsFlagged is checked', async () => {
    const onSave = vi.fn()
    render(
      <RuleEditorModal
        rule={fileIntoRule('a', 'r1')}
        extended={true}
        onSave={onSave}
        onClose={() => {}}
      />)

    const flaggedLabel = screen.getByText('Mark as flagged ⭐')
    const toggle = found(found(flaggedLabel.closest('.rule-wizard-toggle-row'), 'toggle row').querySelector('input'), 'input')
    await userEvent.click(toggle)

    await userEvent.click(screen.getByText('Save changes'))

    const actions: unknown = expect.arrayContaining([
      expect.objectContaining({ type: 'SetFlag', argument: '\\Flagged' }),
      expect.objectContaining({ type: 'FileInto', argument: 'X' }),
    ])
    expect(onSave).toHaveBeenCalledWith(
      expect.objectContaining({ actions })
    )
  })
})

// ── Body condition (extended only) ────────────────────────────

describe('ConditionRow body field', () => {
  it('shows Body option in extended mode', () => {
    render(
      <RuleEditorModal
        rule={fileIntoRule('a', 'r1')}
        extended={true}
        onSave={() => {}}
        onClose={() => {}}
      />)

    const selects = document.querySelectorAll('select')
    const condFieldSelect = found(Array.from(selects).find(s =>
      Array.from(s.options).some(o => o.value === 'Subject')), 'select')
    expect(Array.from(condFieldSelect.options).some(o => o.value === 'Body')).toBe(true)
  })

  it('hides Body option in non-extended mode', () => {
    render(
      <RuleEditorModal
        rule={fileIntoRule('a', 'r1')}
        extended={false}
        onSave={() => {}}
        onClose={() => {}}
      />)

    const selects = document.querySelectorAll('select')
    const condFieldSelect = found(Array.from(selects).find(s =>
      Array.from(s.options).some(o => o.value === 'Subject')), 'select')
    expect(Array.from(condFieldSelect.options).some(o => o.value === 'Body')).toBe(false)
  })

  it('limits operators to Contains, NotContains and Regex when Body is selected', () => {
    const rule: SieveRuleWrite = {
      ...fileIntoRule('a', 'r1'),
      conditions: [{ field: 'Body', operator: 'Contains', value: 'casino' }],
    }
    render(
      <RuleEditorModal rule={rule} extended={true} onSave={() => {}} onClose={() => {}} />)

    const selects = document.querySelectorAll('select')
    const opSelect = found(Array.from(selects).find(s =>
      Array.from(s.options).some(o => o.value === 'Contains') &&
      !Array.from(s.options).some(o => o.value === 'FileInto')), 'select')
    expect(Array.from(opSelect.options).map(o => o.value)).toEqual(['Contains', 'NotContains', 'Regex'])
  })
})

// ── Envelope / subaddress fields (extended only) ──────────────

describe('ConditionRow envelope and subaddress fields', () => {
  it('shows EnvelopeFrom, EnvelopeTo, RecipientDetail in extended mode', () => {
    render(
      <RuleEditorModal
        rule={fileIntoRule('a', 'r1')}
        extended={true}
        onSave={() => {}}
        onClose={() => {}}
      />)

    const selects = document.querySelectorAll('select')
    const condFieldSelect = found(Array.from(selects).find(s =>
      Array.from(s.options).some(o => o.value === 'Subject')), 'select')
    const values = Array.from(condFieldSelect.options).map(o => o.value)
    expect(values).toContain('EnvelopeFrom')
    expect(values).toContain('EnvelopeTo')
    expect(values).toContain('RecipientDetail')
  })

  it('hides envelope/subaddress fields in non-extended mode', () => {
    render(
      <RuleEditorModal
        rule={fileIntoRule('a', 'r1')}
        extended={false}
        onSave={() => {}}
        onClose={() => {}}
      />)

    const selects = document.querySelectorAll('select')
    const condFieldSelect = found(Array.from(selects).find(s =>
      Array.from(s.options).some(o => o.value === 'Subject')), 'select')
    const values = Array.from(condFieldSelect.options).map(o => o.value)
    expect(values).not.toContain('EnvelopeFrom')
    expect(values).not.toContain('EnvelopeTo')
    expect(values).not.toContain('RecipientDetail')
  })
})

// ── Regex operator (extended only) ────────────────────────────

describe('Regex operator', () => {
  it('shows regex option in extended mode', () => {
    render(
      <RuleEditorModal
        rule={fileIntoRule('a', 'r1')}
        extended={true}
        onSave={() => {}}
        onClose={() => {}}
      />)

    const selects = document.querySelectorAll('select')
    const opSelect = found(Array.from(selects).find(s =>
      Array.from(s.options).some(o => o.value === 'Contains') &&
      !Array.from(s.options).some(o => o.value === 'FileInto')), 'select')
    expect(Array.from(opSelect.options).some(o => o.value === 'Regex')).toBe(true)
  })

  it('shows regex option in non-extended mode', () => {
    render(
      <RuleEditorModal
        rule={fileIntoRule('a', 'r1')}
        extended={false}
        onSave={() => {}}
        onClose={() => {}}
      />)

    const selects = document.querySelectorAll('select')
    const opSelect = found(Array.from(selects).find(s =>
      Array.from(s.options).some(o => o.value === 'Contains') &&
      !Array.from(s.options).some(o => o.value === 'FileInto')), 'select')
    expect(Array.from(opSelect.options).some(o => o.value === 'Regex')).toBe(true)
  })
})

// ── Duplicate condition (extended only) ───────────────────────

describe('Duplicate condition', () => {
  it('shows Duplicate field in extended mode', () => {
    render(
      <RuleEditorModal
        rule={fileIntoRule('a', 'r1')}
        extended={true}
        onSave={() => {}}
        onClose={() => {}}
      />)

    const selects = document.querySelectorAll('select')
    const condFieldSelect = found(Array.from(selects).find(s =>
      Array.from(s.options).some(o => o.value === 'Subject')), 'select')
    expect(Array.from(condFieldSelect.options).some(o => o.value === 'Duplicate')).toBe(true)
  })

  it('hides operator select when Duplicate field is active', () => {
    const rule: SieveRuleWrite = {
      ...fileIntoRule('a', 'r1'),
      conditions: [{ field: 'Duplicate', operator: 'Contains', value: '' }],
    }
    render(
      <RuleEditorModal rule={rule} extended={true} onSave={() => {}} onClose={() => {}} />)

    const selects = document.querySelectorAll('select')
    const hasOpSelect = Array.from(selects).some(s =>
      Array.from(s.options).some(o => o.value === 'Contains') &&
      !Array.from(s.options).some(o => o.value === 'FileInto') &&
      !Array.from(s.options).some(o => o.value === 'Subject'))
    expect(hasOpSelect).toBe(false)
  })
})

// ── :create (mailbox) ─────────────────────────────────────────

describe('FileInto :create checkbox', () => {
  it('shows Create checkbox for FileInto in extended mode', () => {
    render(
      <RuleEditorModal
        rule={fileIntoRule('a', 'r1')}
        extended={true}
        onSave={() => {}}
        onClose={() => {}}
      />)

    expect(screen.getByText('Create')).toBeInTheDocument()
  })

  it('hides Create checkbox in non-extended mode', () => {
    render(
      <RuleEditorModal
        rule={fileIntoRule('a', 'r1')}
        extended={false}
        onSave={() => {}}
        onClose={() => {}}
      />)

    expect(screen.queryByText('Create')).not.toBeInTheDocument()
  })

  it('includes autoCreate on save when checkbox is checked', async () => {
    const onSave = vi.fn()
    render(
      <RuleEditorModal
        rule={fileIntoRule('a', 'r1')}
        extended={true}
        onSave={onSave}
        onClose={() => {}}
      />)

    // clicking the label toggles the checkbox inside it
    await userEvent.click(found(screen.getByText('Create').firstElementChild, 'checkbox'))
    await userEvent.click(screen.getByText('Save changes'))

    const actions: unknown = expect.arrayContaining([
      expect.objectContaining({ type: 'FileInto', autoCreate: true }),
    ])
    expect(onSave).toHaveBeenCalledWith(
      expect.objectContaining({ actions })
    )
  })
})

// ── Date conditions ───────────────────────────────────────────

describe('Date condition fields', () => {
  function getCondFieldSelect() {
    return found(Array.from(document.querySelectorAll('select')).find(s =>
      Array.from(s.options).some(o => o.value === 'Subject')), 'select')
  }
  it('CurrentDate and MessageDate appear in extended mode', () => {
    render(<RuleEditorModal rule={fileIntoRule('a', 'r1')} extended={true} onSave={() => {}} onClose={() => {}} />)
    const opts = Array.from(getCondFieldSelect().options).map(o => o.value)
    expect(opts).toContain('CurrentDate')
    expect(opts).toContain('MessageDate')
  })

  it('CurrentDate and MessageDate are hidden in non-extended mode', () => {
    render(<RuleEditorModal rule={fileIntoRule('a', 'r1')} extended={false} onSave={() => {}} onClose={() => {}} />)
    const opts = Array.from(getCondFieldSelect().options).map(o => o.value)
    expect(opts).not.toContain('CurrentDate')
    expect(opts).not.toContain('MessageDate')
  })

  it('shows Before and OnOrAfter operators when CurrentDate is selected', () => {
    const rule: SieveRuleWrite = {
      ...fileIntoRule('a', 'r1'),
      conditions: [{ field: 'CurrentDate', operator: 'Before', value: '2026-12-31' }],
    }
    render(<RuleEditorModal rule={rule} extended={true} onSave={() => {}} onClose={() => {}} />)
    // When CurrentDate is selected the op select shows date operators (no 'Contains')
    const dateOpSelect = found(Array.from(document.querySelectorAll('select')).find(s =>
      Array.from(s.options).some(o => o.value === 'Before')), 'select')
    const ops = Array.from(dateOpSelect.options).map(o => o.value)
    expect(ops).toContain('Before')
    expect(ops).toContain('OnOrAfter')
    expect(ops).toContain('Equals')
    expect(ops).not.toContain('Contains')
    expect(ops).not.toContain('Matches')
  })

  it('shows a date input instead of text when CurrentDate is selected', () => {
    const rule: SieveRuleWrite = {
      ...fileIntoRule('a', 'r1'),
      conditions: [{ field: 'CurrentDate', operator: 'Before', value: '2026-12-31' }],
    }
    render(<RuleEditorModal rule={rule} extended={true} onSave={() => {}} onClose={() => {}} />)
    expect(document.querySelector('input[type="date"]')).toBeInTheDocument()
  })

  it('Before and OnOrAfter operators are absent in non-extended mode', () => {
    render(<RuleEditorModal rule={fileIntoRule('a', 'r1')} extended={false} onSave={() => {}} onClose={() => {}} />)
    const opSelect = found(Array.from(document.querySelectorAll('select')).find(s =>
      Array.from(s.options).some(o => o.value === 'Contains') &&
      !Array.from(s.options).some(o => o.value === 'FileInto')), 'select')
    const ops = Array.from(opSelect.options).map(o => o.value)
    expect(ops).not.toContain('Before')
    expect(ops).not.toContain('OnOrAfter')
  })
})

// ── Weekday / hour conditions ─────────────────────────────────

describe('CurrentWeekday condition', () => {
  it('appears in extended mode', () => {
    render(<RuleEditorModal rule={fileIntoRule('a', 'r1')} extended={true} onSave={() => {}} onClose={() => {}} />)
    const fieldSelect = found(Array.from(document.querySelectorAll('select')).find(s =>
      Array.from(s.options).some(o => o.value === 'Subject')), 'select')
    expect(Array.from(fieldSelect.options).some(o => o.value === 'CurrentWeekday')).toBe(true)
  })

  it('is absent in non-extended mode', () => {
    render(<RuleEditorModal rule={fileIntoRule('a', 'r1')} extended={false} onSave={() => {}} onClose={() => {}} />)
    const fieldSelect = found(Array.from(document.querySelectorAll('select')).find(s =>
      Array.from(s.options).some(o => o.value === 'Subject')), 'select')
    expect(Array.from(fieldSelect.options).some(o => o.value === 'CurrentWeekday')).toBe(false)
  })

  it('shows weekday dropdown with preset options when selected', () => {
    const rule: SieveRuleWrite = {
      ...fileIntoRule('a', 'r1'),
      conditions: [{ field: 'CurrentWeekday', operator: 'Contains', value: '1,2,3,4,5' }],
    }
    render(<RuleEditorModal rule={rule} extended={true} onSave={() => {}} onClose={() => {}} />)
    const weekdaySelect = found(Array.from(document.querySelectorAll('select')).find(s =>
      Array.from(s.options).some(o => o.value === '1,2,3,4,5')), 'select')
    expect(weekdaySelect).toBeTruthy()
    const opts = Array.from(weekdaySelect.options).map(o => o.value)
    expect(opts).toContain('0,6')
    expect(opts).toContain('1')
    expect(opts).toContain('0')
  })

  it('hides the operator select when CurrentWeekday is active', () => {
    const rule: SieveRuleWrite = {
      ...fileIntoRule('a', 'r1'),
      conditions: [{ field: 'CurrentWeekday', operator: 'Contains', value: '1,2,3,4,5' }],
    }
    render(<RuleEditorModal rule={rule} extended={true} onSave={() => {}} onClose={() => {}} />)
    const hasOpSelect = Array.from(document.querySelectorAll('select')).some(s =>
      Array.from(s.options).some(o => o.value === 'Contains') &&
      !Array.from(s.options).some(o => o.value === 'FileInto') &&
      !Array.from(s.options).some(o => o.value === '1,2,3,4,5'))
    expect(hasOpSelect).toBe(false)
  })
})

describe('CurrentHour condition', () => {
  it('appears in extended mode', () => {
    render(<RuleEditorModal rule={fileIntoRule('a', 'r1')} extended={true} onSave={() => {}} onClose={() => {}} />)
    const fieldSelect = found(Array.from(document.querySelectorAll('select')).find(s =>
      Array.from(s.options).some(o => o.value === 'Subject')), 'select')
    expect(Array.from(fieldSelect.options).some(o => o.value === 'CurrentHour')).toBe(true)
  })

  it('shows number input 0-23 and Before/OnOrAfter operators when selected', () => {
    const rule: SieveRuleWrite = {
      ...fileIntoRule('a', 'r1'),
      conditions: [{ field: 'CurrentHour', operator: 'Before', value: '9' }],
    }
    render(<RuleEditorModal rule={rule} extended={true} onSave={() => {}} onClose={() => {}} />)
    const hourInput = document.querySelector('input[type="number"][min="0"][max="23"]')
    expect(hourInput).toBeInTheDocument()
    const opSelect = found(Array.from(document.querySelectorAll('select')).find(s =>
      Array.from(s.options).some(o => o.value === 'Before')), 'select')
    const ops = Array.from(opSelect.options).map(o => o.value)
    expect(ops).toContain('Before')
    expect(ops).toContain('OnOrAfter')
    expect(ops).not.toContain('Contains')
  })
})

// ── Validity helpers ──────────────────────────────────────────

describe('isConditionValid', () => {
  it('rejects an empty value on a text field', () => {
    expect(isConditionValid({ field: 'Subject', operator: 'Contains', value: '' })).toBe(false)
    expect(isConditionValid({ field: 'Subject', operator: 'Contains', value: '   ' })).toBe(false)
  })
  it('accepts a non-empty value on a text field', () => {
    expect(isConditionValid({ field: 'Subject', operator: 'Contains', value: 'x' })).toBe(true)
  })
  it('requires a header name for the Header field', () => {
    expect(isConditionValid({ field: 'Header', operator: 'Contains', value: 'x', headerName: '' })).toBe(false)
    expect(isConditionValid({ field: 'Header', operator: 'Contains', value: 'x', headerName: 'X-Spam' })).toBe(true)
  })
  it('treats Duplicate as always valid (seconds optional)', () => {
    expect(isConditionValid({ field: 'Duplicate', operator: 'Contains', value: '' })).toBe(true)
  })
})

describe('isActionValid', () => {
  it('requires an argument for FileInto/Redirect/Reject/SetFlag', () => {
    expect(isActionValid({ type: 'FileInto', argument: '' })).toBe(false)
    expect(isActionValid({ type: 'FileInto', argument: 'Inbox' })).toBe(true)
    expect(isActionValid({ type: 'Redirect', argument: '' })).toBe(false)
  })
  it('treats Discard and Keep as always valid', () => {
    expect(isActionValid({ type: 'Discard' })).toBe(true)
    expect(isActionValid({ type: 'Keep' })).toBe(true)
  })
})

// ── Wizard step gating (new rule) ─────────────────────────────

describe('Wizard step gating', () => {
  function circle(n: number) {
    return found(Array.from(document.querySelectorAll('.rule-wizard-circle')).find(c => c.textContent === String(n)), 'circle')
  }

  it('locks steps 2-4 and disables Create rule on a fresh rule', () => {
    render(<RuleEditorModal rule={null} extended={false} onSave={() => {}} onClose={() => {}} />)

    expect(circle(1).className).toContain('rule-wizard-circle--active')
    expect(circle(2).className).toContain('rule-wizard-circle--locked')
    expect(circle(3).className).toContain('rule-wizard-circle--locked')
    expect(circle(4).className).toContain('rule-wizard-circle--locked')
    expect(screen.getByText('Create rule')).toBeDisabled()
  })

  it('unlocks only step 2 once the name is filled', async () => {
    render(<RuleEditorModal rule={null} extended={false} onSave={() => {}} onClose={() => {}} />)

    await userEvent.type(wizardNameInput(), 'My rule')

    expect(circle(1).className).not.toContain('rule-wizard-circle--locked')
    expect(circle(2).className).toContain('rule-wizard-circle--active')
    expect(circle(3).className).toContain('rule-wizard-circle--locked')
    expect(circle(4).className).toContain('rule-wizard-circle--locked')
    expect(screen.getByText('Create rule')).toBeDisabled()
  })

  it('unlocks step 3 only once a valid condition exists', async () => {
    render(<RuleEditorModal rule={null} extended={false} onSave={() => {}} onClose={() => {}} />)

    await userEvent.type(wizardNameInput(), 'My rule')
    await userEvent.type(screen.getByPlaceholderText('Value'), 'urgent')

    expect(circle(2).className).not.toContain('rule-wizard-circle--locked')
    expect(circle(2).className).not.toContain('rule-wizard-circle--active')
    expect(circle(3).className).toContain('rule-wizard-circle--active')
    expect(circle(4).className).toContain('rule-wizard-circle--locked')
    expect(screen.getByText('Create rule')).toBeDisabled()
  })

  it('enables Create rule only once a valid action exists', async () => {
    render(<RuleEditorModal rule={null} extended={false} onSave={() => {}} onClose={() => {}} />)

    await userEvent.type(wizardNameInput(), 'My rule')
    await userEvent.type(screen.getByPlaceholderText('Value'), 'urgent')
    await userEvent.type(screen.getByPlaceholderText('Folder name'), 'Urgent')

    expect(circle(3).className).not.toContain('rule-wizard-circle--locked')
    expect(circle(4).className).not.toContain('rule-wizard-circle--locked')
    expect(screen.getByText('Create rule')).toBeEnabled()
  })

  it('re-locks later steps and disables Save when a value is cleared (edit mode)', async () => {
    const rule: SieveRuleWrite = {
      id: 'a',
      name: 'Existing',
      enabled: true,
      matchAll: false,
      stopAfter: false,
      conditions: [{ field: 'Subject', operator: 'Contains', value: 'urgent' }],
      actions: [{ type: 'FileInto', argument: 'Urgent' }],
    }
    render(<RuleEditorModal rule={rule} extended={false} onSave={() => {}} onClose={() => {}} />)

    expect(screen.getByText('Save changes')).toBeEnabled()

    await userEvent.clear(screen.getByPlaceholderText('Value'))

    expect(circle(2).className).toContain('rule-wizard-circle--active')
    expect(circle(3).className).toContain('rule-wizard-circle--locked')
    expect(screen.getByText('Save changes')).toBeDisabled()
  })
})

// ── RuleCard ──────────────────────────────────────────────────

describe('RuleCard', () => {
  function makeCardProps(overrides: Partial<ComponentProps<typeof RuleCard>> = {}) {
    return {
      rule: fileIntoRule('r1', 'My Rule'),
      onEdit: vi.fn(),
      onDelete: vi.fn(),
      onToggleEnabled: vi.fn(),
      isFirst: false,
      isLast: false,
      onMoveUp: vi.fn(),
      onMoveDown: vi.fn(),
      isDragOver: false,
      onDragStart: vi.fn(),
      onDragOver: vi.fn(),
      onDrop: vi.fn(),
      onDragEnd: vi.fn(),
      ...overrides,
    }
  }

  it('renders rule name', () => {
    render(<RuleCard {...makeCardProps()} />)
    expect(screen.getByText('My Rule')).toBeInTheDocument()
  })

  // The grip, the two reorder arrows and the collapse chevron are ad hoc inline svgs, local to
  // this file — icons.test.tsx's glob over src/icons/ structurally cannot see them.
  it('hides its ad hoc icons (grip, reorder arrows, chevron) from assistive tech', () => {
    const { container } = render(<RuleCard {...makeCardProps()} />)
    const svgs = container.querySelectorAll('svg')

    expect(svgs.length).toBeGreaterThan(0)
    svgs.forEach(svg => {
      expect(svg).toHaveAttribute('aria-hidden', 'true')
      expect(svg).toHaveAttribute('focusable', 'false')
    })
  })

  it('starts collapsed with inline action pill and no body', () => {
    render(<RuleCard {...makeCardProps()} />)
    expect(document.querySelector('.rule-card-inline-actions')).toBeInTheDocument()
    expect(document.querySelector('.rule-card-body')).not.toBeInTheDocument()
  })

  it('expand toggle shows body and hides inline pills', () => {
    render(<RuleCard {...makeCardProps()} />)
    fireEvent.click(screen.getByTitle('Expand'))
    expect(document.querySelector('.rule-card-body')).toBeInTheDocument()
    expect(document.querySelector('.rule-card-inline-actions')).not.toBeInTheDocument()
    fireEvent.click(screen.getByTitle('Collapse'))
    expect(document.querySelector('.rule-card-body')).not.toBeInTheDocument()
  })

  it('isDragOver adds drop-over class', () => {
    const { container } = render(<RuleCard {...makeCardProps({ isDragOver: true })} />)
    expect(container.querySelector('.rule-card-drop-over')).toBeInTheDocument()
  })

  it('disabled rule adds disabled class', () => {
    const rule: SieveRuleWrite = { ...fileIntoRule('r1', 'My Rule'), enabled: false }
    const { container } = render(<RuleCard {...makeCardProps({ rule })} />)
    expect(container.querySelector('.rule-card-disabled')).toBeInTheDocument()
  })

  it('Move up is disabled when isFirst', () => {
    render(<RuleCard {...makeCardProps({ isFirst: true })} />)
    expect(screen.getByTitle('Move up')).toBeDisabled()
  })

  it('Move down is disabled when isLast', () => {
    render(<RuleCard {...makeCardProps({ isLast: true })} />)
    expect(screen.getByTitle('Move down')).toBeDisabled()
  })

  it('calls onEdit when Edit is clicked', () => {
    const onEdit = vi.fn()
    render(<RuleCard {...makeCardProps({ onEdit })} />)
    fireEvent.click(screen.getByTitle('Edit'))
    expect(onEdit).toHaveBeenCalled()
  })

  it('calls onDelete when Delete is clicked', () => {
    const onDelete = vi.fn()
    render(<RuleCard {...makeCardProps({ onDelete })} />)
    fireEvent.click(screen.getByTitle('Delete'))
    expect(onDelete).toHaveBeenCalled()
  })

  // "Disable, checkbox, checked" is the state read twice and the rule never named: the switch is
  // named by what it toggles, and `checked` is what says which way it stands.
  it('names the enable switch by its rule, not by the action', () => {
    render(<RuleCard {...makeCardProps()} />)
    expect(screen.getByRole('checkbox', { name: 'My Rule' })).toBeChecked()
  })

  // The editor asks for a name, but the list is parsed from a Sieve script another client wrote:
  // the same fallback the convert dialog already spells out keeps the control named.
  it('names the switch of a rule the script left unnamed', () => {
    render(<RuleCard {...makeCardProps({ rule: fileIntoRule('r1', '') })} />)
    expect(screen.getByRole('checkbox', { name: '(unnamed rule)' })).toBeInTheDocument()
  })

  it('calls onToggleEnabled with false when enabled rule checkbox is clicked', () => {
    const onToggleEnabled = vi.fn()
    render(<RuleCard {...makeCardProps({ onToggleEnabled })} />)
    fireEvent.click(checkboxIn(screen.getByTitle('Disable')))
    expect(onToggleEnabled).toHaveBeenCalledWith(false)
  })

  it('calls onMoveUp when Move up is clicked', () => {
    const onMoveUp = vi.fn()
    render(<RuleCard {...makeCardProps({ onMoveUp })} />)
    fireEvent.click(screen.getByTitle('Move up'))
    expect(onMoveUp).toHaveBeenCalled()
  })

  it('calls onMoveDown when Move down is clicked', () => {
    const onMoveDown = vi.fn()
    render(<RuleCard {...makeCardProps({ onMoveDown })} />)
    fireEvent.click(screen.getByTitle('Move down'))
    expect(onMoveDown).toHaveBeenCalled()
  })
})

// ── summarize helpers (via RuleCard expand) ────────────────────

describe('summarize helpers', () => {
  function baseProps() {
    return {
      onEdit: vi.fn(), onDelete: vi.fn(), onToggleEnabled: vi.fn(),
      isFirst: false, isLast: false,
      onMoveUp: vi.fn(), onMoveDown: vi.fn(), isDragOver: false,
      onDragStart: vi.fn(), onDragOver: vi.fn(), onDrop: vi.fn(), onDragEnd: vi.fn(),
    }
  }
  function cardWithCondition(cond: SieveCondition): SieveRuleWrite {
    return { id: 'r1', name: 'R', enabled: true, matchAll: false, stopAfter: false,
      conditions: [cond], actions: [{ type: 'Keep', argument: '' }] }
  }
  function cardWithAction(action: SieveRuleWrite['actions'][number]): SieveRuleWrite {
    return { id: 'r1', name: 'R', enabled: true, matchAll: false, stopAfter: false,
      conditions: [{ field: 'Subject', operator: 'Contains', value: 'x' }],
      actions: [action] }
  }
  function renderExpanded(rule: SieveRuleWrite) {
    render(<RuleCard rule={rule} {...baseProps()} />)
    fireEvent.click(screen.getByTitle('Expand'))
  }

  it('Subject Contains', () => {
    renderExpanded(cardWithCondition({ field: 'Subject', operator: 'Contains', value: 'urgent' }))
    expect(screen.getByText('Subject contains "urgent"')).toBeInTheDocument()
  })

  it('Duplicate without value', () => {
    renderExpanded(cardWithCondition({ field: 'Duplicate', operator: 'Contains', value: '' }))
    expect(screen.getByText('Duplicate message')).toBeInTheDocument()
  })

  it('Duplicate with seconds window', () => {
    renderExpanded(cardWithCondition({ field: 'Duplicate', operator: 'Contains', value: '60' }))
    expect(screen.getByText('Duplicate (within 60s)')).toBeInTheDocument()
  })

  it('CurrentDate', () => {
    renderExpanded(cardWithCondition({ field: 'CurrentDate', operator: 'Before', value: '2024-01-01' }))
    expect(screen.getByText('Current date is before 2024-01-01')).toBeInTheDocument()
  })

  it('MessageDate', () => {
    renderExpanded(cardWithCondition({ field: 'MessageDate', operator: 'OnOrAfter', value: '2024-06-01' }))
    expect(screen.getByText('Message date is on or after 2024-06-01')).toBeInTheDocument()
  })

  it('CurrentWeekday', () => {
    renderExpanded(cardWithCondition({ field: 'CurrentWeekday', operator: 'Contains', value: '1,2,3,4,5' }))
    expect(screen.getByText('Weekday is Weekday (Mon–Fri)')).toBeInTheDocument()
  })

  it('CurrentHour', () => {
    renderExpanded(cardWithCondition({ field: 'CurrentHour', operator: 'Before', value: '9' }))
    expect(screen.getByText('Hour is before 9:00')).toBeInTheDocument()
  })

  it('Custom header uses headerName', () => {
    renderExpanded(cardWithCondition({ field: 'Header', operator: 'Contains', value: 'spam', headerName: 'X-Spam' }))
    expect(screen.getByText('X-Spam contains "spam"')).toBeInTheDocument()
  })

  it('Redirect action', () => {
    renderExpanded(cardWithAction({ type: 'Redirect', argument: 'a@b.com' }))
    expect(screen.getByText('⇥ a@b.com')).toBeInTheDocument()
  })

  it('SetFlag Seen', () => {
    renderExpanded(cardWithAction({ type: 'SetFlag', argument: '\\Seen' }))
    expect(screen.getByText('Mark as read')).toBeInTheDocument()
  })

  it('SetFlag Flagged', () => {
    renderExpanded(cardWithAction({ type: 'SetFlag', argument: '\\Flagged' }))
    expect(screen.getByText('⭐ Flagged')).toBeInTheDocument()
  })

  it('Keep action', () => {
    renderExpanded(cardWithAction({ type: 'Keep', argument: '' }))
    expect(screen.getByText('Keep in inbox')).toBeInTheDocument()
  })

  it('Discard action', () => {
    renderExpanded(cardWithAction({ type: 'Discard', argument: '' }))
    expect(screen.getByText('Discard')).toBeInTheDocument()
  })

  it('FileInto with autoCreate shows ✚ in expanded view', () => {
    renderExpanded(cardWithAction({ type: 'FileInto', argument: 'Archive', autoCreate: true }))
    expect(screen.getByText('→ Archive ✚')).toBeInTheDocument()
  })

  it('FileInto collapsed pill shows → prefix', () => {
    render(<RuleCard rule={cardWithAction({ type: 'FileInto', argument: 'Inbox' })} {...baseProps()} />)
    expect(screen.getByText('→ Inbox')).toBeInTheDocument()
  })
})

// ── RulesPage — initial load ──────────────────────────────────

describe('RulesPage — initial load', () => {
  it('shows spinner while loading', () => {
    vi.mocked(api.getRules).mockReturnValue(new Promise(() => {}))
    renderPage()
    expect(document.querySelector('.loading-center')).toBeInTheDocument()
  })

  it('shows error toast when getRules rejects', async () => {
    vi.mocked(api.getRules).mockRejectedValue(new Error('network failure'))
    renderPage()
    await screen.findByText('network failure')
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

  it('renders rule cards when rules exist', async () => {
    vi.mocked(api.getRules).mockResolvedValue(ruleSet('weesky', [fileIntoRule('a', 'My Test Rule')]))
    renderPage()
    await screen.findByText('My Test Rule')
  })

  it('shows Advanced notice for Advanced kind', async () => {
    vi.mocked(api.getRules).mockResolvedValue({ kind: 'Advanced', providerId: 'weesky', scriptName: 'custom', rules: [], rawScript: '' })
    renderPage()
    await screen.findByText(/cannot be parsed/)
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
    expect(document.querySelector('.loading-center')).toBeInTheDocument()
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
    ['the editor', () => userEvent.click(screen.getByTitle('Edit'))],
    ['a rule’s delete confirmation', () => userEvent.click(screen.getByTitle('Delete'))],
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
    await userEvent.click(await screen.findByText('Delete script'))
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

  it('click New rule opens editor with empty name', async () => {
    renderPage()
    await screen.findByText('Existing Rule')
    await userEvent.click(screen.getByRole('button', { name: /New rule/i }))
    const nameInput = wizardNameInput()
    expect(nameInput).toBeInTheDocument()
    expect(nameInput.value).toBe('')
  })

  it('create new rule calls saveRules with the new rule appended', async () => {
    renderPage()
    await screen.findByText('Existing Rule')

    await userEvent.click(screen.getByRole('button', { name: /New rule/i }))
    await userEvent.type(wizardNameInput(), 'New Test Rule')
    await userEvent.type(screen.getByPlaceholderText('Value'), 'spam')
    await userEvent.type(screen.getByPlaceholderText('Folder name'), 'Spam')
    await userEvent.click(screen.getByText('Create rule'))

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

    await userEvent.click(screen.getByTitle('Edit'))
    expect(wizardNameInput().value).toBe('Existing Rule')
  })

  it('edit and save calls saveRules with updated rule', async () => {
    renderPage()
    await screen.findByText('Existing Rule')

    await userEvent.click(screen.getByTitle('Edit'))
    const nameInput = wizardNameInput()
    await userEvent.clear(nameInput)
    await userEvent.type(nameInput, 'Renamed Rule')
    await userEvent.click(screen.getByText('Save changes'))

    await waitFor(() => expect(api.saveRules).toHaveBeenCalledWith(
      [expect.objectContaining({ name: 'Renamed Rule' })],
      'weesky', undefined, { accountId: 'primary' }
    ))
  })

  it('click Delete opens confirm modal', async () => {
    renderPage()
    await screen.findByText('Existing Rule')

    await userEvent.click(screen.getByTitle('Delete'))
    expect(screen.getByText('Confirm deletion')).toBeInTheDocument()
  })

  it('confirm delete calls saveRules without the deleted rule', async () => {
    renderPage()
    await screen.findByText('Existing Rule')

    await userEvent.click(screen.getByTitle('Delete'))
    await userEvent.click(screen.getByText('Delete', { selector: 'button' }))

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

    await userEvent.click(screen.getByTitle('Edit'))
    await userEvent.click(screen.getByText('Save changes'))

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

  it('Move up swaps the rule with the one above it', async () => {
    renderPage()
    await screen.findByText('Second')

    const moveUpBtns = screen.getAllByTitle('Move up')
    await userEvent.click(moveUpBtns[1]!)

    await waitFor(() => expect(api.saveRules).toHaveBeenCalledWith(
      [expect.objectContaining({ name: 'Second' }), expect.objectContaining({ name: 'First' })],
      'weesky', undefined, { accountId: 'primary' }
    ))
  })

  it('Move down swaps the rule with the one below it', async () => {
    renderPage()
    await screen.findByText('First')

    const moveDownBtns = screen.getAllByTitle('Move down')
    await userEvent.click(moveDownBtns[0]!)

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
    await userEvent.click(screen.getAllByTitle('Delete')[0]!)
    const hold = holdNextCall(vi.mocked(api.saveRules))
    await userEvent.click(screen.getByText('Delete', { selector: 'button' }))

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

    await userEvent.click(screen.getAllByTitle('Delete')[0]!)
    const hold = holdNextCall(vi.mocked(api.saveRules))
    await userEvent.click(screen.getByText('Delete', { selector: 'button' }))

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
    await userEvent.click(screen.getAllByTitle('Delete')[0]!)

    await userEvent.click(screen.getByText('Delete', { selector: 'button' }))

    await waitFor(() => expect(screen.queryByText('Confirm deletion')).toBeNull())
    await waitFor(() => expect(screen.queryByText('r1')).toBeNull())
    expect(screen.getByRole('heading', { name: /Rules/ })).toHaveFocus()
  })
})

describe('RulesPage — delete all script', () => {
  function advancedRuleSet(): SieveRuleSet {
    return { kind: 'Advanced', providerId: 'weesky', scriptName: 'custom', rules: [], rawScript: '' }
  }

  it('Delete script button opens confirm modal', async () => {
    vi.mocked(api.getRules).mockResolvedValue(advancedRuleSet())
    renderPage()
    await screen.findByText(/cannot be parsed/)

    await userEvent.click(screen.getByText('Delete script'))
    expect(screen.getByText('Confirm deletion')).toBeInTheDocument()
  })

  it('confirm calls deleteRules and switches to structured view', async () => {
    vi.mocked(api.getRules).mockResolvedValue(advancedRuleSet())
    renderPage()
    await screen.findByText(/cannot be parsed/)

    await userEvent.click(screen.getByText('Delete script'))
    await userEvent.click(screen.getByText('Delete', { selector: 'button' }))

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
    await userEvent.click(screen.getByText('Delete script'))

    await userEvent.click(screen.getByText('Delete', { selector: 'button' }))

    await waitFor(() => expect(screen.queryByText('Delete script')).toBeNull())
    expect(screen.getByRole('heading', { name: /Rules/ })).toHaveFocus()
  })

  it('delete error shows error toast', async () => {
    vi.mocked(api.getRules).mockResolvedValue(advancedRuleSet())
    vi.mocked(api.deleteRules).mockRejectedValue(new Error('IMAP connection lost'))
    renderPage()
    await screen.findByText(/cannot be parsed/)

    await userEvent.click(screen.getByText('Delete script'))
    await userEvent.click(screen.getByText('Delete', { selector: 'button' }))

    await screen.findByText('IMAP connection lost')
  })
})

// ── ConvertConfirmModal ───────────────────────────────────────

describe('ConvertConfirmModal', () => {
  it('lists every incompatible rule with its reason', () => {
    const incompatible = [
      { id: '1', name: 'A', reason: 'reason A' },
      { id: '2', name: 'B', reason: 'reason B' },
    ]
    render(<ConvertConfirmModal incompatible={incompatible} onConfirm={() => {}} onClose={() => {}} />)

    expect(screen.getByText('A')).toBeInTheDocument()
    expect(screen.getByText('reason A')).toBeInTheDocument()
    expect(screen.getByText('B')).toBeInTheDocument()
    expect(screen.getByText('reason B')).toBeInTheDocument()
  })

  it('calls onConfirm when the confirm button is clicked', async () => {
    const onConfirm = vi.fn()
    render(<ConvertConfirmModal incompatible={[{ id: '1', name: 'A', reason: 'r' }]}
      onConfirm={onConfirm} onClose={() => {}} />)

    await userEvent.click(screen.getByText('Delete & switch'))
    expect(onConfirm).toHaveBeenCalled()
  })

  it('closes on its named ✕', async () => {
    const onClose = vi.fn()
    render(<ConvertConfirmModal incompatible={[]} onConfirm={() => {}} onClose={onClose} />)

    await userEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(onClose).toHaveBeenCalled()
  })
})
