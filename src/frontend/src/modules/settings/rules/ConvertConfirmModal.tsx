import { Trans, useTranslation } from 'react-i18next'
import Modal from '../../../components/Modal'
import type { IncompatibleRule } from './rulesTypes'

// Weesky → Rainloop only: lists the rules the Rainloop format cannot keep.

interface ConvertConfirmModalProps {
  incompatible: IncompatibleRule[]
  onConfirm: () => void
  onClose: () => void
  loading?: boolean
}

export function ConvertConfirmModal({ incompatible, onConfirm, onClose, loading }: ConvertConfirmModalProps) {
  const { t } = useTranslation('settings')
  return (
    <Modal role="alertdialog" title={t('rules.convertTitle')} onClose={onClose} busy={loading}>
      <p style={{ margin: '0 0 12px', fontSize: '14px' }}>
        <Trans i18nKey="rules.convertBody" ns="settings" count={incompatible.length} />
      </p>
      <ul className="convert-lost-list">
        {incompatible.map(r => (
          <li key={r.id}>
            <span className="convert-lost-name">{r.name || t('rules.unnamed')}</span>
            <span className="convert-lost-reason">{r.reason}</span>
          </li>
        ))}
      </ul>
      <div style={{ display: 'flex', gap: '8px', justifyContent: 'flex-end', marginTop: '20px' }}>
        <button className="btn btn-primary"
          style={{ width: 'auto', background: 'var(--danger)', borderColor: 'var(--danger)' }}
          onClick={onConfirm} disabled={loading}>
          {loading ? <span className="spinner" /> : t('rules.deleteAndSwitch')}
        </button>
      </div>
    </Modal>
  )
}
