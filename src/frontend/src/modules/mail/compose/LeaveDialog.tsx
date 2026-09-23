import { useTranslation } from 'react-i18next'
import Modal from '../../../components/Modal'

interface Props {
  busy: boolean
  canSaveDraft: boolean
  allValid: boolean
  onKeepEditing: () => void
  onDiscard: () => void
  onSaveDraft: () => void
}

/** The save-or-discard question `useLeaveGuard` asks on the way out of a dirty composer. */
export default function LeaveDialog({ busy, canSaveDraft, allValid, onKeepEditing, onDiscard, onSaveDraft }: Props) {
  const { t } = useTranslation('compose')
  // No ✕ and no backdrop close: the three answers are on its buttons, so Escape takes the
  // harmless one rather than picking one of them.
  return (
    <Modal role="alertdialog" title={t('leave.title')} onEscape={onKeepEditing}>
      <p>{t('leave.body')}</p>
      <div className="folder-pick-submit">
        <button type="button" className="btn btn-ghost" onClick={onKeepEditing}>{t('leave.keepEditing')}</button>
        {/* Locked while busy: it deletes the staged ids a save, send or upload may still be reading. */}
        <button type="button" className="btn btn-ghost" disabled={busy} onClick={onDiscard}>
          {t('leave.discard')}
        </button>
        {/* Locked on an invalid token too, where the reason is not on screen: say it. */}
        <button type="button" className="btn btn-primary" disabled={!canSaveDraft}
          title={allValid ? undefined : t('leave.fixAddress')}
          onClick={onSaveDraft}>
          {t('leave.saveDraft')}
        </button>
      </div>
    </Modal>
  )
}
