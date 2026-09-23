import { useRef, type ReactNode, type RefObject } from 'react'
import { Trans, useTranslation } from 'react-i18next'
import Modal from './Modal'

interface DeleteConfirmModalProps {
  entityLabel?: ReactNode
  onConfirm: () => void
  onClose: () => void
  loading?: boolean
  message?: ReactNode
  title?: string
  confirmLabel?: string
  returnFocusRef?: RefObject<HTMLElement | null>
}

// Closed by the ✕ and Escape, with no Cancel button, as in the admin dialogs. `title` and
// `confirmLabel` serve the one question that is not a deletion (discarding an edited form), which
// must not put "Delete" on its button.
export default function DeleteConfirmModal({
  entityLabel, onConfirm, onClose, loading, message, title, confirmLabel, returnFocusRef,
}: DeleteConfirmModalProps) {
  const { t } = useTranslation()
  // A confirmed action removes its own opener, sometimes only on a second round trip: from the
  // press on, the return target wins. A cancel leaves the opener where it was, so it keeps it.
  const confirmed = useRef(false)
  return (
    // alertdialog, not dialog: it interrupts to ask one question rather than offering a surface.
    // `busy`: nothing cancels the write already on the wire, so no dismissal may pretend to.
    <Modal role="alertdialog" title={title ?? t('deleteConfirm.title')}
      onClose={() => { confirmed.current = false; onClose() }}
      busy={loading} returnFocusRef={returnFocusRef} preferReturnRef={confirmed}>
      <p style={{ margin: '0 0 20px', fontSize: '14px' }}>
        {/* Self-closing <name/>: entityLabel is a node, so it travels as a component rather
            than as an interpolated value. */}
        {message ?? (
          <Trans i18nKey="deleteConfirm.message" components={{ name: <strong>{entityLabel}</strong> }} />
        )}
      </p>
      <div style={{ display: 'flex', gap: '8px', justifyContent: 'flex-end' }}>
        <button className="btn btn-primary" style={{ width: 'auto', background: 'var(--danger, #dc2626)', borderColor: 'var(--danger, #dc2626)' }}
          onClick={() => { confirmed.current = true; onConfirm() }} disabled={loading}>
          {loading ? <span className="spinner" /> : confirmLabel ?? t('actions.delete')}
        </button>
      </div>
    </Modal>
  )
}
