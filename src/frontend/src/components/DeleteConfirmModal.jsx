// Reconciled from two copies: AliasesPage used a plain "btn" Cancel button,
// RulesPage used "btn btn-ghost". `cancelClassName` (default "btn") satisfies
// both call sites; RulesPage passes cancelClassName="btn btn-ghost". The danger
// fallback (var(--danger, #dc2626)) is kept — RulesPage omitted the fallback but
// --danger is defined in every theme, so both render identically.
export function DeleteConfirmModal({ entityLabel, onConfirm, onClose, loading, cancelClassName = 'btn' }) {
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
          <button className={cancelClassName} onClick={onClose} disabled={loading}>Cancel</button>
          <button className="btn btn-primary" style={{ width: 'auto', background: 'var(--danger, #dc2626)', borderColor: 'var(--danger, #dc2626)' }}
            onClick={onConfirm} disabled={loading}>
            {loading ? <span className="spinner" /> : 'Delete'}
          </button>
        </div>
      </div>
    </div>
  )
}

export default DeleteConfirmModal
