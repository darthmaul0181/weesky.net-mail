import { useState, useEffect, useCallback, useRef } from 'react'
import { api } from '../../../api.js'
import { useToasts } from '../../../hooks/useToasts.js'
import Toasts from '../../../components/Toasts.jsx'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal.jsx'
import HelpTooltip from '../../../components/HelpTooltip.jsx'
import TrashIcon from '../../../icons/TrashIcon.jsx'
import PencilIcon from '../../../icons/PencilIcon.jsx'

// ── Icons ─────────────────────────────────────────────────────

function PlusIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
      <line x1="12" y1="5" x2="12" y2="19" />
      <line x1="5" y1="12" x2="19" y2="12" />
    </svg>
  )
}

function ChevronUpIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width="13" height="13" viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="18 15 12 9 6 15" />
    </svg>
  )
}

function ChevronDownIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width="13" height="13" viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="6 9 12 15 18 9" />
    </svg>
  )
}

function ArrowUpIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width="13" height="13" viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
      <line x1="12" y1="19" x2="12" y2="5" />
      <polyline points="5 12 12 5 19 12" />
    </svg>
  )
}

function ArrowDownIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width="13" height="13" viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
      <line x1="12" y1="5" x2="12" y2="19" />
      <polyline points="19 12 12 19 5 12" />
    </svg>
  )
}

function GripIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="currentColor">
      <circle cx="8"  cy="6"  r="2" /><circle cx="16" cy="6"  r="2" />
      <circle cx="8"  cy="12" r="2" /><circle cx="16" cy="12" r="2" />
      <circle cx="8"  cy="18" r="2" /><circle cx="16" cy="18" r="2" />
    </svg>
  )
}

// Sliders icon — the conventional "filters / rules" control (used in the side
// menu and next to the Rules popup title). Exported so AliasesPage can reuse it.
export function RulesIcon({ size = 15 }) {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width={size} height={size} viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <line x1="21" y1="4"  x2="14" y2="4" />
      <line x1="10" y1="4"  x2="3"  y2="4" />
      <line x1="21" y1="12" x2="12" y2="12" />
      <line x1="8"  y1="12" x2="3"  y2="12" />
      <line x1="21" y1="20" x2="16" y2="20" />
      <line x1="12" y1="20" x2="3"  y2="20" />
      <line x1="14" y1="2"  x2="14" y2="6" />
      <line x1="8"  y1="10" x2="8"  y2="14" />
      <line x1="16" y1="18" x2="16" y2="22" />
    </svg>
  )
}

const EXTENDED_RULES_HELP =
  'Rules created in extended mode are not compatible with the Rainloop rules editor and will no longer be visible there.'

// ── Constants ─────────────────────────────────────────────────

const TEXT_OPS = ['Contains', 'NotContains', 'Equals', 'NotEquals', 'Regex']

const CONDITION_FIELDS = [
  { value: 'From',            label: 'From',              operators: TEXT_OPS },
  { value: 'Recipient',       label: 'To / Cc',           operators: TEXT_OPS },
  { value: 'Subject',         label: 'Subject',           operators: TEXT_OPS },
  { value: 'Header',          label: 'Custom header',     operators: TEXT_OPS },
  { value: 'Size',            label: 'Size (bytes)',       operators: ['Larger', 'Smaller'] },
  { value: 'Body',            label: 'Body',              extendedOnly: true, operators: ['Contains', 'NotContains', 'Regex'] },
  { value: 'EnvelopeFrom',    label: 'Envelope from',     extendedOnly: true, operators: TEXT_OPS },
  { value: 'EnvelopeTo',      label: 'Envelope to',       extendedOnly: true, operators: TEXT_OPS },
  { value: 'RecipientDetail', label: 'Recipient +detail', extendedOnly: true, operators: TEXT_OPS },
  { value: 'Duplicate',       label: 'Duplicate message', extendedOnly: true, noOperator: true },
  { value: 'CurrentDate',    label: 'Current date',      extendedOnly: true, operators: ['Before', 'OnOrAfter', 'Equals'], inputType: 'date' },
  { value: 'MessageDate',    label: 'Message date',      extendedOnly: true, operators: ['Before', 'OnOrAfter', 'Equals'], inputType: 'date' },
  { value: 'CurrentWeekday', label: 'Current weekday',   extendedOnly: true, noOperator: true, inputType: 'weekday' },
  { value: 'CurrentHour',    label: 'Current hour',      extendedOnly: true, operators: ['Before', 'OnOrAfter', 'Equals'], inputType: 'hour' },
]

const WEEKDAY_OPTIONS = [
  { value: '1,2,3,4,5', label: 'Weekday (Mon–Fri)' },
  { value: '0,6',       label: 'Weekend (Sat–Sun)' },
  { value: '1', label: 'Monday' },
  { value: '2', label: 'Tuesday' },
  { value: '3', label: 'Wednesday' },
  { value: '4', label: 'Thursday' },
  { value: '5', label: 'Friday' },
  { value: '6', label: 'Saturday' },
  { value: '0', label: 'Sunday' },
]

const CONDITION_OPERATORS = [
  { value: 'Contains',    label: 'contains' },
  { value: 'NotContains', label: 'not contains' },
  { value: 'Equals',      label: 'equals' },
  { value: 'NotEquals',   label: 'not equal to' },
  { value: 'Matches',     label: 'matches (wildcard)' }, // kept for display of legacy rules only
  { value: 'Regex',       label: 'matches (regex)' },
  { value: 'Larger',      label: 'is larger than' },
  { value: 'Smaller',     label: 'is smaller than' },
  { value: 'Before',      label: 'is before',         extendedOnly: true },
  { value: 'OnOrAfter',   label: 'is on or after',    extendedOnly: true },
]

const ACTION_TYPES = [
  { value: 'FileInto', label: 'Move to',             hasArg: true,  argPlaceholder: 'Folder name' },
  { value: 'Redirect', label: 'Redirect to',         hasArg: true,  argPlaceholder: 'email@address.com' },
  { value: 'Discard',  label: 'Discard',             hasArg: false, argPlaceholder: '' },
  { value: 'Reject',   label: 'Reject with message', hasArg: true,  argPlaceholder: 'Message (optional)' },
  { value: 'Keep',     label: 'Keep in inbox',       hasArg: false, argPlaceholder: '', extendedOnly: true },
]

// ── Helpers ───────────────────────────────────────────────────

function extractError(err) {
  if (!err) return ''
  const msg = err.message || String(err)
  try {
    return JSON.parse(msg).message || msg
  } catch {
    return msg
  }
}

function summarizeCondition(c) {
  if (c.field === 'Duplicate')
    return c.value ? `Duplicate (within ${c.value}s)` : 'Duplicate message'
  const fieldLabel = CONDITION_FIELDS.find(f => f.value === c.field)?.label ?? c.field
  const opLabel = CONDITION_OPERATORS.find(o => o.value === c.operator)?.label ?? c.operator
  const name = c.field === 'Header' ? (c.headerName ?? 'Header') : fieldLabel
  if (c.field === 'CurrentDate' || c.field === 'MessageDate')
    return `${name} ${opLabel} ${c.value}`
  if (c.field === 'CurrentWeekday') {
    const wLabel = WEEKDAY_OPTIONS.find(o => o.value === c.value)?.label ?? c.value
    return `Weekday is ${wLabel}`
  }
  if (c.field === 'CurrentHour')
    return `Hour ${opLabel} ${c.value}:00`
  return `${name} ${opLabel} "${c.value}"`
}

function summarizeAction(a, compact = false) {
  switch (a.type) {
    case 'FileInto': {
      const label = `${a.argument ?? '?'}${a.autoCreate ? ' ✚' : ''}`
      return compact ? `→ ${label}` : label
    }
    case 'Redirect': return `⇥ ${a.argument ?? '?'}`
    case 'SetFlag':
      if (a.argument === '\\Seen'    || a.argument === '\\\\Seen')    return 'Mark as read'
      if (a.argument === '\\Flagged' || a.argument === '\\\\Flagged') return '⭐ Flagged'
      return `Flag: ${a.argument}`
    case 'Keep':    return 'Keep in inbox'
    case 'Discard': return 'Discard'
    case 'Reject':  return 'Reject'
    default:        return a.type
  }
}

// A condition counts as "filled in" once it has the data the backend needs.
export function isConditionValid(c) {
  if (!c) return false
  if (c.field === 'Duplicate') return true            // seconds window is optional
  if (c.field === 'Header' && !(c.headerName ?? '').trim()) return false
  return (c.value ?? '').toString().trim() !== ''
}

// An action counts as "filled in" once any required argument is present.
export function isActionValid(a) {
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

function makeEmptyRule() {
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

export function RuleCard({ rule, onEdit, onDelete, onToggleEnabled, isFirst, isLast, onMoveUp, onMoveDown, isDragOver, onDragStart, onDragOver, onDrop, onDragEnd }) {
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
        <span className="rule-card-drag" title="Drag to reorder"><GripIcon /></span>
        <label className="toggle-switch" title={rule.enabled ? 'Disable' : 'Enable'}>
          <input type="checkbox" checked={rule.enabled} onChange={e => onToggleEnabled(e.target.checked)} />
          <span className="toggle-track" />
        </label>
        <span className="rule-card-name">{rule.name}</span>
        {collapsed && hasActions && (
          <div className="rule-card-inline-actions">
            {rule.actions.map((a, i) => (
              <span key={i} className="rule-card-pill rule-card-pill-action">{summarizeAction(a, true)}</span>
            ))}
          </div>
        )}
        <div className="rule-card-btns">
          <button className="admin-icon-btn" title="Move up" disabled={isFirst} onClick={e => { e.stopPropagation(); onMoveUp() }}><ArrowUpIcon /></button>
          <button className="admin-icon-btn" title="Move down" disabled={isLast} onClick={e => { e.stopPropagation(); onMoveDown() }}><ArrowDownIcon /></button>
          <button className="admin-icon-btn" title="Edit" onClick={e => { e.stopPropagation(); onEdit() }}><PencilIcon /></button>
          <button className="admin-icon-btn is-danger" title="Delete" onClick={e => { e.stopPropagation(); onDelete() }}><TrashIcon size={13} /></button>
          <span className="rule-card-btns-sep" aria-hidden="true" />
          <button className="admin-icon-btn" title={collapsed ? 'Expand' : 'Collapse'} onClick={e => { e.stopPropagation(); setCollapsed(c => !c) }}>
            {collapsed ? <ChevronDownIcon /> : <ChevronUpIcon />}
          </button>
        </div>
      </div>

      {!collapsed && (
        <div className="rule-card-body">
          <div className="rule-card-side">
            <span className="rule-card-badge">{rule.matchAll ? 'ALL' : 'ANY'}</span>
            <div className="rule-card-conditions">
              {rule.conditions.map((c, i) => (
                <span key={i} className="rule-card-pill">{summarizeCondition(c)}</span>
              ))}
            </div>
          </div>
          {hasActions && (
            <div className="rule-card-side">
              <div className="rule-card-actions">
                {rule.actions.map((a, i) => (
                  <span key={i} className="rule-card-pill rule-card-pill-action">{summarizeAction(a, true)}</span>
                ))}
              </div>
            </div>
          )}
          {rule.stopAfter && <span className="rule-card-stop">stop</span>}
        </div>
      )}
    </div>
  )
}

// ── ConditionRow ──────────────────────────────────────────────

export function ConditionRow({ condition, onChange, onRemove, extended = false }) {
  const availableFields = extended ? CONDITION_FIELDS : CONDITION_FIELDS.filter(f => !f.extendedOnly)
  const fieldDef = CONDITION_FIELDS.find(f => f.value === condition.field)
  const baseOps = extended ? CONDITION_OPERATORS : CONDITION_OPERATORS.filter(o => !o.extendedOnly)
  const availableOperators = fieldDef?.operators
    ? baseOps.filter(o => fieldDef.operators.includes(o.value))
    : baseOps
  const isDuplicate = condition.field === 'Duplicate'
  const isDateField = fieldDef?.inputType === 'date'
  const isWeekday = fieldDef?.inputType === 'weekday'
  const isHour = fieldDef?.inputType === 'hour'

  return (
    <div className="rule-row">
      <select
        value={condition.field}
        onChange={e => {
          const newField = e.target.value
          const newDef = CONDITION_FIELDS.find(f => f.value === newField)
          const newBaseOps = extended ? CONDITION_OPERATORS : CONDITION_OPERATORS.filter(o => !o.extendedOnly)
          const newAvailOps = newDef?.operators ? newBaseOps.filter(o => newDef.operators.includes(o.value)) : newBaseOps
          const opValid = newAvailOps.some(o => o.value === condition.operator)
          onChange({
            ...condition,
            field: newField,
            headerName: null,
            operator: opValid ? condition.operator : (newAvailOps[0]?.value ?? 'Contains'),
            ...(newDef?.inputType === 'weekday' && { value: '1,2,3,4,5' }),
          })
        }}
      >
        {availableFields.map(f => <option key={f.value} value={f.value}>{f.label}</option>)}
      </select>
      {!isDuplicate && !isWeekday && (
        <select
          value={condition.operator}
          onChange={e => onChange({ ...condition, operator: e.target.value })}
        >
          {availableOperators.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
        </select>
      )}
      {condition.field === 'Header' && (
        <input
          type="text"
          className="rule-row-input"
          placeholder="Header name"
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
          placeholder="Seconds window (optional)"
          value={condition.value}
          onChange={e => onChange({ ...condition, value: e.target.value })}
          style={{ flex: 1 }}
        />
      ) : isWeekday ? (
        <select
          value={WEEKDAY_OPTIONS.some(o => o.value === condition.value) ? condition.value : WEEKDAY_OPTIONS[0].value}
          onChange={e => onChange({ ...condition, value: e.target.value })}
          style={{ flex: 1 }}
        >
          {WEEKDAY_OPTIONS.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
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
          placeholder="Value"
          value={condition.value}
          onChange={e => onChange({ ...condition, value: e.target.value })}
          style={{ flex: 1 }}
        />
      )}
      <button className="admin-icon-btn is-danger" type="button" onClick={onRemove} title="Remove">
        <TrashIcon size={13} />
      </button>
    </div>
  )
}

// ── ActionRow ─────────────────────────────────────────────────

export function ActionRow({ action, onChange, onRemove, foldersDatalistId, extended = false }) {
  const availableTypes = extended ? ACTION_TYPES : ACTION_TYPES.filter(t => !t.extendedOnly)
  const def = availableTypes.find(t => t.value === action.type) ?? availableTypes[0]
  return (
    <div className="rule-row">
      <select
        value={action.type}
        onChange={e => {
          const type = e.target.value
          const arg = type === 'SetFlag' ? '\\Seen' : ''
          onChange({ type, argument: arg })
        }}
      >
        {availableTypes.map(t => <option key={t.value} value={t.value}>{t.label}</option>)}
      </select>
      {def.hasArg && (
        <input
          type="text"
          className="rule-row-input"
          placeholder={def.argPlaceholder}
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
          Create
        </label>
      )}
      <button className="admin-icon-btn is-danger" type="button" onClick={onRemove} title="Remove">
        <TrashIcon size={13} />
      </button>
    </div>
  )
}

// ── RuleHelpModal ─────────────────────────────────────────────

function RuleHelpModal({ onClose }) {
  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal rule-help-modal" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">Rule editor — help</span>
          <button className="modal-close" onClick={onClose}>✕</button>
        </div>
        <div className="rule-help-body">

          <section className="rule-help-section">
            <h3 className="rule-help-heading">Conditions</h3>
            <dl className="rule-help-dl">
              <dt>From / To·Cc / Subject</dt>
              <dd>Match against the corresponding email header.</dd>
              <dt>Custom header</dt>
              <dd>Match any header by name (e.g. <code>X-Spam-Score</code>).</dd>
              <dt>Size</dt>
              <dd>Message size in bytes — use <em>is larger than</em> / <em>is smaller than</em>.</dd>
              <dt>Body <span className="rule-help-badge">Extended</span></dt>
              <dd>Search inside the message body text.</dd>
              <dt>Envelope from / Envelope to <span className="rule-help-badge">Extended</span></dt>
              <dd>Match the real SMTP sender/recipient, ignoring display headers.</dd>
              <dt>Recipient +detail <span className="rule-help-badge">Extended</span></dt>
              <dd>Match the <code>+tag</code> part of an address like <code>you+shopping@…</code>.</dd>
              <dt>Duplicate <span className="rule-help-badge">Extended</span></dt>
              <dd>Fires when a similar message was already received within the given time window (seconds).</dd>
              <dt>Current date / Message date <span className="rule-help-badge">Extended</span></dt>
              <dd>Compare the current date or the date in the message header against a fixed date.</dd>
              <dt>Current weekday / Current hour <span className="rule-help-badge">Extended</span></dt>
              <dd>Filter by the day of the week or the hour of the day when the message arrives.</dd>
            </dl>
          </section>

          <section className="rule-help-section">
            <h3 className="rule-help-heading">Operators</h3>
            <dl className="rule-help-dl">
              <dt>contains</dt>
              <dd>The field includes the text anywhere (case-insensitive).</dd>
              <dt>equals</dt>
              <dd>Exact match of the entire field value.</dd>
              <dt>matches (wildcard)</dt>
              <dd>Glob pattern — <code>*</code> matches any sequence of characters, <code>?</code> matches exactly one.</dd>
              <dt>matches (regex) <span className="rule-help-badge">Extended</span></dt>
              <dd>Full POSIX regular expression.</dd>
              <dt>is larger than / is smaller than</dt>
              <dd>Numeric comparison, for the <em>Size</em> field only.</dd>
              <dt>is before / is on or after</dt>
              <dd>Date comparison, for date and time fields only.</dd>
            </dl>
          </section>

          <section className="rule-help-section">
            <h3 className="rule-help-heading">Actions</h3>
            <dl className="rule-help-dl">
              <dt>Move to</dt>
              <dd>Move the message into the specified folder. Enable <em>Create</em> to auto-create the folder if it doesn&apos;t exist <span className="rule-help-badge">Extended</span>.</dd>
              <dt>Redirect to</dt>
              <dd>Forward a copy of the message to another address.</dd>
              <dt>Reject with message</dt>
              <dd>Refuse the message at delivery with an optional error text sent back to the sender.</dd>
              <dt>Discard</dt>
              <dd>Silently drop the message — no bounce, no copy kept.</dd>
              <dt>Keep in inbox <span className="rule-help-badge">Extended</span></dt>
              <dd>Explicitly keep a copy in the inbox, useful when combined with another action.</dd>
            </dl>
          </section>

          <section className="rule-help-section">
            <h3 className="rule-help-heading">Options</h3>
            <dl className="rule-help-dl">
              <dt>Mark as read</dt>
              <dd>Automatically mark the message as read upon delivery.</dd>
              <dt>Mark as flagged ⭐ <span className="rule-help-badge">Extended</span></dt>
              <dd>Add the starred/flagged flag to the message.</dd>
              <dt>Stop processing after this rule</dt>
              <dd>If this rule matches, no further rules in the list are evaluated. Without this, all matching rules apply in order.</dd>
            </dl>
          </section>

        </div>
      </div>
    </div>
  )
}

// ── RuleEditorModal ───────────────────────────────────────────

export function RuleEditorModal({ rule: initialRule, onSave, onClose, extended = false }) {
  const isNew = !initialRule
  const [rule, setRule] = useState(() => {
    const base = initialRule ? JSON.parse(JSON.stringify(initialRule)) : makeEmptyRule()
    return { ...base, actions: base.actions.filter(a => a.type !== 'SetFlag') }
  })
  const [markAsRead, setMarkAsRead] = useState(() =>
    initialRule?.actions?.some(a => a.type === 'SetFlag' && (a.argument === '\\Seen' || a.argument === '\\\\Seen')) ?? false
  )
  const [markAsFlagged, setMarkAsFlagged] = useState(() =>
    initialRule?.actions?.some(a => a.type === 'SetFlag' && (a.argument === '\\Flagged' || a.argument === '\\\\Flagged')) ?? false
  )
  const [error, setError] = useState(null)
  const [folders, setFolders] = useState([])
  const [helpOpen, setHelpOpen] = useState(false)

  const step1Done = rule.name.trim() !== ''
  const step2Done = rule.conditions.length > 0 && rule.conditions.every(isConditionValid)
  const step3Done = rule.actions.length > 0 && rule.actions.every(isActionValid)
  const step2Unlocked = step1Done
  const step3Unlocked = step1Done && step2Done
  const step4Unlocked = step1Done && step2Done && step3Done
  const canSubmit = step1Done && step2Done && step3Done

  function circleClass(isUnlocked, isDone) {
    if (!isUnlocked) return 'rule-wizard-circle rule-wizard-circle--locked'
    if (!isDone)     return 'rule-wizard-circle rule-wizard-circle--active'
    return 'rule-wizard-circle'
  }

  useEffect(() => {
    api.getFolders().then(data => { if (Array.isArray(data)) setFolders(data) }).catch(() => {})
  }, [])

  function setField(key, value) {
    setRule(r => ({ ...r, [key]: value }))
  }

  function updateCondition(i, cond) {
    setRule(r => { const c = [...r.conditions]; c[i] = cond; return { ...r, conditions: c } })
  }
  function removeCondition(i) {
    setRule(r => ({ ...r, conditions: r.conditions.filter((_, idx) => idx !== i) }))
  }
  function addCondition() {
    setRule(r => ({
      ...r,
      conditions: [...r.conditions, { field: 'Subject', operator: 'Contains', value: '', headerName: null }]
    }))
  }

  function updateAction(i, action) {
    setRule(r => { const a = [...r.actions]; a[i] = action; return { ...r, actions: a } })
  }
  function removeAction(i) {
    setRule(r => ({ ...r, actions: r.actions.filter((_, idx) => idx !== i) }))
  }
  function addAction() {
    setRule(r => ({ ...r, actions: [...r.actions, { type: 'FileInto', argument: '' }] }))
  }

  function handleSubmit(e) {
    e.preventDefault()
    if (!rule.name.trim()) { setError('Name is required'); return }
    if (rule.conditions.length === 0) { setError('At least one condition is required'); return }
    if (rule.actions.length === 0) { setError('At least one action is required'); return }
    setError(null)
    const flagActions = [
      ...(markAsRead    ? [{ type: 'SetFlag', argument: '\\Seen' }]    : []),
      ...(markAsFlagged ? [{ type: 'SetFlag', argument: '\\Flagged' }] : []),
    ]
    onSave({ ...rule, actions: [...flagActions, ...rule.actions] })
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" style={{ maxWidth: '680px' }} onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">{isNew ? 'New rule' : 'Edit rule'}</span>
          <button type="button" className="rule-help-btn" onClick={() => setHelpOpen(true)} title="Help">?</button>
          <button className="modal-close" onClick={onClose}>✕</button>
        </div>
        {helpOpen && <RuleHelpModal onClose={() => setHelpOpen(false)} />}
        <form onSubmit={handleSubmit}>
          {error && <div className="alert alert-error" style={{ marginBottom: '16px' }}>{error}</div>}

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
                <div className="rule-wizard-title">Name</div>
                <input
                  type="text"
                  className="rule-wizard-input"
                  value={rule.name}
                  onChange={e => setField('name', e.target.value)}
                  autoFocus
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
                  <span className="rule-wizard-title">Conditions</span>
                  <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <select
                      className="rule-wizard-select"
                      value={rule.matchAll ? 'all' : 'any'}
                      onChange={e => setField('matchAll', e.target.value === 'all')}
                    >
                      <option value="any">Any (anyof)</option>
                      <option value="all">All (allof)</option>
                    </select>
                    <button type="button" className="rule-editor-add-btn" onClick={addCondition}>
                      <PlusIcon /> Add
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
                  <p className="rule-editor-empty">No conditions — applies to all messages.</p>
                )}
                <p className="rule-wizard-hint">
                  <strong>Any</strong> — fires if at least one condition matches.&ensp;
                  <strong>All</strong> — every condition must match.
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
                  <span className="rule-wizard-title">Actions</span>
                  {(extended || rule.actions.length === 0) && (
                    <button type="button" className="rule-editor-add-btn" onClick={addAction}>
                      <PlusIcon /> Add
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
                  <p className="rule-editor-empty">No actions defined.</p>
                )}
              </div>
            </div>

            <div className="rule-wizard-step">
              <div className="rule-wizard-indicator">
                <div className={circleClass(step4Unlocked, step4Unlocked)}>4</div>
              </div>
              <div className={`rule-wizard-body${step4Unlocked ? '' : ' rule-wizard-body--locked'}`}>
                <div className="rule-wizard-title">Options</div>
                <div className="rule-wizard-toggle-row">
                  <label className="toggle-switch">
                    <input type="checkbox" checked={markAsRead}
                      onChange={e => setMarkAsRead(e.target.checked)} />
                    <span className="toggle-track" />
                  </label>
                  <span className="rule-wizard-toggle-label">Mark as read</span>
                </div>
                {extended && (
                  <div className="rule-wizard-toggle-row">
                    <label className="toggle-switch">
                      <input type="checkbox" checked={markAsFlagged}
                        onChange={e => setMarkAsFlagged(e.target.checked)} />
                      <span className="toggle-track" />
                    </label>
                    <span className="rule-wizard-toggle-label">Mark as flagged ⭐</span>
                  </div>
                )}
                <div className="rule-wizard-toggle-row">
                  <label className="toggle-switch">
                    <input type="checkbox" checked={rule.stopAfter}
                      onChange={e => setField('stopAfter', e.target.checked)} />
                    <span className="toggle-track" />
                  </label>
                  <span className="rule-wizard-toggle-label">
                    Stop processing after this rule
                    <span className="rule-wizard-hint rule-wizard-hint--inline">If this rule matches, the remaining rules are skipped.</span>
                  </span>
                </div>
              </div>
            </div>

          </div>

          <div style={{ display: 'flex', gap: '8px', justifyContent: 'flex-end', marginTop: '24px' }}>
            <button type="button" className="btn btn-ghost" onClick={onClose}>Cancel</button>
            <button type="submit" className="btn btn-primary" style={{ width: 'auto' }} disabled={!canSubmit}>
              {isNew ? 'Create rule' : 'Save changes'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}

// ── ConvertConfirmModal (weesky → rainloop, lists rules that will be lost) ──

export function ConvertConfirmModal({ incompatible, onConfirm, onClose, loading }) {
  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" style={{ maxWidth: '712px' }} onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">Turn off extended rules?</span>
          <button className="modal-close" onClick={onClose}>✕</button>
        </div>
        <p style={{ margin: '0 0 12px', fontSize: '14px' }}>
          The following {incompatible.length} rule{incompatible.length !== 1 ? 's' : ''} use
          features the Rainloop format can&apos;t store and will be <strong>deleted</strong>:
        </p>
        <ul className="convert-lost-list">
          {incompatible.map(r => (
            <li key={r.id}>
              <span className="convert-lost-name">{r.name || '(unnamed rule)'}</span>
              <span className="convert-lost-reason">{r.reason}</span>
            </li>
          ))}
        </ul>
        <div style={{ display: 'flex', gap: '8px', justifyContent: 'flex-end', marginTop: '20px' }}>
          <button className="btn btn-ghost" onClick={onClose} disabled={loading}>Cancel</button>
          <button className="btn btn-primary"
            style={{ width: 'auto', background: 'var(--danger)', borderColor: 'var(--danger)' }}
            onClick={onConfirm} disabled={loading}>
            {loading ? <span className="spinner" /> : 'Delete & switch'}
          </button>
        </div>
      </div>
    </div>
  )
}

// ── RulesPage ─────────────────────────────────────────────────

export default function RulesPage() {
  const { toasts, addToast, removeToast } = useToasts()

  const [ruleSet, setRuleSet] = useState(null)
  const [rules, setRules] = useState([])
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [deleting, setDeleting] = useState(false)

  const [ruleToEdit, setRuleToEdit] = useState(undefined)
  const [ruleToDelete, setRuleToDelete] = useState(null)
  const [confirmDeleteAll, setConfirmDeleteAll] = useState(false)
  const [switching, setSwitching] = useState(false)
  const [pendingConversion, setPendingConversion] = useState(null)

  // Slider ON = extended (Weesky provider); OFF = Rainloop (Snappymail interop).
  const extended = ruleSet?.providerId === 'weesky'

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const data = await api.getRules()
      setRuleSet(data)
      setRules(data.rules ?? [])
    } catch (err) {
      addToast(extractError(err) || 'Failed to load rules', 'error')
    } finally {
      setLoading(false)
    }
  }, [addToast])

  useEffect(() => { load() }, [load])

  async function persistRules(updatedRules) {
    setSaving(true)
    try {
      await api.saveRules(updatedRules, ruleSet?.providerId, ruleSet?.scriptName)
      setRules(updatedRules)
      addToast('Rules saved')
    } catch (err) {
      addToast(extractError(err) || 'Failed to save rules', 'error')
    } finally {
      setSaving(false)
    }
  }

  async function handleDeleteAll() {
    setDeleting(true)
    try {
      await api.deleteRules()
      setRules([])
      setRuleSet(prev => prev ? { ...prev, kind: 'Structured', rules: [] } : prev)
      setConfirmDeleteAll(false)
      addToast('Script deleted')
    } catch (err) {
      addToast(extractError(err) || 'Failed to delete script', 'error')
    } finally {
      setDeleting(false)
    }
  }

  // Switch provider by recompiling the current rules with the target provider. We pass a null
  // script name so the backend writes to the target provider's default script (and cleans up
  // the old one). Then we reflect the new providerId locally so the slider/editor track it.
  async function switchToProvider(targetProviderId, rulesToSave) {
    setSwitching(true)
    try {
      await api.saveRules(rulesToSave, targetProviderId, null)
      setRules(rulesToSave)
      setRuleSet(prev => prev ? { ...prev, providerId: targetProviderId, scriptName: null } : prev)
      addToast(targetProviderId === 'weesky' ? 'Extended rules enabled' : 'Switched to Rainloop')
    } catch (err) {
      addToast(extractError(err) || 'Failed to switch rule format', 'error')
    } finally {
      setSwitching(false)
    }
  }

  async function handleToggleExtended(nextExtended) {
    if (nextExtended === extended || switching || saving) return
    if (nextExtended) {
      // rainloop → weesky: Weesky is a superset, lossless. No confirmation needed.
      await switchToProvider('weesky', rules)
      return
    }
    // weesky → rainloop: preview which rules the Rainloop format can't keep.
    setSwitching(true)
    try {
      const res = await api.checkCompatibility('rainloop', rules)
      if (res?.compatible) {
        setSwitching(false)
        await switchToProvider('rainloop', rules)
      } else {
        setSwitching(false)
        setPendingConversion(res?.incompatible ?? [])
      }
    } catch (err) {
      setSwitching(false)
      addToast(extractError(err) || 'Failed to check compatibility', 'error')
    }
  }

  async function handleConfirmConversion() {
    const lostIds = new Set((pendingConversion ?? []).map(r => r.id))
    const kept = rules.filter(r => !lostIds.has(r.id))
    setPendingConversion(null)
    await switchToProvider('rainloop', kept)
  }

  function handleSaveRule(rule) {
    const updated = ruleToEdit === null
      ? [...rules, { ...rule, id: crypto.randomUUID() }]
      : rules.map(r => r.id === rule.id ? rule : r)
    setRuleToEdit(undefined)
    persistRules(updated)
  }

  function handleToggleEnabled(index, enabled) {
    persistRules(rules.map((r, i) => i === index ? { ...r, enabled } : r))
  }

  function handleDeleteRule() {
    const updated = rules.filter(r => r.id !== ruleToDelete.id)
    setRuleToDelete(null)
    persistRules(updated)
  }

  function handleMoveUp(index) {
    if (index === 0) return
    const u = [...rules];
    [u[index - 1], u[index]] = [u[index], u[index - 1]]
    persistRules(u)
  }

  function handleMoveDown(index) {
    if (index === rules.length - 1) return
    const u = [...rules];
    [u[index], u[index + 1]] = [u[index + 1], u[index]]
    persistRules(u)
  }

  const dragIndexRef = useRef(null)
  const [dropIndex, setDropIndex] = useState(null)

  function handleDrop(index) {
    const from = dragIndexRef.current
    dragIndexRef.current = null
    setDropIndex(null)
    if (from === null || from === index) return
    const u = [...rules]
    const [moved] = u.splice(from, 1)
    u.splice(index, 0, moved)
    persistRules(u)
  }

  function handleDragEnd() {
    dragIndexRef.current = null
    setDropIndex(null)
  }

  const providerLabel = ruleSet?.providerId === 'rainloop' ? 'Rainloop'
    : ruleSet?.providerId === 'weesky' ? 'Weesky'
    : null

  return (
    <>
      <div className="settings-page">
        <div className="settings-page-header">
          <span className="settings-page-title">
            <RulesIcon size={17} />
            Rules
            {providerLabel && <span className="provider-badge" style={{ marginLeft: '8px' }}>{providerLabel}</span>}
          </span>
        </div>
        <div className="rules-modal-body">
            <p className="rules-modal-desc">
              Create and manage rules that define how your incoming messages are handled. Rules are processed from top to bottom.
            </p>

            {loading ? (
              <div className="loading-center"><span className="spinner" /></div>
            ) : ruleSet?.kind === 'Advanced' ? (
              <div className="rules-notice">
                <p>The current script cannot be parsed as structured rules.</p>
                <p>Delete it to start fresh with the rule editor.</p>
                <div style={{ marginTop: '16px' }}>
                  <button className="btn"
                    style={{ width: 'auto', color: 'var(--danger)', borderColor: 'var(--danger)', border: '1px solid' }}
                    onClick={() => setConfirmDeleteAll(true)}>
                    Delete script
                  </button>
                </div>
              </div>
            ) : (
              <div>
                <div className="rules-toolbar">
                  <span className="rules-count">
                    {rules.length} rule{rules.length !== 1 ? 's' : ''}
                    {saving && <span className="spinner" style={{ marginLeft: '8px' }} />}
                  </span>
                  <div className="extended-rules-toggle" style={{ marginLeft: 'auto', display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <label className="toggle-switch" title="Extended rules">
                      <input
                        type="checkbox"
                        checked={extended}
                        disabled={switching || saving}
                        onChange={e => handleToggleExtended(e.target.checked)}
                      />
                      <span className="toggle-track" />
                    </label>
                    <span className="rule-wizard-toggle-label">Extended rules</span>
                    <HelpTooltip text={EXTENDED_RULES_HELP} />
                    {switching && <span className="spinner" />}
                  </div>
                  <button
                    className="btn btn-primary"
                    style={{ width: 'auto', display: 'inline-flex', alignItems: 'center', gap: '6px', marginLeft: '12px' }}
                    onClick={() => setRuleToEdit(null)}
                  >
                    <PlusIcon /> New rule
                  </button>
                </div>

                {rules.length === 0 ? (
                  <div className="rules-empty">
                    No rules yet. Click <strong>New rule</strong> to get started.
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
                        onDragStart={() => { dragIndexRef.current = i }}
                        onDragOver={() => { if (dragIndexRef.current !== null && dragIndexRef.current !== i) setDropIndex(i) }}
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
          onConfirm={handleConfirmConversion}
          onClose={() => setPendingConversion(null)}
          loading={switching}
        />
      )}

      {ruleToDelete && (
        <DeleteConfirmModal
          entityLabel={`rule "${ruleToDelete.name}"`}
          onConfirm={handleDeleteRule}
          onClose={() => setRuleToDelete(null)}
          loading={deleting}
        />
      )}

      {confirmDeleteAll && (
        <DeleteConfirmModal
          entityLabel="script"
          onConfirm={handleDeleteAll}
          onClose={() => setConfirmDeleteAll(false)}
          loading={deleting}
        />
      )}

      <Toasts toasts={toasts} onRemove={removeToast} />
    </>
  )
}
