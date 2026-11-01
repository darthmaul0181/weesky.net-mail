import type { CSSProperties } from 'react'
import { useInView } from '../../hooks/useInView'
import { avatarHue, avatarInitials, type AvatarColor } from './senderAvatar'
import { useSenderAvatarUrl } from './useSenderAvatarUrl'

const ROWS_AHEAD_MARGIN = '1700px 0px'

interface Props {
  name?: string
  address?: string
  color: AvatarColor
  bimiDomain?: string
  bimiSelector?: string
  /** The host's own class, which places the box. */
  className?: string
  /** The scrolling ancestor the picture is looked up ahead of. */
  scrollRoot?: string
  /** The contact this avatar stands for, when the host knows it (`null`: one without a photo): its photo
      is the only contact photo looked at, never another contact's holding the same address. */
  contactId?: string | null
  /** False keeps the lookups on our side: no BIMI logo, no Gravatar, only a stored photo or the initials. */
  remote?: boolean
}

export default function SenderAvatar({ name, address, color, bimiDomain, bimiSelector, className, scrollRoot, contactId, remote }: Props) {
  const [ref, inView] = useInView<HTMLDivElement>(ROWS_AHEAD_MARGIN, scrollRoot)
  const url = useSenderAvatarUrl(address, inView, bimiDomain ? { domain: bimiDomain, selector: bimiSelector } : undefined, contactId, remote)
  const style = color === 'sender' ? { '--avatar-h': avatarHue(address) } as CSSProperties : undefined

  return (
    <div ref={ref} className={`avatar${className ? ` ${className}` : ''} ${url ? 'is-photo' : `is-${color}`}`} aria-hidden="true" style={style}>
      {url ? <img src={url} alt="" /> : avatarInitials(name, address)}
    </div>
  )
}
