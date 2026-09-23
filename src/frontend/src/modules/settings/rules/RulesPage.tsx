import { useState, useEffect, useLayoutEffect, useRef } from 'react'
import type { FormEvent } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import i18next from 'i18next'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import type { TFunction } from 'i18next'
import { api } from '../../../api.js'
import Modal from '../../../components/Modal'
import { useAccountId } from '../../../hooks/useAccountId'
import { useKeyedState } from '../../../hooks/useKeyedState'
import { useToasts } from '../../../hooks/useToasts'
import type { AddToast } from '../../../hooks/useToasts'
import { flatten } from '../../mail/folders/folderNodes'
import Toasts from '../../../components/Toasts'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal'
import HelpTooltip from '../../../components/HelpTooltip'
import ListLoadFailed from '../../../components/ListLoadFailed'
import TrashIcon from '../../../icons/TrashIcon'
import PencilIcon from '../../../icons/PencilIcon'
import FunnelIcon from '../../../icons/FunnelIcon'
import type {
  CompatibilityCheckResult, IncompatibleRule, SieveActionType, SieveConditionField,
  SieveConditionOperator, SieveRuleSet, SieveRuleWrite,
} from './rulesTypes'

type RuleCondition = SieveRuleWrite['conditions'][number]
type RuleAction = SieveRuleWrite['actions'][number]
/** The editor's rule: a new one has no id until the page gives it one on save. */
type RuleDraft = Omit<SieveRuleWrite, 'id'> & { id: string | null }
/** The loaded set as the page keeps it: its rules edited in place, and a provider switch clearing
    the script name to null so the backend writes the new provider's default script. */
type LoadedRuleSet = Omit<SieveRuleSet, 'rules' | 'scriptName'> & {
  rules: SieveRuleWrite[]
  scriptName?: string | null
}
type SettingsT = TFunction<'settings'>
interface PendingConversion { accountId: string; incompatible: IncompatibleRule[] }

const rulesKey = (accountId: string) => ['rules', accountId] as const

// ── Icons ─────────────────────────────────────────────────────

function PlusIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
      <line x1="12" y1="5" x2="12" y2="19" />
      <line x1="5" y1="12" x2="19" y2="12" />
    </svg>
  )
}

function ChevronUpIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width="13" height="13" viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
      <polyline points="18 15 12 9 6 15" />
    </svg>
  )
}

function ChevronDownIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width="13" height="13" viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
      <polyline points="6 9 12 15 18 9" />
    </svg>
  )
}

function ArrowUpIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width="13" height="13" viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
      <line x1="12" y1="19" x2="12" y2="5" />
      <polyline points="5 12 12 5 19 12" />
    </svg>
  )
}

function ArrowDownIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width="13" height="13" viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
      <line x1="12" y1="5" x2="12" y2="19" />
      <polyline points="19 12 12 19 5 12" />
    </svg>
  )
}

function GripIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true" focusable="false">
      <circle cx="8"  cy="6"  r="2" /><circle cx="16" cy="6"  r="2" />
      <circle cx="8"  cy="12" r="2" /><circle cx="16" cy="12" r="2" />
      <circle cx="8"  cy="18" r="2" /><circle cx="16" cy="18" r="2" />
    </svg>
  )
}

// ── Constants ─────────────────────────────────────────────────

interface ConditionFieldDef {
  value: SieveConditionField
  operators?: SieveConditionOperator[]
  extendedOnly?: boolean
  noOperator?: boolean
  inputType?: 'date' | 'weekday' | 'hour'
}

interface OperatorDef { value: SieveConditionOperator; extendedOnly?: boolean }

/** `SetFlag` is the editor's two switches, never a row of its own. */
interface ActionTypeDef { value: Exclude<SieveActionType, 'SetFlag'>; hasArg: boolean; extendedOnly?: boolean }

const TEXT_OPS: SieveConditionOperator[] = ['Contains', 'NotContains', 'Equals', 'NotEquals', 'Regex']

const CONDITION_FIELDS: ConditionFieldDef[] = [
  { value: 'From',            operators: TEXT_OPS },
  { value: 'Recipient',       operators: TEXT_OPS },
  { value: 'Subject',         operators: TEXT_OPS },
  { value: 'Header',          operators: TEXT_OPS },
  { value: 'Size',            operators: ['Larger', 'Smaller'] },
  { value: 'Body',            extendedOnly: true, operators: ['Contains', 'NotContains', 'Regex'] },
  { value: 'EnvelopeFrom',    extendedOnly: true, operators: TEXT_OPS },
  { value: 'EnvelopeTo',      extendedOnly: true, operators: TEXT_OPS },
  { value: 'RecipientDetail', extendedOnly: true, operators: TEXT_OPS },
  { value: 'Duplicate',       extendedOnly: true, noOperator: true },
  { value: 'CurrentDate',     extendedOnly: true, operators: ['Before', 'OnOrAfter', 'Equals'], inputType: 'date' },
  { value: 'MessageDate',     extendedOnly: true, operators: ['Before', 'OnOrAfter', 'Equals'], inputType: 'date' },
  { value: 'CurrentWeekday',  extendedOnly: true, noOperator: true, inputType: 'weekday' },
  { value: 'CurrentHour',     extendedOnly: true, operators: ['Before', 'OnOrAfter', 'Equals'], inputType: 'hour' },
]

const WEEKDAY_VALUES = ['1,2,3,4,5', '0,6', '1', '2', '3', '4', '5', '6', '0']

/** The nine keys are written out rather than held on the options: a key that reaches `t()` only
    as a variable is invisible to the typed `t()` and to `src/locales/keys.test.ts` alike. */
function weekdayLabel(value: string, t: SettingsT) {
  switch (value) {
    case '1,2,3,4,5': return t('rules.weekdays.workweek')
    case '0,6': return t('rules.weekdays.weekend')
    case '1': return t('rules.weekdays.monday')
    case '2': return t('rules.weekdays.tuesday')
    case '3': return t('rules.weekdays.wednesday')
    case '4': return t('rules.weekdays.thursday')
    case '5': return t('rules.weekdays.friday')
    case '6': return t('rules.weekdays.saturday')
    case '0': return t('rules.weekdays.sunday')
    default: return null
  }
}

const CONDITION_OPERATORS: OperatorDef[] = [
  { value: 'Contains' },
  { value: 'NotContains' },
  { value: 'Equals' },
  { value: 'NotEquals' },
  { value: 'Matches' }, // kept for display of legacy rules only
  { value: 'Regex' },
  { value: 'Larger' },
  { value: 'Smaller' },
  { value: 'Before',    extendedOnly: true },
  { value: 'OnOrAfter', extendedOnly: true },
]

const ACTION_TYPES: ActionTypeDef[] = [
  { value: 'FileInto', hasArg: true },
  { value: 'Redirect', hasArg: true },
  { value: 'Discard',  hasArg: false },
  { value: 'Reject',   hasArg: true },
  { value: 'Keep',     hasArg: false, extendedOnly: true },
]

// The labels are spelled out for weekdayLabel's reason; a value no list offers (To, Cc) is shown raw.
function fieldLabel(value: SieveConditionField, t: SettingsT): string {
  switch (value) {
    case 'From': return t('rules.fields.From')
    case 'Recipient': return t('rules.fields.Recipient')
    case 'Subject': return t('rules.fields.Subject')
    case 'Header': return t('rules.fields.Header')
    case 'Size': return t('rules.fields.Size')
    case 'Body': return t('rules.fields.Body')
    case 'EnvelopeFrom': return t('rules.fields.EnvelopeFrom')
    case 'EnvelopeTo': return t('rules.fields.EnvelopeTo')
    case 'RecipientDetail': return t('rules.fields.RecipientDetail')
    case 'Duplicate': return t('rules.fields.Duplicate')
    case 'CurrentDate': return t('rules.fields.CurrentDate')
    case 'MessageDate': return t('rules.fields.MessageDate')
    case 'CurrentWeekday': return t('rules.fields.CurrentWeekday')
    case 'CurrentHour': return t('rules.fields.CurrentHour')
    default: return value
  }
}

function operatorLabel(value: SieveConditionOperator, t: SettingsT): string {
  switch (value) {
    case 'Contains': return t('rules.operators.Contains')
    case 'NotContains': return t('rules.operators.NotContains')
    case 'Equals': return t('rules.operators.Equals')
    case 'NotEquals': return t('rules.operators.NotEquals')
    case 'Matches': return t('rules.operators.Matches')
    case 'Regex': return t('rules.operators.Regex')
    case 'Larger': return t('rules.operators.Larger')
    case 'Smaller': return t('rules.operators.Smaller')
    case 'Before': return t('rules.operators.Before')
    case 'OnOrAfter': return t('rules.operators.OnOrAfter')
    default: return value
  }
}

function actionTypeLabel(value: ActionTypeDef['value'], t: SettingsT): string {
  switch (value) {
    case 'FileInto': return t('rules.actionTypes.FileInto')
    case 'Redirect': return t('rules.actionTypes.Redirect')
    case 'Discard': return t('rules.actionTypes.Discard')
    case 'Reject': return t('rules.actionTypes.Reject')
    case 'Keep': return t('rules.actionTypes.Keep')
  }
}

function actionArgPlaceholder(value: ActionTypeDef['value'], t: SettingsT): string | undefined {
  switch (value) {
    case 'FileInto': return t('rules.actionArgs.FileInto')
    case 'Redirect': return t('rules.actionArgs.Redirect')
    case 'Reject': return t('rules.actionArgs.Reject')
    default: return undefined
  }
}

/** A `<select>`'s value read back as the option it names, so the list it drew is also its type. */
function chosen<T extends { value: string }>(options: T[], value: string): T | undefined {
  return options.find(o => o.value === value)
}

// ── Helpers ───────────────────────────────────────────────────

function extractError(err: unknown): string {
  if (!err) return ''
  const msg = (typeof err === 'object' && 'message' in err && typeof err.message === 'string' && err.message)
    || (typeof err === 'string' ? err : '')
  try {
    const parsed: unknown = JSON.parse(msg)
    const message = typeof parsed === 'object' && parsed !== null && 'message' in parsed ? parsed.message : undefined
    return (typeof message === 'string' && message) || msg
  } catch {
    return msg
  }
}

function summarizeCondition(c: RuleCondition, t: SettingsT) {
  if (c.field === 'Duplicate')
    return c.value
      ? t('rules.summary.duplicateWithin', { seconds: c.value })
      : t('rules.fields.Duplicate')
  const operator = operatorLabel(c.operator, t)
  const name = c.field === 'Header'
    ? (c.headerName ?? t('rules.summary.header'))
    : fieldLabel(c.field, t)
  if (c.field === 'CurrentDate' || c.field === 'MessageDate')
    return t('rules.summary.plain', { name, operator, value: c.value })
  if (c.field === 'CurrentWeekday') {
    return t('rules.summary.weekdayIs', { day: weekdayLabel(c.value, t) ?? c.value })
  }
  if (c.field === 'CurrentHour')
    return t('rules.summary.hour', { operator, value: c.value })
  return t('rules.summary.quoted', { name, operator, value: c.value })
}

function summarizeAction(a: RuleAction, compact: boolean, t: SettingsT) {
  switch (a.type) {
    case 'FileInto': {
      const label = `${a.argument ?? '?'}${a.autoCreate ? ' ✚' : ''}`
      return compact ? `→ ${label}` : label
    }
    case 'Redirect': return `⇥ ${a.argument ?? '?'}`
    case 'SetFlag':
      if (a.argument === '\\Seen'    || a.argument === '\\\\Seen')    return t('rules.markAsRead')
      if (a.argument === '\\Flagged' || a.argument === '\\\\Flagged') return t('rules.summary.flagged')
      return t('rules.summary.flag', { flag: a.argument })
    case 'Keep':    return t('rules.actionTypes.Keep')
    case 'Discard': return t('rules.actionTypes.Discard')
    case 'Reject':  return t('rules.summary.reject')
    default:        return a.type
  }
}

// A condition counts as "filled in" once it has the data the backend needs.
export function isConditionValid(c: RuleCondition | undefined): boolean {
  if (!c) return false
  if (c.field === 'Duplicate') return true            // seconds window is optional
  if (c.field === 'Header' && !(c.headerName ?? '').trim()) return false
  return (c.value ?? '').toString().trim() !== ''
}

// An action counts as "filled in" once any required argument is present.
export function isActionValid(a: RuleAction | undefined): boolean {
  if (!a) return false
  switch (a.type) {
    case 'FileInto':
    case 'Redirect':
    case 'Reject':
    case 'SetFlag':
      return (a.argument ?? '').trim() !== ''
    case 'Discard':
    case 'Keep':
      return true
    default:
      return false
  }
}

function makeEmptyRule(): RuleDraft {
  return {
    id: null,
    name: '',
    enabled: true,
    matchAll: false,
    stopAfter: false,
    conditions: [{ field: 'Subject', operator: 'Contains', value: '', headerName: null }],
    actions: [{ type: 'FileInto', argument: '' }],
  }
}

// ── RuleCard ──────────────────────────────────────────────────

interface RuleCardProps {
  rule: SieveRuleWrite
  onEdit: () => void
  onDelete: () => void
  onToggleEnabled: (enabled: boolean) => void
  isFirst: boolean
  isLast: boolean
  onMoveUp: () => void
  onMoveDown: () => void
  isDragOver: boolean
  onDragStart: () => void
  onDragOver: () => void
  onDrop: () => void
  onDragEnd: () => void
}

export function RuleCard({ rule, onEdit, onDelete, onToggleEnabled, isFirst, isLast, onMoveUp, onMoveDown, isDragOver, onDragStart, onDragOver, onDrop, onDragEnd }: RuleCardProps) {
  const { t } = useTranslation('settings')
  const [collapsed, setCollapsed] = useState(true)
  const hasActions = rule.actions.length > 0

  return (
    <div
      className={`rule-card${rule.enabled ? '' : ' rule-card-disabled'}${isDragOver ? ' rule-card-drop-over' : ''}${collapsed ? ' rule-card-collapsed' : ''}`}
      draggable
      onDragStart={e => { e.dataTransfer.effectAllowed = 'move'; onDragStart() }}
      onDragOver={e => { e.preventDefault(); e.dataTransfer.dropEffect = 'move'; onDragOver() }}
      onDrop={e => { e.preventDefault(); onDrop() }}
      onDragEnd={onDragEnd}
    >
      <div className="rule-card-header">
        <span className="rule-card-drag" title={t('rules.dragToReorder')}><GripIcon /></span>
        <label className="toggle-switch" title={t(rule.enabled ? 'rules.disable' : 'rules.enable')}>
          {/* Named by the rule it switches, never by the action: `checked` already says which way
              it stands, and "Disable, checkbox, checked" says that twice and the rule never. */}
          <input type="checkbox" checked={rule.enabled} onChange={e => onToggleEnabled(e.target.checked)}
            aria-label={rule.name || t('rules.unnamed')} />
          <span className="toggle-track" />
        </label>
        <span className="rule-card-name">{rule.name}</span>
        {collapsed && hasActions && (
          <div className="rule-card-inline-actions">
            {rule.actions.map((a, i) => (
              <span key={i} className="rule-card-pill rule-card-pill-action">{summarizeAction(a, true, t)}</span>
            ))}
          </div>
        )}
        <div className="rule-card-btns">
          <button className="admin-icon-btn" title={t('rules.moveUp')} disabled={isFirst} onClick={e => { e.stopPropagation(); onMoveUp() }}><ArrowUpIcon /></button>
          <button className="admin-icon-btn" title={t('rules.moveDown')} disabled={isLast} onClick={e => { e.stopPropagation(); onMoveDown() }}><ArrowDownIcon /></button>
          <button className="admin-icon-btn" title={t('actions.edit', { ns: 'common' })} onClick={e => { e.stopPropagation(); onEdit() }}><PencilIcon /></button>
          <button className="admin-icon-btn is-danger" title={t('actions.delete', { ns: 'common' })} onClick={e => { e.stopPropagation(); onDelete() }}><TrashIcon size={13} /></button>
          <span className="rule-card-btns-sep" aria-hidden="true" />
          <button className="admin-icon-btn" title={t(collapsed ? 'rules.expand' : 'rules.collapse')} onClick={e => { e.stopPropagation(); setCollapsed(c => !c) }}>
            {collapsed ? <ChevronDownIcon /> : <ChevronUpIcon />}
          </button>
        </div>
      </div>

      {!collapsed && (
        <div className="rule-card-body">
          <div className="rule-card-side">
            <span className="rule-card-badge">{t(rule.matchAll ? 'rules.badgeAll' : 'rules.badgeAny')}</span>
            <div className="rule-card-conditions">
              {rule.conditions.map((c, i) => (
                <span key={i} className="rule-card-pill">{summarizeCondition(c, t)}</span>
              ))}
            </div>
          </div>
          {hasActions && (
            <div className="rule-card-side">
              <div className="rule-card-actions">
                {rule.actions.map((a, i) => (
                  <span key={i} className="rule-card-pill rule-card-pill-action">{summarizeAction(a, true, t)}</span>
                ))}
              </div>
            </div>
          )}
          {rule.stopAfter && <span className="rule-card-stop">{t('rules.stop')}</span>}
        </div>
      )}
    </div>
  )
}

// ── ConditionRow ──────────────────────────────────────────────

interface ConditionRowProps {
  condition: RuleCondition
  onChange: (condition: RuleCondition) => void
  onRemove: () => void
  extended?: boolean
}

export function ConditionRow({ condition, onChange, onRemove, extended = false }: ConditionRowProps) {
  const { t } = useTranslation('settings')
  const availableFields = extended ? CONDITION_FIELDS : CONDITION_FIELDS.filter(f => !f.extendedOnly)
  const fieldDef = CONDITION_FIELDS.find(f => f.value === condition.field)
  const baseOps = extended ? CONDITION_OPERATORS : CONDITION_OPERATORS.filter(o => !o.extendedOnly)
  const fieldOps = fieldDef?.operators
  const availableOperators = fieldOps ? baseOps.filter(o => fieldOps.includes(o.value)) : baseOps
  const isDuplicate = condition.field === 'Duplicate'
  const isDateField = fieldDef?.inputType === 'date'
  const isWeekday = fieldDef?.inputType === 'weekday'
  const isHour = fieldDef?.inputType === 'hour'

  return (
    <div className="rule-row">
      <select
        value={condition.field}
        onChange={e => {
          const newDef = chosen(CONDITION_FIELDS, e.target.value)
          if (!newDef) return
          const newBaseOps = extended ? CONDITION_OPERATORS : CONDITION_OPERATORS.filter(o => !o.extendedOnly)
          const newOps = newDef.operators
          const newAvailOps = newOps ? newBaseOps.filter(o => newOps.includes(o.value)) : newBaseOps
          const opValid = newAvailOps.some(o => o.value === condition.operator)
          onChange({
            ...condition,
            field: newDef.value,
            headerName: null,
            operator: opValid ? condition.operator : (newAvailOps[0]?.value ?? 'Contains'),
            ...(newDef.inputType === 'weekday' && { value: '1,2,3,4,5' }),
          })
        }}
      >
        {availableFields.map(f => <option key={f.value} value={f.value}>{fieldLabel(f.value, t)}</option>)}
      </select>
      {!isDuplicate && !isWeekday && (
        <select
          value={condition.operator}
          onChange={e => {
            const op = chosen(availableOperators, e.target.value)
            if (op) onChange({ ...condition, operator: op.value })
          }}
        >
          {availableOperators.map(o =>
            <option key={o.value} value={o.value}>{operatorLabel(o.value, t)}</option>)}
        </select>
      )}
      {condition.field === 'Header' && (
        <input
          type="text"
          className="rule-row-input"
          placeholder={t('rules.headerNamePlaceholder')}
          value={condition.headerName ?? ''}
          onChange={e => onChange({ ...condition, headerName: e.target.value })}
          style={{ width: '130px', flexShrink: 0 }}
        />
      )}
      {isDuplicate ? (
        <input
          type="number"
          min="1"
          className="rule-row-input"
          placeholder={t('rules.secondsPlaceholder')}
          value={condition.value}
          onChange={e => onChange({ ...condition, value: e.target.value })}
          style={{ flex: 1 }}
        />
      ) : isWeekday ? (
        <select
          value={WEEKDAY_VALUES.includes(condition.value) ? condition.value : WEEKDAY_VALUES[0]}
          onChange={e => onChange({ ...condition, value: e.target.value })}
          style={{ flex: 1 }}
        >
          {WEEKDAY_VALUES.map(v => <option key={v} value={v}>{weekdayLabel(v, t)}</option>)}
        </select>
      ) : isDateField ? (
        <input
          type="date"
          className="rule-row-input"
          value={condition.value ?? ''}
          onChange={e => onChange({ ...condition, value: e.target.value })}
          style={{ flex: 1 }}
        />
      ) : isHour ? (
        <input
          type="number"
          min="0"
          max="23"
          className="rule-row-input"
          placeholder="0–23"
          value={condition.value ?? ''}
          onChange={e => onChange({ ...condition, value: e.target.value })}
          style={{ flex: 1 }}
        />
      ) : (
        <input
          type="text"
          className="rule-row-input"
          placeholder={t('rules.valuePlaceholder')}
          value={condition.value}
          onChange={e => onChange({ ...condition, value: e.target.value })}
          style={{ flex: 1 }}
        />
      )}
      <button className="admin-icon-btn is-danger" type="button" onClick={onRemove}
        title={t('actions.remove', { ns: 'common' })}>
        <TrashIcon size={13} />
      </button>
    </div>
  )
}

// ── ActionRow ─────────────────────────────────────────────────

interface ActionRowProps {
  action: RuleAction
  onChange: (action: RuleAction) => void
  onRemove: () => void
  foldersDatalistId?: string
  extended?: boolean
}

export function ActionRow({ action, onChange, onRemove, foldersDatalistId, extended = false }: ActionRowProps) {
  const { t } = useTranslation('settings')
  const availableTypes = extended ? ACTION_TYPES : ACTION_TYPES.filter(t => !t.extendedOnly)
  // ACTION_TYPES always has entries with no extendedOnly flag, so availableTypes is never empty.
  const def = availableTypes.find(t => t.value === action.type) ?? availableTypes[0]!
  return (
    <div className="rule-row">
      <select
        value={action.type}
        onChange={e => {
          const next = chosen(availableTypes, e.target.value)
          if (next) onChange({ type: next.value, argument: '' })
        }}
      >
        {availableTypes.map(type =>
          <option key={type.value} value={type.value}>{actionTypeLabel(type.value, t)}</option>)}
      </select>
      {def.hasArg && (
        <input
          type="text"
          className="rule-row-input"
          placeholder={actionArgPlaceholder(def.value, t)}
          value={action.argument ?? ''}
          onChange={e => onChange({ ...action, argument: e.target.value })}
          list={action.type === 'FileInto' && foldersDatalistId ? foldersDatalistId : undefined}
          style={{ flex: 1 }}
        />
      )}
      {action.type === 'FileInto' && extended && (
        <label style={{ display: 'flex', alignItems: 'center', gap: '4px', fontSize: '12px', whiteSpace: 'nowrap', flexShrink: 0 }}>
          <input
            type="checkbox"
            checked={action.autoCreate ?? false}
            onChange={e => onChange({ ...action, autoCreate: e.target.checked })}
          />
          {t('rules.autoCreate')}
        </label>
      )}
      <button className="admin-icon-btn is-danger" type="button" onClick={onRemove}
        title={t('actions.remove', { ns: 'common' })}>
        <TrashIcon size={13} />
      </button>
    </div>
  )
}

// ── RuleHelpModal ─────────────────────────────────────────────

// The badge is an inline element inside one description, so it travels as a component too.
const HELP_TAGS = { code: <code />, em: <em />, badge: <span className="rule-help-badge" /> }

function Badge() {
  const { t } = useTranslation('settings')
  return <span className="rule-help-badge">{t('rules.help.badge')}</span>
}

function RuleHelpModal({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation('settings')
  // Built from the keys the dropdowns themselves read: restating the wording here is how the
  // help dialog ends up contradicting the control it documents.
  const pair = (a: string, b: string) => t('rules.help.termPair', { a, b })

  return (
    <Modal title={t('rules.help.title')} onClose={onClose} className="rule-help-modal">
      <div className="rule-help-body">

        <section className="rule-help-section">
          <h3 className="rule-help-heading">{t('rules.stepConditions')}</h3>
          <dl className="rule-help-dl">
            <dt>{t('rules.help.headerTerms')}</dt>
            <dd><Trans i18nKey="rules.help.headerDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{t('rules.fields.Header')}</dt>
            <dd><Trans i18nKey="rules.help.customHeaderDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{t('rules.help.sizeTerm')}</dt>
            <dd><Trans i18nKey="rules.help.sizeDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{t('rules.fields.Body')} <Badge /></dt>
            <dd><Trans i18nKey="rules.help.bodyDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{pair(t('rules.fields.EnvelopeFrom'), t('rules.fields.EnvelopeTo'))} <Badge /></dt>
            <dd><Trans i18nKey="rules.help.envelopeDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{t('rules.fields.RecipientDetail')} <Badge /></dt>
            <dd><Trans i18nKey="rules.help.detailDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{t('rules.help.duplicateTerm')} <Badge /></dt>
            <dd><Trans i18nKey="rules.help.duplicateDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{pair(t('rules.fields.CurrentDate'), t('rules.fields.MessageDate'))} <Badge /></dt>
            <dd><Trans i18nKey="rules.help.dateDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{pair(t('rules.fields.CurrentWeekday'), t('rules.fields.CurrentHour'))} <Badge /></dt>
            <dd><Trans i18nKey="rules.help.whenDesc" ns="settings" components={HELP_TAGS} /></dd>
          </dl>
        </section>

        <section className="rule-help-section">
          <h3 className="rule-help-heading">{t('rules.help.operators')}</h3>
          <dl className="rule-help-dl">
            <dt>{t('rules.operators.Contains')}</dt>
            <dd><Trans i18nKey="rules.help.containsDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{t('rules.operators.Equals')}</dt>
            <dd><Trans i18nKey="rules.help.equalsDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{t('rules.operators.Matches')}</dt>
            <dd><Trans i18nKey="rules.help.wildcardDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{t('rules.operators.Regex')} <Badge /></dt>
            <dd><Trans i18nKey="rules.help.regexDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{pair(t('rules.operators.Larger'), t('rules.operators.Smaller'))}</dt>
            <dd><Trans i18nKey="rules.help.sizeCompareDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{pair(t('rules.operators.Before'), t('rules.operators.OnOrAfter'))}</dt>
            <dd><Trans i18nKey="rules.help.dateCompareDesc" ns="settings" components={HELP_TAGS} /></dd>
          </dl>
        </section>

        <section className="rule-help-section">
          <h3 className="rule-help-heading">{t('rules.stepActions')}</h3>
          <dl className="rule-help-dl">
            <dt>{t('rules.actionTypes.FileInto')}</dt>
            <dd><Trans i18nKey="rules.help.fileIntoDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{t('rules.actionTypes.Redirect')}</dt>
            <dd><Trans i18nKey="rules.help.redirectDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{t('rules.actionTypes.Reject')}</dt>
            <dd><Trans i18nKey="rules.help.rejectDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{t('rules.actionTypes.Discard')}</dt>
            <dd><Trans i18nKey="rules.help.discardDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{t('rules.actionTypes.Keep')} <Badge /></dt>
            <dd><Trans i18nKey="rules.help.keepDesc" ns="settings" components={HELP_TAGS} /></dd>
          </dl>
        </section>

        <section className="rule-help-section">
          <h3 className="rule-help-heading">{t('rules.stepOptions')}</h3>
          <dl className="rule-help-dl">
            <dt>{t('rules.markAsRead')}</dt>
            <dd><Trans i18nKey="rules.help.markReadDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{t('rules.markAsFlagged')} <Badge /></dt>
            <dd><Trans i18nKey="rules.help.markFlaggedDesc" ns="settings" components={HELP_TAGS} /></dd>
            <dt>{t('rules.stopAfter')}</dt>
            <dd><Trans i18nKey="rules.help.stopAfterDesc" ns="settings" components={HELP_TAGS} /></dd>
          </dl>
        </section>

      </div>
    </Modal>
  )
}

// ── RuleEditorModal ───────────────────────────────────────────

interface RuleEditorModalProps {
  /** Null opens the editor on a new rule. */
  rule: SieveRuleWrite | null
  onSave: (rule: RuleDraft) => void
  onClose: () => void
  extended?: boolean
}

export function RuleEditorModal({ rule: initialRule, onSave, onClose, extended = false }: RuleEditorModalProps) {
  const { t } = useTranslation('settings')
  const isNew = !initialRule
  const accountId = useAccountId()
  const nameRef = useRef<HTMLInputElement>(null)
  const [rule, setRule] = useState<RuleDraft>(() => {
    const base: RuleDraft = initialRule
      ? { ...initialRule, conditions: initialRule.conditions.map(c => ({ ...c })), actions: initialRule.actions.map(a => ({ ...a })) }
      : makeEmptyRule()
    return { ...base, actions: base.actions.filter(a => a.type !== 'SetFlag') }
  })
  const [markAsRead, setMarkAsRead] = useState(() =>
    initialRule?.actions?.some(a => a.type === 'SetFlag' && (a.argument === '\\Seen' || a.argument === '\\\\Seen')) ?? false
  )
  const [markAsFlagged, setMarkAsFlagged] = useState(() =>
    initialRule?.actions?.some(a => a.type === 'SetFlag' && (a.argument === '\\Flagged' || a.argument === '\\\\Flagged')) ?? false
  )
  const [error, setError] = useState<string | null>(null)
  const [folders, setFolders] = useState<string[]>([])
  const [helpOpen, setHelpOpen] = useState(false)

  const step1Done = rule.name.trim() !== ''
  const step2Done = rule.conditions.length > 0 && rule.conditions.every(isConditionValid)
  const step3Done = rule.actions.length > 0 && rule.actions.every(isActionValid)
  const step2Unlocked = step1Done
  const step3Unlocked = step1Done && step2Done
  const step4Unlocked = step1Done && step2Done && step3Done
  const canSubmit = step1Done && step2Done && step3Done

  function circleClass(isUnlocked: boolean, isDone: boolean) {
    if (!isUnlocked) return 'rule-wizard-circle rule-wizard-circle--locked'
    if (!isDone)     return 'rule-wizard-circle rule-wizard-circle--active'
    return 'rule-wizard-circle'
  }

  // The rule is filed into the active mailbox, so the picker must offer that mailbox's folders:
  // /api/Account/Folders answers the primary's whatever the header says. Containers cannot hold
  // mail, so a rule naming one would file nowhere.
  useEffect(() => {
    api.getMailFolders({ accountId })
      .then(tree => setFolders(
        Array.isArray(tree) ? flatten(tree).filter(f => f.node.selectable).map(f => f.node.path) : []))
      .catch(() => {})
  }, [accountId])

  function setField<K extends keyof RuleDraft>(key: K, value: RuleDraft[K]) {
    setRule(r => ({ ...r, [key]: value }))
  }

  function updateCondition(i: number, cond: RuleCondition) {
    setRule(r => { const c = [...r.conditions]; c[i] = cond; return { ...r, conditions: c } })
  }
  function removeCondition(i: number) {
    setRule(r => ({ ...r, conditions: r.conditions.filter((_, idx) => idx !== i) }))
  }
  function addCondition() {
    setRule(r => ({
      ...r,
      conditions: [...r.conditions, { field: 'Subject', operator: 'Contains', value: '', headerName: null }]
    }))
  }

  function updateAction(i: number, action: RuleAction) {
    setRule(r => { const a = [...r.actions]; a[i] = action; return { ...r, actions: a } })
  }
  function removeAction(i: number) {
    setRule(r => ({ ...r, actions: r.actions.filter((_, idx) => idx !== i) }))
  }
  function addAction() {
    setRule(r => ({ ...r, actions: [...r.actions, { type: 'FileInto', argument: '' }] }))
  }

  function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (!rule.name.trim()) { setError(t('rules.nameRequired')); return }
    if (rule.conditions.length === 0) { setError(t('rules.conditionRequired')); return }
    if (rule.actions.length === 0) { setError(t('rules.actionRequired')); return }
    setError(null)
    const flag = (argument: string): RuleAction => ({ type: 'SetFlag', argument })
    const flagActions = [
      ...(markAsRead    ? [flag('\\Seen')]    : []),
      ...(markAsFlagged ? [flag('\\Flagged')] : []),
    ]
    onSave({ ...rule, actions: [...flagActions, ...rule.actions] })
  }

  return (
    <Modal title={t(isNew ? 'rules.newRule' : 'rules.editRule')} onClose={onClose}
      onSubmit={handleSubmit} initialFocusRef={nameRef}
      headerExtra={
        <button type="button" className="rule-help-btn" onClick={() => setHelpOpen(true)}
          aria-label={t('rules.helpTitle')} title={t('rules.helpTitle')}>?</button>
      }>
      {helpOpen && <RuleHelpModal onClose={() => setHelpOpen(false)} />}
      {error && <div className="alert alert-error" role="alert" style={{ marginBottom: '16px' }}>{error}</div>}

      {folders.length > 0 && (
        <datalist id="rule-editor-folders">
          {folders.map(f => <option key={f} value={f} />)}
        </datalist>
      )}

      <div className="rule-wizard">

        <div className="rule-wizard-step">
          <div className="rule-wizard-indicator">
            <div className={circleClass(true, step1Done)}>1</div>
            <div className="rule-wizard-line" />
          </div>
          <div className="rule-wizard-body">
            <div className="rule-wizard-title">{t('rules.stepName')}</div>
            <input
              type="text"
              className="rule-wizard-input"
              value={rule.name}
              onChange={e => setField('name', e.target.value)}
              ref={nameRef}
              required
            />
          </div>
        </div>

        <div className="rule-wizard-step">
          <div className="rule-wizard-indicator">
            <div className={circleClass(step2Unlocked, step2Done)}>2</div>
            <div className="rule-wizard-line" />
          </div>
          <div className={`rule-wizard-body${step2Unlocked ? '' : ' rule-wizard-body--locked'}`}>
            <div className="rule-wizard-step-header">
              <span className="rule-wizard-title">{t('rules.stepConditions')}</span>
              <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                <select
                  className="rule-wizard-select"
                  value={rule.matchAll ? 'all' : 'any'}
                  onChange={e => setField('matchAll', e.target.value === 'all')}
                >
                  <option value="any">{t('rules.anyOf')}</option>
                  <option value="all">{t('rules.allOf')}</option>
                </select>
                <button type="button" className="rule-editor-add-btn" onClick={addCondition}>
                  <PlusIcon /> {t('actions.add', { ns: 'common' })}
                </button>
              </div>
            </div>
            {rule.conditions.map((c, i) => (
              <ConditionRow key={i} condition={c}
                onChange={cond => updateCondition(i, cond)}
                onRemove={() => removeCondition(i)}
                extended={extended} />
            ))}
            {rule.conditions.length === 0 && (
              <p className="rule-editor-empty">{t('rules.noConditions')}</p>
            )}
            <p className="rule-wizard-hint">
              <Trans i18nKey="rules.matchHint" ns="settings" />
            </p>
          </div>
        </div>

        <div className="rule-wizard-step">
          <div className="rule-wizard-indicator">
            <div className={circleClass(step3Unlocked, step3Done)}>3</div>
            <div className="rule-wizard-line" />
          </div>
          <div className={`rule-wizard-body${step3Unlocked ? '' : ' rule-wizard-body--locked'}`}>
            <div className="rule-wizard-step-header">
              <span className="rule-wizard-title">{t('rules.stepActions')}</span>
              {(extended || rule.actions.length === 0) && (
                <button type="button" className="rule-editor-add-btn" onClick={addAction}>
                  <PlusIcon /> {t('actions.add', { ns: 'common' })}
                </button>
              )}
            </div>
            {rule.actions.map((a, i) => (
              <ActionRow key={i} action={a}
                extended={extended}
                onChange={action => updateAction(i, action)}
                onRemove={() => removeAction(i)}
                foldersDatalistId={folders.length > 0 ? 'rule-editor-folders' : undefined} />
            ))}
            {rule.actions.length === 0 && (
              <p className="rule-editor-empty">{t('rules.noActions')}</p>
            )}
          </div>
        </div>

        <div className="rule-wizard-step">
          <div className="rule-wizard-indicator">
            <div className={circleClass(step4Unlocked, step4Unlocked)}>4</div>
          </div>
          <div className={`rule-wizard-body${step4Unlocked ? '' : ' rule-wizard-body--locked'}`}>
            <div className="rule-wizard-title">{t('rules.stepOptions')}</div>
            <div className="rule-wizard-toggle-row">
              <label className="toggle-switch">
                <input type="checkbox" checked={markAsRead}
                  onChange={e => setMarkAsRead(e.target.checked)} aria-label={t('rules.markAsRead')} />
                <span className="toggle-track" />
              </label>
              <span className="rule-wizard-toggle-label">{t('rules.markAsRead')}</span>
            </div>
            {extended && (
              <div className="rule-wizard-toggle-row">
                <label className="toggle-switch">
                  <input type="checkbox" checked={markAsFlagged}
                    onChange={e => setMarkAsFlagged(e.target.checked)} aria-label={t('rules.markAsFlagged')} />
                  <span className="toggle-track" />
                </label>
                <span className="rule-wizard-toggle-label">{t('rules.markAsFlagged')}</span>
              </div>
            )}
            <div className="rule-wizard-toggle-row">
              <label className="toggle-switch">
                <input type="checkbox" checked={rule.stopAfter}
                  onChange={e => setField('stopAfter', e.target.checked)} aria-label={t('rules.stopAfter')} />
                <span className="toggle-track" />
              </label>
              <span className="rule-wizard-toggle-label">
                {t('rules.stopAfter')}
                <span className="rule-wizard-hint rule-wizard-hint--inline">{t('rules.stopAfterHint')}</span>
              </span>
            </div>
          </div>
        </div>

      </div>

      <div style={{ display: 'flex', gap: '8px', justifyContent: 'flex-end', marginTop: '24px' }}>
        <button type="submit" className="btn btn-primary" style={{ width: 'auto' }} disabled={!canSubmit}>
          {isNew ? t('rules.createRule') : t('actions.saveChanges', { ns: 'common' })}
        </button>
      </div>
    </Modal>
  )
}

// ── ConvertConfirmModal (weesky → rainloop, lists rules that will be lost) ──

interface ConvertConfirmModalProps {
  incompatible: IncompatibleRule[]
  onConfirm: () => void
  onClose: () => void
  loading?: boolean
}

export function ConvertConfirmModal({ incompatible, onConfirm, onClose, loading }: ConvertConfirmModalProps) {
  const { t } = useTranslation('settings')
  return (
    <Modal role="alertdialog" title={t('rules.convertTitle')} onClose={onClose} busy={loading}>
      <p style={{ margin: '0 0 12px', fontSize: '14px' }}>
        <Trans i18nKey="rules.convertBody" ns="settings" count={incompatible.length} />
      </p>
      <ul className="convert-lost-list">
        {incompatible.map(r => (
          <li key={r.id}>
            <span className="convert-lost-name">{r.name || t('rules.unnamed')}</span>
            <span className="convert-lost-reason">{r.reason}</span>
          </li>
        ))}
      </ul>
      <div style={{ display: 'flex', gap: '8px', justifyContent: 'flex-end', marginTop: '20px' }}>
        <button className="btn btn-primary"
          style={{ width: 'auto', background: 'var(--danger)', borderColor: 'var(--danger)' }}
          onClick={onConfirm} disabled={loading}>
          {loading ? <span className="spinner" /> : t('rules.deleteAndSwitch')}
        </button>
      </div>
    </Modal>
  )
}

// ── RulesPage ─────────────────────────────────────────────────

/** Writes in flight, counted per account: an account stays busy until its own last write returns. */
function useWritesInFlight(active: string) {
  const [counts, setCounts] = useState<ReadonlyMap<string, number>>(() => new Map())
  const shift = (account: string, by: number) => setCounts(prev => {
    const next = new Map(prev)
    const count = (prev.get(account) ?? 0) + by
    if (count > 0) next.set(account, count)
    else next.delete(account)
    return next
  })
  return {
    busy: counts.has(active),
    begin: (account: string) => shift(account, 1),
    end: (account: string) => shift(account, -1),
  }
}

export default function RulesPage() {
  const { t } = useTranslation('settings')
  const { toasts, addToast, removeToast, pauseToast, resumeToast } = useToasts()
  // The script belongs to the active mailbox: the backend swaps the ManageSieve target on it.
  const accountId = useAccountId()

  const queryClient = useQueryClient()
  // Keyed by account, so the set on screen is the active account's or none: another account's
  // never shows, not even for the render after a switch. Read once per visit, as the effect it
  // replaced: gcTime 0 drops it on leaving, and 'static' stops every automatic refetch — focus,
  // reconnect — which a failed load (no data, so stale whatever the staleTime) would otherwise get.
  const rulesQuery = useQuery({
    queryKey: rulesKey(accountId),
    queryFn: async ({ signal }): Promise<LoadedRuleSet> => {
      try {
        return await api.getRules({ accountId, signal })
      } catch (err) {
        // Aborted: the account was left, and its failure is no longer this screen's news.
        if (!signal.aborted) addToast(extractError(err) || i18next.t('settings:rules.loadFailed'), 'error')
        throw err
      }
    },
    gcTime: 0,
    staleTime: 'static',
    retry: false,
  })
  const ruleSet = rulesQuery.data
  const rules = ruleSet?.rules ?? []
  const loading = rulesQuery.isPending
  // A write's outcome belongs to the account it was made for: its toast and its busy state
  // show only while that account is the active one, never over the account that replaced it.
  const activeAccount = useRef(accountId)
  useLayoutEffect(() => { activeAccount.current = accountId })
  function toastFor(account: string, ...toast: Parameters<AddToast>) {
    if (account === activeAccount.current) addToast(...toast)
  }
  const savingWrites = useWritesInFlight(accountId)
  const deletingWrites = useWritesInFlight(accountId)
  const switchingWrites = useWritesInFlight(accountId)
  const saving = savingWrites.busy
  const deleting = deletingWrites.busy
  const switching = switchingWrites.busy

  // Every dialog belongs to the account it was opened under, and closes with a switch.
  // The editor: undefined is closed, null is open on a new rule.
  const [ruleToEdit, setRuleToEdit] = useKeyedState<SieveRuleWrite | null | undefined>(() => undefined, accountId)
  const [ruleToDelete, setRuleToDelete] = useKeyedState<SieveRuleWrite | null>(() => null, accountId)
  const [confirmDeleteAll, setConfirmDeleteAll] = useKeyedState(() => false, accountId)
  // Tagged too: a compatibility check still in flight at the switch answers afterwards.
  const [conversion, setConversion] = useKeyedState<PendingConversion | null>(() => null, accountId)
  const pendingConversion = conversion?.accountId === accountId ? conversion.incompatible : null

  // Slider ON = extended (Weesky provider); OFF = Rainloop (Snappymail interop).
  const extended = ruleSet?.providerId === 'weesky'

  // A write refuses until the active account's own set has arrived: while it loads, and after a
  // failed load, there is nothing on screen that is that account's to write.
  function belongsToActiveAccount() {
    if (ruleSet) return true
    addToast(t('rules.wrongAccount'), 'error')
    return false
  }

  // Onto the account the write was made for: a write that returns after a switch patches that
  // account's set, which is no longer on screen, and never the one that replaced it.
  function patchLoaded(patch: Partial<LoadedRuleSet>) {
    queryClient.setQueryData<LoadedRuleSet>(rulesKey(accountId), prev => prev && { ...prev, ...patch })
  }

  async function persistRules(updatedRules: SieveRuleWrite[]) {
    if (!belongsToActiveAccount()) return
    savingWrites.begin(accountId)
    try {
      await api.saveRules(updatedRules, ruleSet?.providerId, ruleSet?.scriptName, { accountId })
      patchLoaded({ rules: updatedRules })
      toastFor(accountId, t('rules.saved'))
    } catch (err) {
      toastFor(accountId, extractError(err) || t('rules.saveFailed'), 'error')
    } finally {
      savingWrites.end(accountId)
    }
  }

  async function handleDeleteAll() {
    if (!belongsToActiveAccount()) return
    deletingWrites.begin(accountId)
    try {
      await api.deleteRules({ accountId })
      patchLoaded({ kind: 'Structured', rules: [] })
      setConfirmDeleteAll(false)
      toastFor(accountId, t('rules.scriptDeleted'))
    } catch (err) {
      toastFor(accountId, extractError(err) || t('rules.deleteScriptFailed'), 'error')
    } finally {
      deletingWrites.end(accountId)
    }
  }

  // Switch provider by recompiling the current rules with the target provider. We pass a null
  // script name so the backend writes to the target provider's default script (and cleans up
  // the old one). Then we reflect the new providerId locally so the slider/editor track it.
  async function switchToProvider(targetProviderId: string, rulesToSave: SieveRuleWrite[]) {
    if (!belongsToActiveAccount()) return
    switchingWrites.begin(accountId)
    try {
      await api.saveRules(rulesToSave, targetProviderId, null, { accountId })
      patchLoaded({ rules: rulesToSave, providerId: targetProviderId, scriptName: null })
      toastFor(accountId, t(targetProviderId === 'weesky' ? 'rules.extendedEnabled' : 'rules.switchedRainloop'))
    } catch (err) {
      toastFor(accountId, extractError(err) || t('rules.switchFailed'), 'error')
    } finally {
      switchingWrites.end(accountId)
    }
  }

  async function handleToggleExtended(nextExtended: boolean) {
    if (nextExtended === extended || switching || saving) return
    if (nextExtended) {
      // rainloop → weesky: Weesky is a superset, lossless. No confirmation needed.
      await switchToProvider('weesky', rules)
      return
    }
    // weesky → rainloop: preview which rules the Rainloop format can't keep.
    switchingWrites.begin(accountId)
    let res: CompatibilityCheckResult | undefined
    try {
      res = await api.checkCompatibility('rainloop', rules, { accountId })
    } catch (err) {
      toastFor(accountId, extractError(err) || t('rules.compatFailed'), 'error')
      return
    } finally {
      switchingWrites.end(accountId)
    }
    if (res?.compatible) await switchToProvider('rainloop', rules)
    else setConversion({ accountId, incompatible: res?.incompatible ?? [] })
  }

  async function handleConfirmConversion() {
    const lostIds = new Set((pendingConversion ?? []).map(r => r.id))
    const kept = rules.filter(r => !lostIds.has(r.id))
    setConversion(null)
    await switchToProvider('rainloop', kept)
  }

  function handleSaveRule(rule: RuleDraft) {
    const updated = ruleToEdit === null
      ? [...rules, { ...rule, id: crypto.randomUUID() }]
      : rules.map(r => r.id === rule.id ? { ...rule, id: r.id } : r)
    setRuleToEdit(undefined)
    void persistRules(updated)
  }

  function handleToggleEnabled(index: number, enabled: boolean) {
    void persistRules(rules.map((r, i) => i === index ? { ...r, enabled } : r))
  }

  function handleDeleteRule() {
    if (!ruleToDelete) return
    const updated = rules.filter(r => r.id !== ruleToDelete.id)
    setRuleToDelete(null)
    void persistRules(updated)
  }

  function handleMoveUp(index: number) {
    if (index === 0) return
    const u = [...rules];
    // index is in [1, rules.length - 1] here, so both u[index - 1] and u[index] exist.
    [u[index - 1], u[index]] = [u[index]!, u[index - 1]!]
    void persistRules(u)
  }

  function handleMoveDown(index: number) {
    if (index === rules.length - 1) return
    const u = [...rules];
    // index is in [0, rules.length - 2] here, so both u[index] and u[index + 1] exist.
    [u[index], u[index + 1]] = [u[index + 1]!, u[index]!]
    void persistRules(u)
  }

  // The dragged rule's id, not its index: a card stays draggable while a save from elsewhere
  // (a delete, another tab) is in flight, and `rules` can shrink or reorder under the drag before
  // the drop. An index captured at drag start would then resolve to whatever rule now sits there —
  // the wrong rule, moved and saved without anyone catching it — or past the end.
  const dragIdRef = useRef<string | null>(null)
  // A confirmed delete takes the card's own button — or the whole Advanced notice — with it, so
  // focus goes back to the page's name.
  const headingRef = useRef<HTMLHeadingElement>(null)
  const [dropIndex, setDropIndex] = useState<number | null>(null)

  function handleDrop(index: number) {
    const id = dragIdRef.current
    dragIdRef.current = null
    setDropIndex(null)
    if (id === null) return
    const from = rules.findIndex(r => r.id === id)
    // Gone: the drag started on a rule a concurrent save has since removed. Nothing to move.
    if (from === -1 || from === index) return
    const u = [...rules]
    const [moved] = u.splice(from, 1)
    u.splice(index, 0, moved!)
    // The shrink between drag start and drop can also leave the order exactly as it was.
    // u is rules with one item spliced out and back in, so the two are always the same length.
    if (u.every((r, i) => r.id === rules[i]!.id)) return
    void persistRules(u)
  }

  function handleDragEnd() {
    dragIdRef.current = null
    setDropIndex(null)
  }

  const providerLabel = ruleSet?.providerId === 'rainloop' ? 'Rainloop'
    : ruleSet?.providerId === 'weesky' ? 'Weesky'
    : null

  return (
    <>
      <div className="settings-page">
        <div className="settings-page-header">
          <h1 className="settings-page-title" ref={headingRef} tabIndex={-1}>
            <FunnelIcon size={17} />
            {t('nav.rules')}
            {providerLabel && <span className="provider-badge">{providerLabel}</span>}
          </h1>
        </div>
        <div className="rules-modal-body">
            <p className="rules-modal-desc">{t('rules.intro')}</p>

            {loading ? (
              <div className="loading-center"><span className="spinner" /></div>
            ) : !ruleSet ? (
              <ListLoadFailed>{t('rules.loadFailedBody')}</ListLoadFailed>
            ) : ruleSet.kind === 'Advanced' ? (
              <div className="rules-notice">
                <p>{t('rules.advancedUnparsable')}</p>
                <p>{t('rules.advancedDeleteHint')}</p>
                <div style={{ marginTop: '16px' }}>
                  <button className="btn"
                    style={{ width: 'auto', color: 'var(--danger)', borderColor: 'var(--danger)', border: '1px solid' }}
                    onClick={() => setConfirmDeleteAll(true)}>
                    {t('rules.deleteScript')}
                  </button>
                </div>
              </div>
            ) : (
              <div>
                <div className="rules-toolbar">
                  <span className="rules-count">
                    {t('rules.count', { count: rules.length })}
                    {saving && <span className="spinner" style={{ marginLeft: '8px' }} />}
                  </span>
                  <div className="extended-rules-toggle" style={{ marginLeft: 'auto', display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <label className="toggle-switch" title={t('rules.extended')}>
                      <input
                        type="checkbox"
                        checked={extended}
                        disabled={switching || saving}
                        onChange={e => void handleToggleExtended(e.target.checked)}
                        aria-label={t('rules.extended')}
                      />
                      <span className="toggle-track" />
                    </label>
                    <span className="rule-wizard-toggle-label">{t('rules.extended')}</span>
                    <HelpTooltip text={t('rules.extendedHelp')} />
                    {switching && <span className="spinner" />}
                  </div>
                  <button
                    className="btn btn-primary"
                    style={{ width: 'auto', display: 'inline-flex', alignItems: 'center', gap: '6px', marginLeft: '12px' }}
                    onClick={() => setRuleToEdit(null)}
                  >
                    <PlusIcon /> {t('rules.newRule')}
                  </button>
                </div>

                {rules.length === 0 ? (
                  <div className="rules-empty">
                    <Trans i18nKey="rules.empty" ns="settings" />
                  </div>
                ) : (
                  <div className="rules-list">
                    {rules.map((rule, i) => (
                      <RuleCard
                        key={rule.id ?? i}
                        rule={rule}
                        isFirst={i === 0}
                        isLast={i === rules.length - 1}
                        onEdit={() => setRuleToEdit(rule)}
                        onDelete={() => setRuleToDelete(rule)}
                        onToggleEnabled={enabled => handleToggleEnabled(i, enabled)}
                        onMoveUp={() => handleMoveUp(i)}
                        onMoveDown={() => handleMoveDown(i)}
                        isDragOver={dropIndex === i}
                        onDragStart={() => { dragIdRef.current = rule.id }}
                        onDragOver={() => { if (dragIdRef.current !== null && dragIdRef.current !== rule.id) setDropIndex(i) }}
                        onDrop={() => handleDrop(i)}
                        onDragEnd={handleDragEnd}
                      />
                    ))}
                  </div>
                )}
              </div>
            )}
        </div>
      </div>

      {ruleToEdit !== undefined && (
        <RuleEditorModal
          rule={ruleToEdit}
          extended={extended}
          onSave={handleSaveRule}
          onClose={() => setRuleToEdit(undefined)}
        />
      )}

      {pendingConversion && (
        <ConvertConfirmModal
          incompatible={pendingConversion}
          onConfirm={() => void handleConfirmConversion()}
          onClose={() => setConversion(null)}
          loading={switching}
        />
      )}

      {ruleToDelete && (
        <DeleteConfirmModal
          entityLabel={t('rules.ruleEntity', { name: ruleToDelete.name })}
          onConfirm={handleDeleteRule}
          onClose={() => setRuleToDelete(null)}
          loading={deleting}
          returnFocusRef={headingRef}
        />
      )}

      {confirmDeleteAll && (
        <DeleteConfirmModal
          entityLabel={t('rules.scriptEntity')}
          onConfirm={() => void handleDeleteAll()}
          onClose={() => setConfirmDeleteAll(false)}
          loading={deleting}
          returnFocusRef={headingRef}
        />
      )}

      <Toasts toasts={toasts} onRemove={removeToast} onPause={pauseToast} onResume={resumeToast} />
    </>
  )
}
