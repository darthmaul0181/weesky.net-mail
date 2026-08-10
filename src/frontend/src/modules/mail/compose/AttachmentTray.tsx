import { useTranslation } from 'react-i18next'
import { formatSize } from '../reader/formatSize'
import type { StagedItem } from './useStagedAttachments'

interface Props {
  items: StagedItem[]
  onRemove: (key: string) => void
}

/** The staged files, and only them: the button that adds one is a toolbar action, beside the
    image picker it shares a file dialog with. An empty tray draws nothing rather than a band. */
export default function AttachmentTray({ items, onRemove }: Props) {
  const { t } = useTranslation('compose')

  if (items.length === 0) return null

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
    </div>
  )
}
