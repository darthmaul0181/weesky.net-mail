import { useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import Modal from '../../../components/Modal'
import DownloadIcon from '../../../icons/DownloadIcon'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import WhatsNewTarget from '../../whatsnew/WhatsNewTarget'
import type { MailExportFormat } from '../api/mailTypes'

interface Props {
  /** Resolves once the file is handed to the browser; a rejection stays in the dialog. */
  onSave: (format: MailExportFormat) => Promise<void>
  onClose: () => void
}

export default function SaveMessageModal({ onSave, onClose }: Props) {
  const { t } = useTranslation('mail')
  const id = useId()
  const [format, setFormat] = useState<MailExportFormat>('eml')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSaving(true)
    setError(null)
    try {
      await onSave(format)
      onClose()
    } catch (failure) {
      setError(apiErrorMessage(failure, t('reader.saveDialog.failed')))
      setSaving(false)
    }
  }

  const option = (value: MailExportFormat, name: string, hint: string) => (
    <div className="save-format">
      <label>
        <input type="radio" name={`${id}-format`} checked={format === value}
          aria-describedby={`${id}-${value}`} onChange={() => setFormat(value)} />
        {name}
      </label>
      <span id={`${id}-${value}`} className="setting-hint">{hint}</span>
    </div>
  )

  return (
    <Modal title={t('reader.saveDialog.title')} icon={<DownloadIcon size={18} />}
      onClose={onClose} busy={saving} onSubmit={event => void submit(event)}>
      {error && <div className="alert alert-error" role="alert">{error}</div>}
      <WhatsNewTarget id="reader:save-message" className="field-h">
        {/* The group names itself; this label is the row's caption, associated with nothing. */}
        <label>{t('reader.saveDialog.format')}</label>
        <div className="save-formats" role="radiogroup" aria-label={t('reader.saveDialog.format')}>
          {option('eml', t('reader.saveDialog.eml'), t('reader.saveDialog.emlHint'))}
          {option('msg', t('reader.saveDialog.msg'), t('reader.saveDialog.msgHint'))}
        </div>
      </WhatsNewTarget>
      <div className="modal-actions">
        <button type="submit" className="btn btn-primary" disabled={saving}>
          {saving ? <span className="spinner" /> : t('reader.saveDialog.submit')}
        </button>
      </div>
    </Modal>
  )
}
