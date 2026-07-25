// Closing is the ✕ alone, as in the admin dialogs — no Cancel button. `message` overrides the
// default one-liner (e.g. the emptying warning). The danger fallback (var(--danger, #dc2626)) is
// kept — --danger is defined in every theme, so it renders identically everywhere.
/**
 * @param {object} props
 * @param {import('react').ReactNode} [props.entityLabel]
 * @param {() => void} props.onConfirm
 * @param {() => void} props.onClose
 * @param {boolean} [props.loading]
 * @param {import('react').ReactNode} [props.message]
 */
export function DeleteConfirmModal({ entityLabel, onConfirm, onClose, loading, message }) {
  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">Confirm deletion</span>
          <button className="modal-close" onClick={onClose}>✕</button>
        </div>
        <p style={{ margin: '0 0 20px', fontSize: '14px' }}>
          {message ?? <>Delete <strong>{entityLabel}</strong>? This action cannot be undone.</>}
        </p>
        <div style={{ display: 'flex', gap: '8px', justifyContent: 'flex-end' }}>
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
