import { render, screen, waitFor, fireEvent } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { api } from '../api.js'
import RulesPage, {
  RuleEditorModal,
  ConvertConfirmModal,
} from './RulesPage.jsx'

vi.mock('../api.js', () => ({
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

  it('limits operators to Contains and Matches when Body is selected', () => {
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
    expect(Array.from(opSelect.options).map(o => o.value)).toEqual(['Contains', 'Matches', 'Regex'])
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

  it('hides regex option in non-extended mode', () => {
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
    expect(Array.from(opSelect.options).some(o => o.value === 'Regex')).toBe(false)
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
