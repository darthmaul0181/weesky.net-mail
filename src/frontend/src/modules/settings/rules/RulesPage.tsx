import { useRef } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import { useAccountId } from '../../../hooks/useAccountId'
import { useKeyedState } from '../../../hooks/useKeyedState'
import { useToasts } from '../../../hooks/useToasts'
import Toasts from '../../../components/Toasts'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal'
import HelpTooltip from '../../../components/HelpTooltip'
import ListLoadFailed from '../../../components/ListLoadFailed'
import FunnelIcon from '../../../icons/FunnelIcon'
import { PlusIcon } from './ruleIcons'
import { RuleEditorModal } from './RuleEditorModal'
import { ConvertConfirmModal } from './ConvertConfirmModal'
import { RuleList, useRuleDrag } from './RuleList'
import { useRulesData } from './useRulesData'
import type { PendingConversion } from './useRulesData'
import type { RuleDraft } from './ruleDraft'
import type { SieveRuleWrite } from './rulesTypes'

export default function RulesPage() {
  const { t } = useTranslation('settings')
  const { toasts, addToast, removeToast, pauseToast, resumeToast } = useToasts()
  // The script belongs to the active mailbox: the backend swaps the ManageSieve target on it.
  const accountId = useAccountId()

  const {
    ruleSet, rules, loading, saving, deleting, switching, extended,
    persistRules, deleteAll, switchToProvider, toggleExtended,
  } = useRulesData(accountId, addToast)

  // Every dialog belongs to the account it was opened under, and closes with a switch.
  // The editor: undefined is closed, null is open on a new rule.
  const [ruleToEdit, setRuleToEdit] = useKeyedState<SieveRuleWrite | null | undefined>(() => undefined, accountId)
  const [ruleToDelete, setRuleToDelete] = useKeyedState<SieveRuleWrite | null>(() => null, accountId)
  const [confirmDeleteAll, setConfirmDeleteAll] = useKeyedState(() => false, accountId)
  // Tagged too: a compatibility check still in flight at the switch answers afterwards.
  const [conversion, setConversion] = useKeyedState<PendingConversion | null>(() => null, accountId)
  const pendingConversion = conversion?.accountId === accountId ? conversion.incompatible : null

  function handleDeleteAll() {
    return deleteAll(() => setConfirmDeleteAll(false))
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

  function handleDeleteRule() {
    if (!ruleToDelete) return
    const updated = rules.filter(r => r.id !== ruleToDelete.id)
    setRuleToDelete(null)
    void persistRules(updated)
  }

  const drag = useRuleDrag(rules, persistRules)
  // A confirmed delete takes the card's own button — or the whole Advanced notice — with it, so
  // focus goes back to the page's name.
  const headingRef = useRef<HTMLHeadingElement>(null)

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
                        onChange={e => void toggleExtended(e.target.checked, setConversion)}
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
                  <RuleList
                    rules={rules}
                    drag={drag}
                    persistRules={persistRules}
                    onEdit={setRuleToEdit}
                    onDelete={setRuleToDelete}
                  />
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
