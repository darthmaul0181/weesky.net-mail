import type { SwipeAction } from '../../hooks/usePreferences'
import ArchiveIcon from '../../icons/ArchiveIcon'
import BanIcon from '../../icons/BanIcon'
import MailIcon from '../../icons/MailIcon'
import MailOpenIcon from '../../icons/MailOpenIcon'
import StarIcon from '../../icons/StarIcon'
import TrashIcon from '../../icons/TrashIcon'

/** One glyph per swipe action, shared by the row's band and the settings list. `unread: false`
    turns Read / Unread into the closed envelope, the action that swipe would take. */
export default function SwipeActionIcon({ action, size = 20, unread = true }:
  { action: SwipeAction; size?: number; unread?: boolean }) {
  switch (action) {
    case 'none': return <BanIcon size={size} />
    case 'seen': return unread ? <MailOpenIcon size={size} /> : <MailIcon size={size} />
    case 'flag': return <StarIcon size={size} />
    case 'archive': return <ArchiveIcon size={size} />
    case 'delete': return <TrashIcon size={size} />
  }
}
