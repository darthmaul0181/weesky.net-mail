import { render, screen, fireEvent } from '@testing-library/react'
import { describe, it, expect, vi } from 'vitest'
import type { ComponentProps } from 'react'
import { RuleCard } from './RuleCard'
import { checkboxIn, fileIntoRule } from './rulesTestFixtures'
import type { SieveCondition, SieveRuleWrite } from './rulesTypes'

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

describe('RuleCard', () => {
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

  it.each([
    ['Move up', { isFirst: true }],
    ['Move down', { isLast: true }],
  ])('%s is disabled at its end of the list', (title, ends) => {
    render(<RuleCard {...makeCardProps(ends)} />)
    expect(screen.getByTitle(title)).toBeDisabled()
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

  it.each([
    ['Move up', 'onMoveUp'],
    ['Move down', 'onMoveDown'],
  ] as const)('calls %s’s handler when it is clicked', (title, handler) => {
    const onMove = vi.fn()
    render(<RuleCard {...makeCardProps({ [handler]: onMove })} />)
    fireEvent.click(screen.getByTitle(title))
    expect(onMove).toHaveBeenCalled()
  })
})

// ── summarize helpers (via RuleCard expand) ────────────────────

describe('summarize helpers', () => {
  function cardWithCondition(cond: SieveCondition): SieveRuleWrite {
    return { id: 'r1', name: 'R', enabled: true, matchAll: false, stopAfter: false,
      conditions: [cond], actions: [{ type: 'Keep', argument: '' }] }
  }
  function cardWithAction(action: SieveRuleWrite['actions'][number]): SieveRuleWrite {
    return { id: 'r1', name: 'R', enabled: true, matchAll: false, stopAfter: false,
      conditions: [{ field: 'Subject', operator: 'Contains', value: 'x' }],
      actions: [action] }
  }

  it.each([
    ['Subject Contains', cardWithCondition({ field: 'Subject', operator: 'Contains', value: 'urgent' }), 'Subject contains "urgent"'],
    ['Duplicate without value', cardWithCondition({ field: 'Duplicate', operator: 'Contains', value: '' }), 'Duplicate message'],
    ['Duplicate with seconds window', cardWithCondition({ field: 'Duplicate', operator: 'Contains', value: '60' }), 'Duplicate (within 60s)'],
    ['CurrentDate', cardWithCondition({ field: 'CurrentDate', operator: 'Before', value: '2024-01-01' }), 'Current date is before 2024-01-01'],
    ['MessageDate', cardWithCondition({ field: 'MessageDate', operator: 'OnOrAfter', value: '2024-06-01' }), 'Message date is on or after 2024-06-01'],
    ['CurrentWeekday', cardWithCondition({ field: 'CurrentWeekday', operator: 'Contains', value: '1,2,3,4,5' }), 'Weekday is Weekday (Mon–Fri)'],
    ['CurrentHour', cardWithCondition({ field: 'CurrentHour', operator: 'Before', value: '9' }), 'Hour is before 9:00'],
    ['Custom header uses headerName', cardWithCondition({ field: 'Header', operator: 'Contains', value: 'spam', headerName: 'X-Spam' }), 'X-Spam contains "spam"'],
    ['Redirect action', cardWithAction({ type: 'Redirect', argument: 'a@b.com' }), '⇥ a@b.com'],
    ['SetFlag Seen', cardWithAction({ type: 'SetFlag', argument: '\\Seen' }), 'Mark as read'],
    ['SetFlag Flagged', cardWithAction({ type: 'SetFlag', argument: '\\Flagged' }), '⭐ Flagged'],
    ['Keep action', cardWithAction({ type: 'Keep', argument: '' }), 'Keep in inbox'],
    ['Discard action', cardWithAction({ type: 'Discard', argument: '' }), 'Discard'],
    ['FileInto with autoCreate shows ✚ in expanded view', cardWithAction({ type: 'FileInto', argument: 'Archive', autoCreate: true }), '→ Archive ✚'],
  ])('%s', (_name, rule, text) => {
    render(<RuleCard {...makeCardProps({ rule })} />)
    fireEvent.click(screen.getByTitle('Expand'))
    expect(screen.getByText(text)).toBeInTheDocument()
  })

  it('FileInto collapsed pill shows → prefix', () => {
    render(<RuleCard {...makeCardProps({ rule: cardWithAction({ type: 'FileInto', argument: 'Inbox' }) })} />)
    expect(screen.getByText('→ Inbox')).toBeInTheDocument()
  })
})
