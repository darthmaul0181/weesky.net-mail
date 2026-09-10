import { Trans, useTranslation } from 'react-i18next'

// Closing is the ✕ alone, as in the admin dialogs — no Cancel button. `message` overrides the
// default one-liner (e.g. the emptying warning). The danger fallback (var(--danger, #dc2626)) is
// kept — --danger is defined in every theme, so it renders identically everywhere.
// `title` and `confirmLabel` exist for the one question that is not a deletion — discarding an
// edited form — which asks exactly this shape and must not put "Delete" on its own button.
/**
 * @param {object} props
 * @param {import('react').ReactNode} [props.entityLabel]
 * @param {() => void} props.onConfirm
 * @param {() => void} props.onClose
 * @param {boolean} [props.loading]
 * @param {import('react').ReactNode} [props.message]
 * @param {string} [props.title]
 * @param {string} [props.confirmLabel]
 */
export function DeleteConfirmModal({
  entityLabel, onConfirm, onClose, loading, message, title, confirmLabel,
}) {
  const { t } = useTranslation()
  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">{title ?? t('deleteConfirm.title')}</span>
          <button className="modal-close" onClick={onClose}>✕</button>
        </div>
        <p style={{ margin: '0 0 20px', fontSize: '14px' }}>
          {/* Self-closing <name/>: entityLabel is a node, so it travels as a component rather
              than as an interpolated value. */}
          {message ?? (
            <Trans i18nKey="deleteConfirm.message" components={{ name: <strong>{entityLabel}</strong> }} />
          )}
        </p>
        <div style={{ display: 'flex', gap: '8px', justifyContent: 'flex-end' }}>
          <button className="btn btn-primary" style={{ width: 'auto', background: 'var(--danger, #dc2626)', borderColor: 'var(--danger, #dc2626)' }}
            onClick={onConfirm} disabled={loading}>
            {loading ? <span className="spinner" /> : confirmLabel ?? t('actions.delete')}
          </button>
        </div>
      </div>
    </div>
  )
}

export default DeleteConfirmModal
