import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import TrashIcon from '../../../icons/TrashIcon'
import PencilIcon from '../../../icons/PencilIcon'
import { ArrowDownIcon, ArrowUpIcon, ChevronDownIcon, ChevronUpIcon, GripIcon } from './ruleIcons'
import { fieldLabel, operatorLabel, weekdayLabel } from './RuleRows'
import type { SettingsT } from './RuleRows'
import type { RuleAction, RuleCondition } from './ruleDraft'
import type { SieveRuleWrite } from './rulesTypes'

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
