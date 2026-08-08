import { useState, useEffect, useCallback, useRef } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import i18next from 'i18next'
import { api } from '../../../api.js'
import { useAccountId } from '../../../hooks/useAccountId'
import { useToasts } from '../../../hooks/useToasts.js'
import { flatten } from '../../mail/folders/folderNodes'
import Toasts from '../../../components/Toasts.jsx'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal.jsx'
import HelpTooltip from '../../../components/HelpTooltip.jsx'
import TrashIcon from '../../../icons/TrashIcon.jsx'
import PencilIcon from '../../../icons/PencilIcon.jsx'
import FunnelIcon from '../../../icons/FunnelIcon'

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

// ── Constants ─────────────────────────────────────────────────

const TEXT_OPS = ['Contains', 'NotContains', 'Equals', 'NotEquals', 'Regex']

const CONDITION_FIELDS = [
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
    as a variable is invisible to `src/locales/keys.test.ts`, the only guard over this .jsx file. */
function weekdayLabel(value, t) {
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

const CONDITION_OPERATORS = [
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

const ACTION_TYPES = [
  { value: 'FileInto', hasArg: true },
  { value: 'Redirect', hasArg: true },
  { value: 'Discard',  hasArg: false },
  { value: 'Reject',   hasArg: true },
  { value: 'Keep',     hasArg: false, extendedOnly: true },
]

// The labels live in the catalogue under the wire value, so one lookup serves every list.
const fieldLabel = (value, t) =>
  CONDITION_FIELDS.some(f => f.value === value) ? t(`rules.fields.${value}`) : value
const operatorLabel = (value, t) =>
  CONDITION_OPERATORS.some(o => o.value === value) ? t(`rules.operators.${value}`) : value

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

function summarizeCondition(c, t) {
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

function summarizeAction(a, compact, t) {
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
          <input type="checkbox" checked={rule.enabled} onChange={e => onToggleEnabled(e.target.checked)} />
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

export function ConditionRow({ condition, onChange, onRemove, extended = false }) {
  const { t } = useTranslation('settings')
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
        {availableFields.map(f => <option key={f.value} value={f.value}>{fieldLabel(f.value, t)}</option>)}
      </select>
      {!isDuplicate && !isWeekday && (
        <select
          value={condition.operator}
          onChange={e => onChange({ ...condition, operator: e.target.value })}
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

export function ActionRow({ action, onChange, onRemove, foldersDatalistId, extended = false }) {
  const { t } = useTranslation('settings')
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
        {availableTypes.map(type =>
          <option key={type.value} value={type.value}>{t(`rules.actionTypes.${type.value}`)}</option>)}
      </select>
      {def.hasArg && (
        <input
          type="text"
          className="rule-row-input"
          placeholder={t(`rules.actionArgs.${def.value}`)}
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

function RuleHelpModal({ onClose }) {
  const { t } = useTranslation('settings')
  // Built from the keys the dropdowns themselves read: restating the wording here is how the
  // help dialog ends up contradicting the control it documents.
  const pair = (a, b) => t('rules.help.termPair', { a, b })

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal rule-help-modal" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">{t('rules.help.title')}</span>
          <button className="modal-close" onClick={onClose}>✕</button>
        </div>
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
      </div>
    </div>
  )
}

// ── RuleEditorModal ───────────────────────────────────────────

export function RuleEditorModal({ rule: initialRule, onSave, onClose, extended = false }) {
  const { t } = useTranslation('settings')
  const isNew = !initialRule
  const accountId = useAccountId()
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

  // The rule is filed into the active mailbox, so the picker must offer that mailbox's folders:
  // /api/Account/Folders answers the primary's whatever the header says. Containers cannot hold
  // mail, so a rule naming one would file nowhere.
  useEffect(() => {
    api.getMailFolders({ accountId })
      .then(tree => setFolders(
        Array.isArray(tree) ? flatten(tree).filter(f => f.node.selectable).map(f => f.node.path) : []))
      .catch(() => {})
  }, [accountId])

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
    if (!rule.name.trim()) { setError(t('rules.nameRequired')); return }
    if (rule.conditions.length === 0) { setError(t('rules.conditionRequired')); return }
    if (rule.actions.length === 0) { setError(t('rules.actionRequired')); return }
    setError(null)
    const flagActions = [
      ...(markAsRead    ? [{ type: 'SetFlag', argument: '\\Seen' }]    : []),
      ...(markAsFlagged ? [{ type: 'SetFlag', argument: '\\Flagged' }] : []),
    ]
    onSave({ ...rule, actions: [...flagActions, ...rule.actions] })
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">{t(isNew ? 'rules.newRule' : 'rules.editRule')}</span>
          <button type="button" className="rule-help-btn" onClick={() => setHelpOpen(true)}
            title={t('rules.helpTitle')}>?</button>
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
                <div className="rule-wizard-title">{t('rules.stepName')}</div>
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
                      onChange={e => setMarkAsRead(e.target.checked)} />
                    <span className="toggle-track" />
                  </label>
                  <span className="rule-wizard-toggle-label">{t('rules.markAsRead')}</span>
                </div>
                {extended && (
                  <div className="rule-wizard-toggle-row">
                    <label className="toggle-switch">
                      <input type="checkbox" checked={markAsFlagged}
                        onChange={e => setMarkAsFlagged(e.target.checked)} />
                      <span className="toggle-track" />
                    </label>
                    <span className="rule-wizard-toggle-label">{t('rules.markAsFlagged')}</span>
                  </div>
                )}
                <div className="rule-wizard-toggle-row">
                  <label className="toggle-switch">
                    <input type="checkbox" checked={rule.stopAfter}
                      onChange={e => setField('stopAfter', e.target.checked)} />
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
            <button type="button" className="btn btn-ghost" onClick={onClose}>
              {t('actions.cancel', { ns: 'common' })}
            </button>
            <button type="submit" className="btn btn-primary" style={{ width: 'auto' }} disabled={!canSubmit}>
              {isNew ? t('rules.createRule') : t('actions.saveChanges', { ns: 'common' })}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}

// ── ConvertConfirmModal (weesky → rainloop, lists rules that will be lost) ──

export function ConvertConfirmModal({ incompatible, onConfirm, onClose, loading }) {
  const { t } = useTranslation('settings')
  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">{t('rules.convertTitle')}</span>
          <button className="modal-close" onClick={onClose}>✕</button>
        </div>
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
          <button className="btn btn-ghost" onClick={onClose} disabled={loading}>
            {t('actions.cancel', { ns: 'common' })}
          </button>
          <button className="btn btn-primary"
            style={{ width: 'auto', background: 'var(--danger)', borderColor: 'var(--danger)' }}
            onClick={onConfirm} disabled={loading}>
            {loading ? <span className="spinner" /> : t('rules.deleteAndSwitch')}
          </button>
        </div>
      </div>
    </div>
  )
}

// ── RulesPage ─────────────────────────────────────────────────

export default function RulesPage() {
  const { t } = useTranslation('settings')
  const { toasts, addToast, removeToast } = useToasts()
  // The script belongs to the active mailbox: the backend swaps the ManageSieve target on it.
  const accountId = useAccountId()

  const [ruleSet, setRuleSet] = useState(null)
  const [rules, setRules] = useState([])
  // The account the two above were loaded under. Null until a load succeeds, so a failed one
  // leaves nothing writable rather than the previous account's set.
  const [loadedFor, setLoadedFor] = useState(null)
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
    // Dropped before the fetch, not after it: a load that fails would otherwise leave the previous
    // account's rules on screen and clickable, and one toggle PUTs them into this account's script.
    setRuleSet(null)
    setRules([])
    setLoadedFor(null)
    try {
      const data = await api.getRules({ accountId })
      setRuleSet(data)
      setRules(data.rules ?? [])
      setLoadedFor(accountId)
    } catch (err) {
      addToast(extractError(err) || i18next.t('settings:rules.loadFailed'), 'error')
    } finally {
      setLoading(false)
    }
  }, [addToast, accountId])

  useEffect(() => { load() }, [load])

  // Clearing alone still leaves a window: between the switch and the arriving load the old set is
  // on screen for a render. A write refuses whenever it no longer belongs to the active account.
  function belongsToActiveAccount() {
    if (loadedFor === accountId) return true
    addToast(t('rules.wrongAccount'), 'error')
    return false
  }

  async function persistRules(updatedRules) {
    if (!belongsToActiveAccount()) return
    setSaving(true)
    try {
      await api.saveRules(updatedRules, ruleSet?.providerId, ruleSet?.scriptName, { accountId })
      setRules(updatedRules)
      addToast(t('rules.saved'))
    } catch (err) {
      addToast(extractError(err) || t('rules.saveFailed'), 'error')
    } finally {
      setSaving(false)
    }
  }

  async function handleDeleteAll() {
    if (!belongsToActiveAccount()) return
    setDeleting(true)
    try {
      await api.deleteRules({ accountId })
      setRules([])
      setRuleSet(prev => prev ? { ...prev, kind: 'Structured', rules: [] } : prev)
      setConfirmDeleteAll(false)
      addToast(t('rules.scriptDeleted'))
    } catch (err) {
      addToast(extractError(err) || t('rules.deleteScriptFailed'), 'error')
    } finally {
      setDeleting(false)
    }
  }

  // Switch provider by recompiling the current rules with the target provider. We pass a null
  // script name so the backend writes to the target provider's default script (and cleans up
  // the old one). Then we reflect the new providerId locally so the slider/editor track it.
  async function switchToProvider(targetProviderId, rulesToSave) {
    if (!belongsToActiveAccount()) return
    setSwitching(true)
    try {
      await api.saveRules(rulesToSave, targetProviderId, null, { accountId })
      setRules(rulesToSave)
      setRuleSet(prev => prev ? { ...prev, providerId: targetProviderId, scriptName: null } : prev)
      addToast(t(targetProviderId === 'weesky' ? 'rules.extendedEnabled' : 'rules.switchedRainloop'))
    } catch (err) {
      addToast(extractError(err) || t('rules.switchFailed'), 'error')
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
      const res = await api.checkCompatibility('rainloop', rules, { accountId })
      if (res?.compatible) {
        setSwitching(false)
        await switchToProvider('rainloop', rules)
      } else {
        setSwitching(false)
        setPendingConversion(res?.incompatible ?? [])
      }
    } catch (err) {
      setSwitching(false)
      addToast(extractError(err) || t('rules.compatFailed'), 'error')
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
          <h1 className="settings-page-title">
            <FunnelIcon size={17} />
            {t('nav.rules')}
            {providerLabel && <span className="provider-badge">{providerLabel}</span>}
          </h1>
        </div>
        <div className="rules-modal-body">
            <p className="rules-modal-desc">{t('rules.intro')}</p>

            {loading ? (
              <div className="loading-center"><span className="spinner" /></div>
            ) : ruleSet?.kind === 'Advanced' ? (
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
                        onChange={e => handleToggleExtended(e.target.checked)}
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
          entityLabel={t('rules.ruleEntity', { name: ruleToDelete.name })}
          onConfirm={handleDeleteRule}
          onClose={() => setRuleToDelete(null)}
          loading={deleting}
        />
      )}

      {confirmDeleteAll && (
        <DeleteConfirmModal
          entityLabel={t('rules.scriptEntity')}
          onConfirm={handleDeleteAll}
          onClose={() => setConfirmDeleteAll(false)}
          loading={deleting}
        />
      )}

      <Toasts toasts={toasts} onRemove={removeToast} />
    </>
  )
}
