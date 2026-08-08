import { useRef } from 'react'
import { useTranslation } from 'react-i18next'
import PaperclipIcon from '../../../icons/PaperclipIcon'
import { formatSize } from '../reader/formatSize'
import type { StagedItem } from './useStagedAttachments'

interface Props {
  items: StagedItem[]
  onAddFiles: (files: File[]) => void
  onRemove: (key: string) => void
}

export default function AttachmentTray({ items, onAddFiles, onRemove }: Props) {
  const { t } = useTranslation('compose')
  const picker = useRef<HTMLInputElement>(null)

  return (
    <div className="compose-attachments">
      {items.map(item => (
        <span key={item.key} className={`compose-attachment${item.error ? ' is-error' : ''}`}>
          <span className="compose-attachment-name">{item.fileName}</span>
          {item.error
            ? <span className="compose-attachment-error" role="alert">{item.error}</span>
            : item.progress < 1
              ? <progress value={item.progress} max={1}
                  aria-label={t('attachments.uploading', { name: item.fileName })} />
              : <span className="compose-attachment-size">{formatSize(item.size)}</span>}
          <button type="button" aria-label={t('attachments.remove', { name: item.fileName })}
            onClick={() => onRemove(item.key)}>✕</button>
        </span>
      ))}
      <button type="button" className="btn btn-ghost compose-attach-btn" onClick={() => picker.current?.click()}>
        <PaperclipIcon size={16} /> {t('attachments.attach')}
      </button>
      <input ref={picker} type="file" multiple hidden data-testid="attachment-input"
        onChange={e => {
          const files = e.target.files
          if (files?.length) { onAddFiles(Array.from(files)); e.target.value = '' }
        }} />
    </div>
  )
}
