import { useState, useEffect, useRef } from 'react'
import type { FormEvent } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { api } from '../../../api.js'
import Modal from '../../../components/Modal'
import { useAccountId } from '../../../hooks/useAccountId'
import { flatten } from '../../mail/folders/folderNodes'
import { PlusIcon } from './ruleIcons'
import { ActionRow, ConditionRow } from './RuleRows'
import { RuleHelpModal } from './RuleHelpModal'
import { isActionValid, isConditionValid, makeEmptyRule } from './ruleDraft'
import type { RuleAction, RuleCondition, RuleDraft } from './ruleDraft'
import type { SieveRuleWrite } from './rulesTypes'

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
