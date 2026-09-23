import { useTranslation } from 'react-i18next'
import type { SpecialUse } from '../api/mailTypes'
import TrashIcon from '../../../icons/TrashIcon'

interface Props {
  role: SpecialUse | null
  total: number
  /** The purge is already on the wire: there is nothing left to ask for, and a control that is
      about to leave with the last message must not keep the focus the confirm handed it. */
  busy?: boolean
  onEmpty: () => void
}

// The copy describes the action, never the server's retention, which we do not control. The role
// name it interpolates carries the article French needs, hence not `roleLabel`'s capitalised title.
export default function EmptyFolderBanner({ role, total, busy = false, onEmpty }: Props) {
  const { t } = useTranslation('mail')
  if ((role !== 'trash' && role !== 'junk') || total <= 0) return null

  const trash = role === 'trash'

  return (
    <div className="empty-folder-banner">
      <TrashIcon size={16} />
      <span className="empty-folder-banner-text">
        {t(trash ? 'list.emptyBanner.trash' : 'list.emptyBanner.junk')}
      </span>
      <button type="button" className="empty-folder-banner-link" disabled={busy} onClick={onEmpty}>
        {t('list.emptyBanner.action', {
          role: t(trash ? 'list.emptyBanner.roleTrash' : 'list.emptyBanner.roleJunk'),
        })}
      </button>
    </div>
  )
}
