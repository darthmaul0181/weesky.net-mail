import { useTranslation } from 'react-i18next'
import Modal from '../../components/Modal'

interface Props {
  onClose: () => void
  onReload: () => void
}

/** An alertdialog: it interrupts the save to say the card moved under it, and offers the one way
    forward. */
export default function ContactConflictDialog({ onClose, onReload }: Props) {
  const { t } = useTranslation('contacts')
  return (
    <Modal role="alertdialog" title={t('layout.conflictTitle')} onClose={onClose}>
      <p>{t('layout.conflictBody')}</p>
      <div className="modal-actions">
        <button type="button" className="btn btn-primary" onClick={onReload}>
          {t('layout.conflictReload')}
        </button>
      </div>
    </Modal>
  )
}
