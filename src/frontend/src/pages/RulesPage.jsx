import { useState, useEffect, useCallback } from 'react'
import { api } from '../api.js'
import logoCircle from '../assets/logo_circle.jpg'
import weeskyLogo from '../assets/weesky_net.png'

// ── Toasts (local copy, same pattern as AliasesPage) ─────────

function useToasts() {
  const [toasts, setToasts] = useState([])
  const removeToast = useCallback((id) => {
    setToasts(prev => prev.filter(t => t.id !== id))
  }, [])
  const addToast = useCallback((message, type = 'success') => {
    const id = Date.now()
    setToasts(prev => [...prev, { id, message, type }])
    if (type !== 'error') {
      setTimeout(() => setToasts(prev => prev.filter(t => t.id !== id)), 3000)
    }
  }, [])
  return { toasts, addToast, removeToast }
}

export function Toasts({ toasts, onRemove }) {
  if (!toasts.length) return null
  return (
    <div className="toast-container">
      {toasts.map(t => (
        <div key={t.id} className={`toast toast-${t.type}`}>
          <span>{t.message}</span>
          {t.type === 'error' && (
            <button className="toast-close" onClick={() => onRemove(t.id)}>✕</button>
          )}
        </div>
      ))}
    </div>
  )
}

// ── Icons ─────────────────────────────────────────────────────

function ArrowLeftIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
      <line x1="19" y1="12" x2="5" y2="12" />
      <polyline points="12 19 5 12 12 5" />
    </svg>
  )
}

function PlusIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
      <line x1="12" y1="5" x2="12" y2="19" />
      <line x1="5" y1="12" x2="19" y2="12" />
    </svg>
  )
}

function TrashIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width="13" height="13" viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="3 6 5 6 21 6" />
      <path d="M19 6l-1 14H6L5 6" />
      <path d="M10 11v6" />
      <path d="M14 11v6" />
      <path d="M9 6V4h6v2" />
    </svg>
  )
}

function PencilIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width="13" height="13" viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
      <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z" />
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

// ── Constants ─────────────────────────────────────────────────

const CONDITION_FIELDS = [
  { value: 'From',      label: 'From' },
  { value: 'Recipient', label: 'To / Cc' },
  { value: 'Subject',   label: 'Subject' },
  { value: 'Header',    label: 'Custom header' },
  { value: 'Size',      label: 'Size (bytes)' },
]

const CONDITION_OPERATORS = [
  { value: 'Contains', label: 'contains' },
  { value: 'Equals',   label: 'equals' },
  { value: 'Matches',  label: 'matches (wildcard)' },
  { value: 'Larger',   label: 'is larger than' },
  { value: 'Smaller',  label: 'is smaller than' },
]

const ACTION_TYPES = [
  { value: 'FileInto', label: 'Move to',             hasArg: true,  argPlaceholder: 'Folder name' },
  { value: 'Redirect', label: 'Redirect to',         hasArg: true,  argPlaceholder: 'email@address.com' },
  { value: 'Discard',  label: 'Discard',             hasArg: false, argPlaceholder: '' },
  { value: 'Reject',   label: 'Reject with message', hasArg: true,  argPlaceholder: 'Message (optional)' },
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
  const fieldLabel = CONDITION_FIELDS.find(f => f.value === c.field)?.label ?? c.field
  const opLabel = CONDITION_OPERATORS.find(o => o.value === c.operator)?.label ?? c.operator
  const name = c.field === 'Header' ? (c.headerName ?? 'Header') : fieldLabel
  return `${name} ${opLabel} "${c.value}"`
}

function summarizeAction(a) {
  switch (a.type) {
    case 'FileInto': return `→ ${a.argument ?? '?'}`
    case 'Redirect': return `⇥ ${a.argument ?? '?'}`
    case 'SetFlag':  return 'Mark as read'
    case 'Keep':     return 'Keep'
    case 'Discard':  return 'Discard'
    case 'Reject':   return 'Reject'
    default:         return a.type
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

export function RuleCard({ rule, onEdit, onDelete, onToggleEnabled, onMoveUp, onMoveDown, isFirst, isLast }) {
  return (
    <div className={`rule-card${rule.enabled ? '' : ' rule-card-disabled'}`}>
      <div className="rule-card-header">
        <label className="toggle-switch" title={rule.enabled ? 'Disable' : 'Enable'}>
          <input type="checkbox" checked={rule.enabled} onChange={e => onToggleEnabled(e.target.checked)} />
          <span className="toggle-track" />
        </label>
        <span className="rule-card-name">{rule.name}</span>
        <div className="rule-card-btns">
          <button className="admin-icon-btn" title="Move up" disabled={isFirst} onClick={onMoveUp}><ChevronUpIcon /></button>
          <button className="admin-icon-btn" title="Move down" disabled={isLast} onClick={onMoveDown}><ChevronDownIcon /></button>
          <button className="admin-icon-btn" title="Edit" onClick={onEdit}><PencilIcon /></button>
          <button className="admin-icon-btn is-danger" title="Delete" onClick={onDelete}><TrashIcon /></button>
        </div>
      </div>
      <div className="rule-card-body">
        <div className="rule-card-side">
          <span className="rule-card-badge">{rule.matchAll ? 'ALL' : 'ANY'}</span>
          <div className="rule-card-conditions">
            {rule.conditions.map((c, i) => (
              <span key={i} className="rule-card-pill">{summarizeCondition(c)}</span>
            ))}
          </div>
        </div>
        <span className="rule-card-arrow">→</span>
        <div className="rule-card-side">
          <div className="rule-card-actions">
            {rule.actions.map((a, i) => (
              <span key={i} className="rule-card-pill rule-card-pill-action">{summarizeAction(a)}</span>
            ))}
          </div>
        </div>
        {rule.stopAfter && <span className="rule-card-stop">stop</span>}
      </div>
    </div>
  )
}

// ── ConditionRow ──────────────────────────────────────────────

export function ConditionRow({ condition, onChange, onRemove }) {
  return (
    <div className="rule-row">
      <select
        value={condition.field}
        onChange={e => onChange({ ...condition, field: e.target.value, headerName: null })}
      >
        {CONDITION_FIELDS.map(f => <option key={f.value} value={f.value}>{f.label}</option>)}
      </select>
      <select
        value={condition.operator}
        onChange={e => onChange({ ...condition, operator: e.target.value })}
      >
        {CONDITION_OPERATORS.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
      </select>
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
      <input
        type="text"
        className="rule-row-input"
        placeholder="Value"
        value={condition.value}
        onChange={e => onChange({ ...condition, value: e.target.value })}
        style={{ flex: 1 }}
      />
      <button className="admin-icon-btn is-danger" type="button" onClick={onRemove} title="Remove">
        <TrashIcon />
      </button>
    </div>
  )
}

// ── ActionRow ─────────────────────────────────────────────────

export function ActionRow({ action, onChange, onRemove, foldersDatalistId }) {
  const def = ACTION_TYPES.find(t => t.value === action.type) ?? ACTION_TYPES[0]
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
        {ACTION_TYPES.map(t => <option key={t.value} value={t.value}>{t.label}</option>)}
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
      <button className="admin-icon-btn is-danger" type="button" onClick={onRemove} title="Remove">
        <TrashIcon />
      </button>
    </div>
  )
}

// ── RuleEditorModal ───────────────────────────────────────────

export function RuleEditorModal({ rule: initialRule, onSave, onClose }) {
  const isNew = !initialRule
  const [rule, setRule] = useState(() => {
    const base = initialRule ? JSON.parse(JSON.stringify(initialRule)) : makeEmptyRule()
    return { ...base, actions: base.actions.filter(a => a.type !== 'SetFlag') }
  })
  const [markAsRead, setMarkAsRead] = useState(() =>
    initialRule?.actions?.some(a => a.type === 'SetFlag') ?? false
  )
  const [error, setError] = useState(null)
  const [folders, setFolders] = useState([])

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
    const fullActions = markAsRead
      ? [{ type: 'SetFlag', argument: '\\Seen' }, ...rule.actions]
      : rule.actions
    onSave({ ...rule, actions: fullActions })
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" style={{ maxWidth: '680px' }} onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">{isNew ? 'New rule' : 'Edit rule'}</span>
          <button className="modal-close" onClick={onClose}>✕</button>
        </div>
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
                <div className="rule-wizard-circle">1</div>
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
                <div className="rule-wizard-circle">2</div>
                <div className="rule-wizard-line" />
              </div>
              <div className="rule-wizard-body">
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
                    onRemove={() => removeCondition(i)} />
                ))}
                {rule.conditions.length === 0 && (
                  <p className="rule-editor-empty">No conditions — applies to all messages.</p>
                )}
              </div>
            </div>

            <div className="rule-wizard-step">
              <div className="rule-wizard-indicator">
                <div className="rule-wizard-circle">3</div>
                <div className="rule-wizard-line" />
              </div>
              <div className="rule-wizard-body">
                <div className="rule-wizard-step-header">
                  <span className="rule-wizard-title">Actions</span>
                  <button type="button" className="rule-editor-add-btn" onClick={addAction}>
                    <PlusIcon /> Add
                  </button>
                </div>
                {rule.actions.map((a, i) => (
                  <ActionRow key={i} action={a}
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
                <div className="rule-wizard-circle">4</div>
              </div>
              <div className="rule-wizard-body">
                <div className="rule-wizard-title">Options</div>
                <div className="rule-wizard-toggle-row">
                  <label className="toggle-switch">
                    <input type="checkbox" checked={markAsRead}
                      onChange={e => setMarkAsRead(e.target.checked)} />
                    <span className="toggle-track" />
                  </label>
                  <span className="rule-wizard-toggle-label">Mark as read</span>
                </div>
                <div className="rule-wizard-toggle-row">
                  <label className="toggle-switch">
                    <input type="checkbox" checked={rule.stopAfter}
                      onChange={e => setField('stopAfter', e.target.checked)} />
                    <span className="toggle-track" />
                  </label>
                  <span className="rule-wizard-toggle-label">Stop processing after this rule</span>
                </div>
              </div>
            </div>

          </div>

          <div style={{ display: 'flex', gap: '8px', justifyContent: 'flex-end', marginTop: '24px' }}>
            <button type="button" className="btn btn-ghost" onClick={onClose}>Cancel</button>
            <button type="submit" className="btn btn-primary" style={{ width: 'auto' }}>
              {isNew ? 'Create rule' : 'Save changes'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}

// ── DeleteConfirmModal (local) ────────────────────────────────

function DeleteConfirmModal({ entityLabel, onConfirm, onClose, loading }) {
  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">Confirm deletion</span>
          <button className="modal-close" onClick={onClose}>✕</button>
        </div>
        <p style={{ margin: '0 0 20px', fontSize: '14px' }}>
          Delete <strong>{entityLabel}</strong>? This action cannot be undone.
        </p>
        <div style={{ display: 'flex', gap: '8px', justifyContent: 'flex-end' }}>
          <button className="btn btn-ghost" onClick={onClose} disabled={loading}>Cancel</button>
          <button className="btn btn-primary"
            style={{ width: 'auto', background: 'var(--danger)', borderColor: 'var(--danger)' }}
            onClick={onConfirm} disabled={loading}>
            {loading ? <span className="spinner" /> : 'Delete'}
          </button>
        </div>
      </div>
    </div>
  )
}

// ── RulesPage ─────────────────────────────────────────────────

export default function RulesPage({ onBack }) {
  const { toasts, addToast, removeToast } = useToasts()

  const [ruleSet, setRuleSet] = useState(null)
  const [rules, setRules] = useState([])
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [deleting, setDeleting] = useState(false)

  const [view, setView] = useState('rules')
  const [rawContent, setRawContent] = useState('')
  const [rawLoaded, setRawLoaded] = useState(false)

  const [ruleToEdit, setRuleToEdit] = useState(undefined)
  const [ruleToDelete, setRuleToDelete] = useState(null)
  const [confirmDeleteAll, setConfirmDeleteAll] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const data = await api.getRules()
      setRuleSet(data)
      setRules(data.rules ?? [])
      if (data.kind === 'Advanced') {
        setView('raw')
        setRawContent(data.rawScript ?? '')
        setRawLoaded(true)
      } else {
        setView('rules')
      }
    } catch (err) {
      addToast(extractError(err) || 'Failed to load rules', 'error')
    } finally {
      setLoading(false)
    }
  }, [addToast])

  useEffect(() => { load() }, [load])

  async function handleSwitchToRaw() {
    setView('raw')
    if (!rawLoaded) {
      try {
        const data = await api.getRawScript()
        setRawContent(data?.content ?? '')
        setRawLoaded(true)
      } catch {
        setRawContent('')
        setRawLoaded(true)
      }
    }
  }

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

  async function saveRawScript() {
    setSaving(true)
    try {
      await api.saveRawScript(rawContent, ruleSet?.scriptName)
      setRawLoaded(false)
      await load()
      addToast('Script saved')
    } catch (err) {
      addToast(extractError(err) || 'Failed to save script', 'error')
    } finally {
      setSaving(false)
    }
  }

  async function handleDeleteAll() {
    setDeleting(true)
    try {
      await api.deleteRules()
      setRules([])
      setRawContent('')
      setRuleSet(prev => prev ? { ...prev, kind: 'Structured', rules: [] } : prev)
      setConfirmDeleteAll(false)
      setView('rules')
      addToast('All rules deleted')
    } catch (err) {
      addToast(extractError(err) || 'Failed to delete rules', 'error')
    } finally {
      setDeleting(false)
    }
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

  const providerLabel = ruleSet?.providerId === 'rainloop' ? 'Rainloop'
    : ruleSet?.providerId === 'weesky' ? 'Weesky'
    : null

  return (
    <>
      <header className="site-header">
        <div className="site-header-brand">
          <button className="back-btn" onClick={onBack} title="Back to aliases">
            <ArrowLeftIcon />
          </button>
          <img src={logoCircle} alt="" className="site-header-circle" />
          <img src={weeskyLogo} alt="weesky.net" className="site-header-logo" />
        </div>
      </header>

      <div className="page-main">
        <div className="header">
          <div>
            <div className="header-title">Mail filters</div>
            <div className="header-sub">Sieve / Pigeonhole rules</div>
          </div>
          {providerLabel && (
            <span className="provider-badge">{providerLabel}</span>
          )}
        </div>

        {loading ? (
          <div className="loading-center"><span className="spinner" /></div>
        ) : (
          <>
            <div className="rules-tabs">
              <button
                className={`rules-tab${view === 'rules' ? ' is-active' : ''}`}
                onClick={() => setView('rules')}
              >
                Rules
              </button>
              <button
                className={`rules-tab${view === 'raw' ? ' is-active' : ''}`}
                onClick={handleSwitchToRaw}
              >
                Raw script
              </button>
            </div>

            {view === 'rules' ? (
              ruleSet?.kind === 'Advanced' ? (
                <div className="rules-notice">
                  <p>The current script cannot be parsed as structured rules.</p>
                  <p>Use the <strong>Raw script</strong> tab to edit it manually, or delete it to start fresh.</p>
                  <div style={{ marginTop: '16px', display: 'flex', gap: '8px' }}>
                    <button className="btn btn-ghost" onClick={handleSwitchToRaw}>View raw script</button>
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
                    <div style={{ display: 'flex', gap: '8px', marginLeft: 'auto' }}>
                      {rules.length > 0 && (
                        <button className="btn"
                          style={{ width: 'auto', color: 'var(--danger)', border: '1px solid var(--danger)' }}
                          onClick={() => setConfirmDeleteAll(true)}>
                          Delete all
                        </button>
                      )}
                      <button
                        className="btn btn-primary"
                        style={{ width: 'auto', display: 'inline-flex', alignItems: 'center', gap: '6px' }}
                        onClick={() => setRuleToEdit(null)}
                      >
                        <PlusIcon /> New rule
                      </button>
                    </div>
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
                        />
                      ))}
                    </div>
                  )}
                </div>
              )
            ) : (
              <div className="raw-editor-wrap">
                {ruleSet?.kind === 'Structured' && (
                  <div className="alert alert-warn">
                    Warning: editing the script manually may make structured rules unavailable in the rule editor.
                  </div>
                )}
                <textarea
                  className="raw-textarea"
                  value={rawContent}
                  onChange={e => setRawContent(e.target.value)}
                  spellCheck={false}
                  autoCapitalize="off"
                  autoCorrect="off"
                />
                <div style={{ display: 'flex', gap: '8px', marginTop: '12px' }}>
                  <button className="btn btn-primary" style={{ width: 'auto' }}
                    onClick={saveRawScript} disabled={saving}>
                    {saving ? <span className="spinner" /> : 'Save script'}
                  </button>
                </div>
              </div>
            )}
          </>
        )}
      </div>

      {ruleToEdit !== undefined && (
        <RuleEditorModal
          rule={ruleToEdit}
          onSave={handleSaveRule}
          onClose={() => setRuleToEdit(undefined)}
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
          entityLabel="all rules"
          onConfirm={handleDeleteAll}
          onClose={() => setConfirmDeleteAll(false)}
          loading={deleting}
        />
      )}

      <Toasts toasts={toasts} onRemove={removeToast} />
    </>
  )
}
