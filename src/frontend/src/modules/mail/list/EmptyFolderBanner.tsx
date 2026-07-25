import type { SpecialUse } from '../api/mailTypes'
import TrashIcon from '../../../icons/TrashIcon'

interface Props {
  role: SpecialUse | null
  total: number
  onEmpty: () => void
}

// The copy describes the effect of the action, never the server's retention: some servers purge
// the trash after N days on their own — we do not control that, so we never assert it.
const COPY: Record<string, { text: string; link: string }> = {
  trash: { text: 'Emptying the trash permanently deletes these messages.', link: 'Empty trash now' },
  junk: { text: 'Emptying the junk folder permanently deletes these messages.', link: 'Empty junk now' },
}

export default function EmptyFolderBanner({ role, total, onEmpty }: Props) {
  const copy = role ? COPY[role] : undefined
  if (!copy || total <= 0) return null

  return (
    <div className="empty-folder-banner">
      <TrashIcon size={16} />
      <span className="empty-folder-banner-text">{copy.text}</span>
      <button type="button" className="empty-folder-banner-link" onClick={onEmpty}>{copy.link}</button>
    </div>
  )
}
