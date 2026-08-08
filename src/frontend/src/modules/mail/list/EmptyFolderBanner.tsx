import { useTranslation } from 'react-i18next'
import type { SpecialUse } from '../api/mailTypes'
import TrashIcon from '../../../icons/TrashIcon'

interface Props {
  role: SpecialUse | null
  total: number
  onEmpty: () => void
}

// The copy describes the effect of the action, never the server's retention: some servers purge
// the trash after N days on their own — we do not control that, so we never assert it.
// The link is one key across both roles: the role name it interpolates carries the article the
// French sentence needs, which is why it is not `roleLabel`'s capitalised title.
export default function EmptyFolderBanner({ role, total, onEmpty }: Props) {
  const { t } = useTranslation('mail')
  if ((role !== 'trash' && role !== 'junk') || total <= 0) return null

  const trash = role === 'trash'

  return (
    <div className="empty-folder-banner">
      <TrashIcon size={16} />
      <span className="empty-folder-banner-text">
        {t(trash ? 'list.emptyBanner.trash' : 'list.emptyBanner.junk')}
      </span>
      <button type="button" className="empty-folder-banner-link" onClick={onEmpty}>
        {t('list.emptyBanner.action', {
          role: t(trash ? 'list.emptyBanner.roleTrash' : 'list.emptyBanner.roleJunk'),
        })}
      </button>
    </div>
  )
}
