import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { api } from '../../../api.js'
import RulesPage, {
  RuleEditorModal,
  ConvertConfirmModal,
  RuleCard,
  isConditionValid,
  isActionValid,
} from './RulesPage.jsx'

vi.mock('../../../api.js', () => ({
  api: {
    getRules: vi.fn(),
    saveRules: vi.fn(),
    deleteRules: vi.fn(),
    checkCompatibility: vi.fn(),
    getFolders: vi.fn(),
  },
}))

function fileIntoRule(id, name) {
  return {
    id,
    name,
    enabled: true,
    matchAll: false,
    stopAfter: false,
    conditions: [{ field: 'Subject', operator: 'Contains', value: 'x', headerName: null }],
    actions: [{ type: 'FileInto', argument: 'X' }],
  }
}

function ruleSet(providerId, rules) {
  return { kind: 'Structured', providerId, scriptName: null, rules }
}

beforeEach(() => {
  vi.clearAllMocks()
  api.getFolders.mockResolvedValue([])
  api.saveRules.mockResolvedValue(null)
  api.deleteRules.mockResolvedValue(null)
})

// ── Slider state derived from provider ────────────────────────

describe('Extended rules slider', () => {
  it('is OFF when the active provider is rainloop', async () => {
    api.getRules.mockResolvedValue(ruleSet('rainloop', [fileIntoRule('a', 'r1')]))
    render(<RulesPage onClose={() => {}} />)

    const toggle = await screen.findByTitle('Extended rules')
    const checkbox = toggle.querySelector('input[type="checkbox"]')
    expect(checkbox.checked).toBe(false)
  })

  it('is ON when the active provider is weesky', async () => {
    api.getRules.mockResolvedValue(ruleSet('weesky', [fileIntoRule('a', 'r1')]))
    render(<RulesPage onClose={() => {}} />)

    const toggle = await screen.findByTitle('Extended rules')
    const checkbox = toggle.querySelector('input[type="checkbox"]')
    expect(checkbox.checked).toBe(true)
  })

  it('turning ON switches to weesky without a compatibility check', async () => {
    api.getRules.mockResolvedValue(ruleSet('rainloop', [fileIntoRule('a', 'r1')]))
    render(<RulesPage onClose={() => {}} />)

    const toggle = await screen.findByTitle('Extended rules')
    fireEvent.click(toggle.querySelector('input[type="checkbox"]'))

    await waitFor(() =>
      expect(api.saveRules).toHaveBeenCalledWith(expect.any(Array), 'weesky', null))
    expect(api.checkCompatibility).not.toHaveBeenCalled()
  })

  it('turning OFF with compatible rules switches straight to rainloop', async () => {
    api.getRules.mockResolvedValue(ruleSet('weesky', [fileIntoRule('a', 'r1')]))
    api.checkCompatibility.mockResolvedValue({ compatible: true, incompatible: [] })
    render(<RulesPage onClose={() => {}} />)

    const toggle = await screen.findByTitle('Extended rules')
    fireEvent.click(toggle.querySelector('input[type="checkbox"]'))

    await waitFor(() =>
      expect(api.checkCompatibility).toHaveBeenCalledWith('rainloop', expect.any(Array)))
    await waitFor(() =>
      expect(api.saveRules).toHaveBeenCalledWith(expect.any(Array), 'rainloop', null))
  })

  it('turning OFF with incompatible rules shows the conversion modal and drops them on confirm', async () => {
    const rules = [fileIntoRule('keep-me', 'Keeper'), fileIntoRule('lose-me', 'Loser')]
    api.getRules.mockResolvedValue(ruleSet('weesky', rules))
    api.checkCompatibility.mockResolvedValue({
      compatible: false,
      incompatible: [{ id: 'lose-me', name: 'Loser', reason: 'uses extended flags' }],
    })
    render(<RulesPage onClose={() => {}} />)

    const toggle = await screen.findByTitle('Extended rules')
    fireEvent.click(toggle.querySelector('input[type="checkbox"]'))

    expect(await screen.findByText('Turn off extended rules?')).toBeInTheDocument()
    expect(screen.getByText('uses extended flags')).toBeInTheDocument()

    fireEvent.click(screen.getByText('Delete & switch'))

    await waitFor(() =>
      expect(api.saveRules).toHaveBeenCalledWith(
        [expect.objectContaining({ id: 'keep-me' })], 'rainloop', null))
  })

  it('cancelling the conversion modal keeps the provider unchanged', async () => {
    api.getRules.mockResolvedValue(ruleSet('weesky', [fileIntoRule('a', 'r1')]))
    api.checkCompatibility.mockResolvedValue({
      compatible: false,
      incompatible: [{ id: 'a', name: 'r1', reason: 'nope' }],
    })
    render(<RulesPage onClose={() => {}} />)

    const toggle = await screen.findByTitle('Extended rules')
    fireEvent.click(toggle.querySelector('input[type="checkbox"]'))

    fireEvent.click(await screen.findByText('Cancel'))

    await waitFor(() =>
      expect(screen.queryByText('Turn off extended rules?')).not.toBeInTheDocument())
    expect(api.saveRules).not.toHaveBeenCalled()
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
    const actionSelect = Array.from(selects).find(s =>
      Array.from(s.options).some(o => o.value === 'FileInto'))
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
    const actionSelect = Array.from(selects).find(s =>
      Array.from(s.options).some(o => o.value === 'FileInto'))
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
    const rule = {
      ...fileIntoRule('a', 'r1'),
      actions: [
        { type: 'SetFlag', argument: '\\Flagged' },
        { type: 'FileInto', argument: 'X' },
      ],
    }
    render(
      <RuleEditorModal rule={rule} extended={true} onSave={() => {}} onClose={() => {}} />)

    const checkbox = screen.getByText('Mark as flagged ⭐').previousSibling?.querySelector('input')
      ?? document.querySelectorAll('input[type="checkbox"]')[1]
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
    const toggle = flaggedLabel.closest('.rule-wizard-toggle-row').querySelector('input')
    await userEvent.click(toggle)

    await userEvent.click(screen.getByText('Save changes'))

    expect(onSave).toHaveBeenCalledWith(
      expect.objectContaining({
        actions: expect.arrayContaining([
          expect.objectContaining({ type: 'SetFlag', argument: '\\Flagged' }),
          expect.objectContaining({ type: 'FileInto', argument: 'X' }),
        ])
      })
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
    const condFieldSelect = Array.from(selects).find(s =>
      Array.from(s.options).some(o => o.value === 'Subject'))
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
    const condFieldSelect = Array.from(selects).find(s =>
      Array.from(s.options).some(o => o.value === 'Subject'))
    expect(Array.from(condFieldSelect.options).some(o => o.value === 'Body')).toBe(false)
  })

  it('limits operators to Contains, NotContains and Regex when Body is selected', () => {
    const rule = {
      ...fileIntoRule('a', 'r1'),
      conditions: [{ field: 'Body', operator: 'Contains', value: 'casino', headerName: null }],
    }
    render(
      <RuleEditorModal rule={rule} extended={true} onSave={() => {}} onClose={() => {}} />)

    const selects = document.querySelectorAll('select')
    const opSelect = Array.from(selects).find(s =>
      Array.from(s.options).some(o => o.value === 'Contains') &&
      !Array.from(s.options).some(o => o.value === 'FileInto'))
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
    const condFieldSelect = Array.from(selects).find(s =>
      Array.from(s.options).some(o => o.value === 'Subject'))
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
    const condFieldSelect = Array.from(selects).find(s =>
      Array.from(s.options).some(o => o.value === 'Subject'))
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
    const opSelect = Array.from(selects).find(s =>
      Array.from(s.options).some(o => o.value === 'Contains') &&
      !Array.from(s.options).some(o => o.value === 'FileInto'))
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
    const opSelect = Array.from(selects).find(s =>
      Array.from(s.options).some(o => o.value === 'Contains') &&
      !Array.from(s.options).some(o => o.value === 'FileInto'))
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
    const condFieldSelect = Array.from(selects).find(s =>
      Array.from(s.options).some(o => o.value === 'Subject'))
    expect(Array.from(condFieldSelect.options).some(o => o.value === 'Duplicate')).toBe(true)
  })

  it('hides operator select when Duplicate field is active', () => {
    const rule = {
      ...fileIntoRule('a', 'r1'),
      conditions: [{ field: 'Duplicate', operator: 'Contains', value: '', headerName: null }],
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
    await userEvent.click(screen.getByText('Create').firstElementChild)
    await userEvent.click(screen.getByText('Save changes'))

    expect(onSave).toHaveBeenCalledWith(
      expect.objectContaining({
        actions: expect.arrayContaining([
          expect.objectContaining({ type: 'FileInto', autoCreate: true }),
        ])
      })
    )
  })
})

// ── Date conditions ───────────────────────────────────────────

describe('Date condition fields', () => {
  function getCondFieldSelect() {
    return Array.from(document.querySelectorAll('select')).find(s =>
      Array.from(s.options).some(o => o.value === 'Subject'))
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
    const rule = {
      ...fileIntoRule('a', 'r1'),
      conditions: [{ field: 'CurrentDate', operator: 'Before', value: '2026-12-31', headerName: null }],
    }
    render(<RuleEditorModal rule={rule} extended={true} onSave={() => {}} onClose={() => {}} />)
    // When CurrentDate is selected the op select shows date operators (no 'Contains')
    const dateOpSelect = Array.from(document.querySelectorAll('select')).find(s =>
      Array.from(s.options).some(o => o.value === 'Before'))
    const ops = Array.from(dateOpSelect.options).map(o => o.value)
    expect(ops).toContain('Before')
    expect(ops).toContain('OnOrAfter')
    expect(ops).toContain('Equals')
    expect(ops).not.toContain('Contains')
    expect(ops).not.toContain('Matches')
  })

  it('shows a date input instead of text when CurrentDate is selected', () => {
    const rule = {
      ...fileIntoRule('a', 'r1'),
      conditions: [{ field: 'CurrentDate', operator: 'Before', value: '2026-12-31', headerName: null }],
    }
    render(<RuleEditorModal rule={rule} extended={true} onSave={() => {}} onClose={() => {}} />)
    expect(document.querySelector('input[type="date"]')).toBeInTheDocument()
  })

  it('Before and OnOrAfter operators are absent in non-extended mode', () => {
    render(<RuleEditorModal rule={fileIntoRule('a', 'r1')} extended={false} onSave={() => {}} onClose={() => {}} />)
    const opSelect = Array.from(document.querySelectorAll('select')).find(s =>
      Array.from(s.options).some(o => o.value === 'Contains') &&
      !Array.from(s.options).some(o => o.value === 'FileInto'))
    const ops = Array.from(opSelect.options).map(o => o.value)
    expect(ops).not.toContain('Before')
    expect(ops).not.toContain('OnOrAfter')
  })
})

// ── Weekday / hour conditions ─────────────────────────────────

describe('CurrentWeekday condition', () => {
  it('appears in extended mode', () => {
    render(<RuleEditorModal rule={fileIntoRule('a', 'r1')} extended={true} onSave={() => {}} onClose={() => {}} />)
    const fieldSelect = Array.from(document.querySelectorAll('select')).find(s =>
      Array.from(s.options).some(o => o.value === 'Subject'))
    expect(Array.from(fieldSelect.options).some(o => o.value === 'CurrentWeekday')).toBe(true)
  })

  it('is absent in non-extended mode', () => {
    render(<RuleEditorModal rule={fileIntoRule('a', 'r1')} extended={false} onSave={() => {}} onClose={() => {}} />)
    const fieldSelect = Array.from(document.querySelectorAll('select')).find(s =>
      Array.from(s.options).some(o => o.value === 'Subject'))
    expect(Array.from(fieldSelect.options).some(o => o.value === 'CurrentWeekday')).toBe(false)
  })

  it('shows weekday dropdown with preset options when selected', () => {
    const rule = {
      ...fileIntoRule('a', 'r1'),
      conditions: [{ field: 'CurrentWeekday', operator: 'Contains', value: '1,2,3,4,5', headerName: null }],
    }
    render(<RuleEditorModal rule={rule} extended={true} onSave={() => {}} onClose={() => {}} />)
    const weekdaySelect = Array.from(document.querySelectorAll('select')).find(s =>
      Array.from(s.options).some(o => o.value === '1,2,3,4,5'))
    expect(weekdaySelect).toBeTruthy()
    const opts = Array.from(weekdaySelect.options).map(o => o.value)
    expect(opts).toContain('0,6')
    expect(opts).toContain('1')
    expect(opts).toContain('0')
  })

  it('hides the operator select when CurrentWeekday is active', () => {
    const rule = {
      ...fileIntoRule('a', 'r1'),
      conditions: [{ field: 'CurrentWeekday', operator: 'Contains', value: '1,2,3,4,5', headerName: null }],
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
    const fieldSelect = Array.from(document.querySelectorAll('select')).find(s =>
      Array.from(s.options).some(o => o.value === 'Subject'))
    expect(Array.from(fieldSelect.options).some(o => o.value === 'CurrentHour')).toBe(true)
  })

  it('shows number input 0-23 and Before/OnOrAfter operators when selected', () => {
    const rule = {
      ...fileIntoRule('a', 'r1'),
      conditions: [{ field: 'CurrentHour', operator: 'Before', value: '9', headerName: null }],
    }
    render(<RuleEditorModal rule={rule} extended={true} onSave={() => {}} onClose={() => {}} />)
    const hourInput = document.querySelector('input[type="number"][min="0"][max="23"]')
    expect(hourInput).toBeInTheDocument()
    const opSelect = Array.from(document.querySelectorAll('select')).find(s =>
      Array.from(s.options).some(o => o.value === 'Before'))
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
    expect(isConditionValid({ field: 'Duplicate', value: '' })).toBe(true)
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
  function circle(n) {
    return Array.from(document.querySelectorAll('.rule-wizard-circle')).find(c => c.textContent === String(n))
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

    await userEvent.type(document.querySelector('.rule-wizard-input'), 'My rule')

    expect(circle(1).className).not.toContain('rule-wizard-circle--locked')
    expect(circle(2).className).toContain('rule-wizard-circle--active')
    expect(circle(3).className).toContain('rule-wizard-circle--locked')
    expect(circle(4).className).toContain('rule-wizard-circle--locked')
    expect(screen.getByText('Create rule')).toBeDisabled()
  })

  it('unlocks step 3 only once a valid condition exists', async () => {
    render(<RuleEditorModal rule={null} extended={false} onSave={() => {}} onClose={() => {}} />)

    await userEvent.type(document.querySelector('.rule-wizard-input'), 'My rule')
    await userEvent.type(screen.getByPlaceholderText('Value'), 'urgent')

    expect(circle(2).className).not.toContain('rule-wizard-circle--locked')
    expect(circle(2).className).not.toContain('rule-wizard-circle--active')
    expect(circle(3).className).toContain('rule-wizard-circle--active')
    expect(circle(4).className).toContain('rule-wizard-circle--locked')
    expect(screen.getByText('Create rule')).toBeDisabled()
  })

  it('enables Create rule only once a valid action exists', async () => {
    render(<RuleEditorModal rule={null} extended={false} onSave={() => {}} onClose={() => {}} />)

    await userEvent.type(document.querySelector('.rule-wizard-input'), 'My rule')
    await userEvent.type(screen.getByPlaceholderText('Value'), 'urgent')
    await userEvent.type(screen.getByPlaceholderText('Folder name'), 'Urgent')

    expect(circle(3).className).not.toContain('rule-wizard-circle--locked')
    expect(circle(4).className).not.toContain('rule-wizard-circle--locked')
    expect(screen.getByText('Create rule')).toBeEnabled()
  })

  it('re-locks later steps and disables Save when a value is cleared (edit mode)', async () => {
    const rule = {
      id: 'a',
      name: 'Existing',
      enabled: true,
      matchAll: false,
      stopAfter: false,
      conditions: [{ field: 'Subject', operator: 'Contains', value: 'urgent', headerName: null }],
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
  function makeCardProps(overrides = {}) {
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
    const rule = { ...fileIntoRule('r1', 'My Rule'), enabled: false }
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

  it('calls onToggleEnabled with false when enabled rule checkbox is clicked', () => {
    const onToggleEnabled = vi.fn()
    render(<RuleCard {...makeCardProps({ onToggleEnabled })} />)
    fireEvent.click(screen.getByTitle('Disable').querySelector('input[type="checkbox"]'))
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
  function cardWithCondition(cond) {
    return { id: 'r1', name: 'R', enabled: true, matchAll: false, stopAfter: false,
      conditions: [cond], actions: [{ type: 'Keep', argument: '' }] }
  }
  function cardWithAction(action) {
    return { id: 'r1', name: 'R', enabled: true, matchAll: false, stopAfter: false,
      conditions: [{ field: 'Subject', operator: 'Contains', value: 'x', headerName: null }],
      actions: [action] }
  }
  function renderExpanded(rule) {
    render(<RuleCard rule={rule} {...baseProps()} />)
    fireEvent.click(screen.getByTitle('Expand'))
  }

  it('Subject Contains', () => {
    renderExpanded(cardWithCondition({ field: 'Subject', operator: 'Contains', value: 'urgent', headerName: null }))
    expect(screen.getByText('Subject contains "urgent"')).toBeInTheDocument()
  })

  it('Duplicate without value', () => {
    renderExpanded(cardWithCondition({ field: 'Duplicate', operator: null, value: '', headerName: null }))
    expect(screen.getByText('Duplicate message')).toBeInTheDocument()
  })

  it('Duplicate with seconds window', () => {
    renderExpanded(cardWithCondition({ field: 'Duplicate', operator: null, value: '60', headerName: null }))
    expect(screen.getByText('Duplicate (within 60s)')).toBeInTheDocument()
  })

  it('CurrentDate', () => {
    renderExpanded(cardWithCondition({ field: 'CurrentDate', operator: 'Before', value: '2024-01-01', headerName: null }))
    expect(screen.getByText('Current date is before 2024-01-01')).toBeInTheDocument()
  })

  it('MessageDate', () => {
    renderExpanded(cardWithCondition({ field: 'MessageDate', operator: 'OnOrAfter', value: '2024-06-01', headerName: null }))
    expect(screen.getByText('Message date is on or after 2024-06-01')).toBeInTheDocument()
  })

  it('CurrentWeekday', () => {
    renderExpanded(cardWithCondition({ field: 'CurrentWeekday', operator: null, value: '1,2,3,4,5', headerName: null }))
    expect(screen.getByText('Weekday is Weekday (Mon–Fri)')).toBeInTheDocument()
  })

  it('CurrentHour', () => {
    renderExpanded(cardWithCondition({ field: 'CurrentHour', operator: 'Before', value: '9', headerName: null }))
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
    api.getRules.mockReturnValue(new Promise(() => {}))
    render(<RulesPage onClose={() => {}} />)
    expect(document.querySelector('.loading-center')).toBeInTheDocument()
  })

  it('shows error toast when getRules rejects', async () => {
    api.getRules.mockRejectedValue(new Error('network failure'))
    render(<RulesPage onClose={() => {}} />)
    await screen.findByText('network failure')
  })

  it('shows empty state when rules list is empty', async () => {
    api.getRules.mockResolvedValue(ruleSet('weesky', []))
    render(<RulesPage onClose={() => {}} />)
    await screen.findByText(/No rules yet/)
  })

  it('renders rule cards when rules exist', async () => {
    api.getRules.mockResolvedValue(ruleSet('weesky', [fileIntoRule('a', 'My Test Rule')]))
    render(<RulesPage onClose={() => {}} />)
    await screen.findByText('My Test Rule')
  })

  it('shows Advanced notice for Advanced kind', async () => {
    api.getRules.mockResolvedValue({ kind: 'Advanced', providerId: 'weesky', scriptName: 'custom', rules: [] })
    render(<RulesPage onClose={() => {}} />)
    await screen.findByText(/cannot be parsed/)
  })

  it('shows provider badge for weesky', async () => {
    api.getRules.mockResolvedValue(ruleSet('weesky', []))
    render(<RulesPage onClose={() => {}} />)
    await waitFor(() => expect(document.querySelector('.provider-badge')).toBeInTheDocument())
    expect(document.querySelector('.provider-badge').textContent).toBe('Weesky')
  })
})

// ── RulesPage — CRUD ──────────────────────────────────────────

describe('RulesPage — CRUD', () => {
  beforeEach(() => {
    api.getRules.mockResolvedValue(ruleSet('weesky', [fileIntoRule('a', 'Existing Rule')]))
  })

  it('click New rule opens editor with empty name', async () => {
    render(<RulesPage onClose={() => {}} />)
    await screen.findByText('Existing Rule')
    await userEvent.click(screen.getByRole('button', { name: /New rule/i }))
    const nameInput = document.querySelector('.rule-wizard-input')
    expect(nameInput).toBeInTheDocument()
    expect(nameInput.value).toBe('')
  })

  it('create new rule calls saveRules with the new rule appended', async () => {
    render(<RulesPage onClose={() => {}} />)
    await screen.findByText('Existing Rule')

    await userEvent.click(screen.getByRole('button', { name: /New rule/i }))
    await userEvent.type(document.querySelector('.rule-wizard-input'), 'New Test Rule')
    await userEvent.type(screen.getByPlaceholderText('Value'), 'spam')
    await userEvent.type(screen.getByPlaceholderText('Folder name'), 'Spam')
    await userEvent.click(screen.getByText('Create rule'))

    await waitFor(() => expect(api.saveRules).toHaveBeenCalledWith(
      expect.arrayContaining([
        expect.objectContaining({ name: 'Existing Rule' }),
        expect.objectContaining({ name: 'New Test Rule' }),
      ]),
      'weesky', null
    ))
  })

  it('click Edit opens editor pre-filled with rule name', async () => {
    render(<RulesPage onClose={() => {}} />)
    await screen.findByText('Existing Rule')

    await userEvent.click(screen.getByTitle('Edit'))
    expect(document.querySelector('.rule-wizard-input').value).toBe('Existing Rule')
  })

  it('edit and save calls saveRules with updated rule', async () => {
    render(<RulesPage onClose={() => {}} />)
    await screen.findByText('Existing Rule')

    await userEvent.click(screen.getByTitle('Edit'))
    const nameInput = document.querySelector('.rule-wizard-input')
    await userEvent.clear(nameInput)
    await userEvent.type(nameInput, 'Renamed Rule')
    await userEvent.click(screen.getByText('Save changes'))

    await waitFor(() => expect(api.saveRules).toHaveBeenCalledWith(
      [expect.objectContaining({ name: 'Renamed Rule' })],
      'weesky', null
    ))
  })

  it('click Delete opens confirm modal', async () => {
    render(<RulesPage onClose={() => {}} />)
    await screen.findByText('Existing Rule')

    await userEvent.click(screen.getByTitle('Delete'))
    expect(screen.getByText('Confirm deletion')).toBeInTheDocument()
  })

  it('confirm delete calls saveRules without the deleted rule', async () => {
    render(<RulesPage onClose={() => {}} />)
    await screen.findByText('Existing Rule')

    await userEvent.click(screen.getByTitle('Delete'))
    await userEvent.click(screen.getByText('Delete', { selector: 'button' }))

    await waitFor(() => expect(api.saveRules).toHaveBeenCalledWith([], 'weesky', null))
  })

  it('toggle enabled calls saveRules with updated enabled flag', async () => {
    render(<RulesPage onClose={() => {}} />)
    await screen.findByText('Existing Rule')

    fireEvent.click(screen.getByTitle('Disable').querySelector('input[type="checkbox"]'))

    await waitFor(() => expect(api.saveRules).toHaveBeenCalledWith(
      [expect.objectContaining({ name: 'Existing Rule', enabled: false })],
      'weesky', null
    ))
  })

  it('save error shows error toast', async () => {
    api.saveRules.mockRejectedValue(new Error('connection refused'))
    render(<RulesPage onClose={() => {}} />)
    await screen.findByText('Existing Rule')

    await userEvent.click(screen.getByTitle('Edit'))
    await userEvent.click(screen.getByText('Save changes'))

    await screen.findByText('connection refused')
  })
})

// ── RulesPage — reordering ────────────────────────────────────

describe('RulesPage — reordering', () => {
  beforeEach(() => {
    api.getRules.mockResolvedValue(ruleSet('weesky', [
      fileIntoRule('a', 'First'),
      fileIntoRule('b', 'Second'),
    ]))
  })

  it('Move up swaps the rule with the one above it', async () => {
    render(<RulesPage onClose={() => {}} />)
    await screen.findByText('Second')

    const moveUpBtns = screen.getAllByTitle('Move up')
    await userEvent.click(moveUpBtns[1])

    await waitFor(() => expect(api.saveRules).toHaveBeenCalledWith(
      [expect.objectContaining({ name: 'Second' }), expect.objectContaining({ name: 'First' })],
      'weesky', null
    ))
  })

  it('Move down swaps the rule with the one below it', async () => {
    render(<RulesPage onClose={() => {}} />)
    await screen.findByText('First')

    const moveDownBtns = screen.getAllByTitle('Move down')
    await userEvent.click(moveDownBtns[0])

    await waitFor(() => expect(api.saveRules).toHaveBeenCalledWith(
      [expect.objectContaining({ name: 'Second' }), expect.objectContaining({ name: 'First' })],
      'weesky', null
    ))
  })
})

// ── RulesPage — delete all script ────────────────────────────

describe('RulesPage — delete all script', () => {
  function advancedRuleSet() {
    return { kind: 'Advanced', providerId: 'weesky', scriptName: 'custom', rules: [] }
  }

  it('Delete script button opens confirm modal', async () => {
    api.getRules.mockResolvedValue(advancedRuleSet())
    render(<RulesPage onClose={() => {}} />)
    await screen.findByText(/cannot be parsed/)

    await userEvent.click(screen.getByText('Delete script'))
    expect(screen.getByText('Confirm deletion')).toBeInTheDocument()
  })

  it('confirm calls deleteRules and switches to structured view', async () => {
    api.getRules.mockResolvedValue(advancedRuleSet())
    render(<RulesPage onClose={() => {}} />)
    await screen.findByText(/cannot be parsed/)

    await userEvent.click(screen.getByText('Delete script'))
    await userEvent.click(screen.getByText('Delete', { selector: 'button' }))

    await waitFor(() => expect(api.deleteRules).toHaveBeenCalled())
    await screen.findByText('Script deleted')
    expect(document.querySelector('.rules-toolbar')).toBeInTheDocument()
  })

  it('delete error shows error toast', async () => {
    api.getRules.mockResolvedValue(advancedRuleSet())
    api.deleteRules.mockRejectedValue(new Error('IMAP connection lost'))
    render(<RulesPage onClose={() => {}} />)
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
})
