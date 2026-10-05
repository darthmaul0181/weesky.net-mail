import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { api } from '../../../api.js'
import { optionsOf, setupUser } from '../../../test-utils'
import { RuleEditorModal } from './RuleEditorModal'
import { fileIntoRule, found, wizardNameInput } from './rulesTestFixtures'
import type { MailFolderNode } from '../../mail/api/mailTypes'
import type { SieveRuleWrite } from './rulesTypes'

vi.mock('../../../api.js', () => ({
  api: { getMailFolders: vi.fn() },
}))

// Mutable so a test can open the editor under a connected account.
const auth = vi.hoisted(() => ({ activeAccountId: 'primary' }))

vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({ activeAccountId: auth.activeAccountId }),
}))

let user: ReturnType<typeof setupUser>
beforeEach(() => {
  vi.clearAllMocks()
  auth.activeAccountId = 'primary'
  vi.mocked(api.getMailFolders).mockResolvedValue([])
  user = setupUser()
})

function renderEditor(rule: SieveRuleWrite | null = fileIntoRule('a', 'r1'), extended = true, onSave = () => {}) {
  return render(<RuleEditorModal rule={rule} extended={extended} onSave={onSave} onClose={() => {}} />)
}

function withCondition(condition: SieveRuleWrite['conditions'][number]): SieveRuleWrite {
  return { ...fileIntoRule('a', 'r1'), conditions: [condition] }
}

const combobox = (name: string) => screen.getByRole('combobox', { name })

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
    await user.click(screen.getByTitle('Help'))
    expect(screen.getAllByRole('button', { name: 'Close' })).toHaveLength(2)

    await user.click(screen.getAllByRole('button', { name: 'Close' })[1]!)
    expect(screen.queryByText('Rule editor — help')).not.toBeInTheDocument()
    expect(onClose).not.toHaveBeenCalled()

    await user.click(screen.getByRole('button', { name: 'Close' }))
    expect(onClose).toHaveBeenCalled()
  })
})

// ── What the extended mode adds ───────────────────────────────

describe('RuleEditorModal extended vs non-extended', () => {
  const EXTENDED_FIELDS = [
    'Body', 'Envelope from', 'Envelope to', 'Recipient +detail', 'Duplicate message',
    'Current date', 'Message date', 'Current weekday', 'Current hour',
  ]

  // Read once per mode on a plain Subject → FileInto rule. Regex is offered in both modes; the date
  // operators only ever follow a date field, so neither mode offers them on Subject.
  it.each([
    ['extended', true, {
      extendedFields: EXTENDED_FIELDS, regex: true, dateOperators: [], keep: true,
      addButtons: 2, markAsFlagged: true, autoCreate: true,
    }],
    ['non-extended', false, {
      extendedFields: [], regex: true, dateOperators: [], keep: false,
      addButtons: 1, markAsFlagged: false, autoCreate: false,
    }],
  ])('offers the %s fields, operators, actions and options', async (_mode, extended, expected) => {
    renderEditor(fileIntoRule('a', 'r1'), extended)

    const fields = await optionsOf(combobox('Field'))
    const operators = await optionsOf(combobox('Operator'))
    const actions = await optionsOf(combobox('Action'))
    expect({
      extendedFields: EXTENDED_FIELDS.filter(field => fields.includes(field)),
      regex: operators.includes('matches (regex)'),
      dateOperators: operators.filter(op => op === 'is before' || op === 'is on or after'),
      keep: actions.includes('Keep in inbox'),
      // Only the conditions' Add is drawn once a non-extended rule holds its one action.
      addButtons: screen.getAllByRole('button', { name: /Add/ }).length,
      markAsFlagged: screen.queryByText('Mark as flagged ⭐') !== null,
      autoCreate: screen.queryByText('Create') !== null,
    }).toEqual(expected)
  })
})

describe('Mark as flagged', () => {
  it('initialises markAsFlagged=true when rule has \\Flagged action', () => {
    renderEditor({
      ...fileIntoRule('a', 'r1'),
      actions: [
        { type: 'SetFlag', argument: '\\Flagged' },
        { type: 'FileInto', argument: 'X' },
      ],
    })

    const checkbox = found(screen.getByText('Mark as flagged ⭐').previousElementSibling?.querySelector('input')
      ?? document.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')[1], 'checkbox')
    expect(checkbox.checked).toBe(true)
  })

  it('includes \\Flagged action on save when markAsFlagged is checked', async () => {
    const onSave = vi.fn()
    renderEditor(fileIntoRule('a', 'r1'), true, onSave)

    const flaggedLabel = screen.getByText('Mark as flagged ⭐')
    const toggle = found(found(flaggedLabel.closest('.rule-wizard-toggle-row'), 'toggle row').querySelector('input'), 'input')
    await user.click(toggle)

    await user.click(screen.getByText('Save changes'))

    const actions: unknown = expect.arrayContaining([
      expect.objectContaining({ type: 'SetFlag', argument: '\\Flagged' }),
      expect.objectContaining({ type: 'FileInto', argument: 'X' }),
    ])
    expect(onSave).toHaveBeenCalledWith(
      expect.objectContaining({ actions })
    )
  })
})

describe('ConditionRow per field', () => {
  it('limits operators to Contains, NotContains and Regex when Body is selected', async () => {
    renderEditor(withCondition({ field: 'Body', operator: 'Contains', value: 'casino' }))

    expect(await optionsOf(combobox('Operator'))).toEqual(['contains', 'not contains', 'matches (regex)'])
  })

  it.each([
    ['Duplicate', withCondition({ field: 'Duplicate', operator: 'Contains', value: '' })],
    ['CurrentWeekday', withCondition({ field: 'CurrentWeekday', operator: 'Contains', value: '1,2,3,4,5' })],
  ])('hides the operator select when %s is active', (_field, rule) => {
    renderEditor(rule)

    expect(screen.queryByRole('combobox', { name: 'Operator' })).not.toBeInTheDocument()
  })

  it('shows Before and OnOrAfter operators when CurrentDate is selected', async () => {
    renderEditor(withCondition({ field: 'CurrentDate', operator: 'Before', value: '2026-12-31' }))
    // When CurrentDate is selected the op select shows date operators (no 'contains')
    const ops = await optionsOf(combobox('Operator'))
    expect(ops).toContain('is before')
    expect(ops).toContain('is on or after')
    expect(ops).toContain('equals')
    expect(ops).not.toContain('contains')
    expect(ops).not.toContain('matches (wildcard)')
  })

  it('shows a date input instead of text when CurrentDate is selected', () => {
    renderEditor(withCondition({ field: 'CurrentDate', operator: 'Before', value: '2026-12-31' }))
    expect(document.querySelector('input[type="date"]')).toBeInTheDocument()
  })

  it('shows weekday dropdown with preset options when CurrentWeekday is selected', async () => {
    renderEditor(withCondition({ field: 'CurrentWeekday', operator: 'Contains', value: '1,2,3,4,5' }))
    const opts = await optionsOf(combobox('Day'))
    expect(opts).toContain('Weekend (Sat–Sun)')
    expect(opts).toContain('Monday')
    expect(opts).toContain('Sunday')
  })

  it('shows number input 0-23 and Before/OnOrAfter operators when CurrentHour is selected', async () => {
    renderEditor(withCondition({ field: 'CurrentHour', operator: 'Before', value: '9' }))
    const hourInput = document.querySelector('input[type="number"][min="0"][max="23"]')
    expect(hourInput).toBeInTheDocument()
    const ops = await optionsOf(combobox('Operator'))
    expect(ops).toContain('is before')
    expect(ops).toContain('is on or after')
    expect(ops).not.toContain('contains')
  })
})

describe('FileInto :create checkbox', () => {
  it('includes autoCreate on save when checkbox is checked', async () => {
    const onSave = vi.fn()
    renderEditor(fileIntoRule('a', 'r1'), true, onSave)

    // clicking the label toggles the checkbox inside it
    await user.click(found(screen.getByText('Create').firstElementChild, 'checkbox'))
    await user.click(screen.getByText('Save changes'))

    const actions: unknown = expect.arrayContaining([
      expect.objectContaining({ type: 'FileInto', autoCreate: true }),
    ])
    expect(onSave).toHaveBeenCalledWith(
      expect.objectContaining({ actions })
    )
  })
})

describe('Wizard step gating', () => {
  function circle(n: number) {
    return found(Array.from(document.querySelectorAll('.rule-wizard-circle')).find(c => c.textContent === String(n)), 'circle')
  }

  it('locks steps 2-4 and disables Create rule on a fresh rule', () => {
    renderEditor(null, false)

    expect(circle(1).className).toContain('rule-wizard-circle--active')
    expect(circle(2).className).toContain('rule-wizard-circle--locked')
    expect(circle(3).className).toContain('rule-wizard-circle--locked')
    expect(circle(4).className).toContain('rule-wizard-circle--locked')
    expect(screen.getByText('Create rule')).toBeDisabled()
  })

  it('unlocks only step 2 once the name is filled', async () => {
    renderEditor(null, false)

    await user.type(wizardNameInput(), 'My rule')

    expect(circle(1).className).not.toContain('rule-wizard-circle--locked')
    expect(circle(2).className).toContain('rule-wizard-circle--active')
    expect(circle(3).className).toContain('rule-wizard-circle--locked')
    expect(circle(4).className).toContain('rule-wizard-circle--locked')
    expect(screen.getByText('Create rule')).toBeDisabled()
  })

  it('unlocks step 3 only once a valid condition exists', async () => {
    renderEditor(null, false)

    await user.type(wizardNameInput(), 'My rule')
    await user.type(screen.getByPlaceholderText('Value'), 'urgent')

    expect(circle(2).className).not.toContain('rule-wizard-circle--locked')
    expect(circle(2).className).not.toContain('rule-wizard-circle--active')
    expect(circle(3).className).toContain('rule-wizard-circle--active')
    expect(circle(4).className).toContain('rule-wizard-circle--locked')
    expect(screen.getByText('Create rule')).toBeDisabled()
  })

  it('enables Create rule only once a valid action exists', async () => {
    renderEditor(null, false)

    await user.type(wizardNameInput(), 'My rule')
    await user.type(screen.getByPlaceholderText('Value'), 'urgent')
    await user.type(screen.getByPlaceholderText('Folder name'), 'Urgent')

    expect(circle(3).className).not.toContain('rule-wizard-circle--locked')
    expect(circle(4).className).not.toContain('rule-wizard-circle--locked')
    expect(screen.getByText('Create rule')).toBeEnabled()
  })

  it('re-locks later steps and disables Save when a value is cleared (edit mode)', async () => {
    renderEditor({
      id: 'a',
      name: 'Existing',
      enabled: true,
      matchAll: false,
      stopAfter: false,
      conditions: [{ field: 'Subject', operator: 'Contains', value: 'urgent' }],
      actions: [{ type: 'FileInto', argument: 'Urgent' }],
    }, false)

    expect(screen.getByText('Save changes')).toBeEnabled()

    await user.clear(screen.getByPlaceholderText('Value'))

    expect(circle(2).className).toContain('rule-wizard-circle--active')
    expect(circle(3).className).toContain('rule-wizard-circle--locked')
    expect(screen.getByText('Save changes')).toBeDisabled()
  })
})
