import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import TrashIcon from '../../../icons/TrashIcon'
import type { RuleAction, RuleCondition } from './ruleDraft'
import type { SieveActionType, SieveConditionField, SieveConditionOperator } from './rulesTypes'

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

export type SettingsT = TFunction<'settings'>

/** The nine keys are written out rather than held on the options: a key that reaches `t()` only
    as a variable is invisible to the typed `t()` and to `src/locales/keys.test.ts` alike. */
export function weekdayLabel(value: string, t: SettingsT) {
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

// The labels are spelled out for weekdayLabel's reason; a value no list offers (To, Cc) is shown raw.
export function fieldLabel(value: SieveConditionField, t: SettingsT): string {
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

export function operatorLabel(value: SieveConditionOperator, t: SettingsT): string {
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
