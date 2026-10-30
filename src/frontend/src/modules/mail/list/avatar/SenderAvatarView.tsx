import type { CSSProperties } from 'react'
import { useInView } from '../../../../hooks/useInView'
import { avatarHue, avatarInitials, type AvatarColor } from './senderAvatar'
import { useSenderAvatarUrl } from './useSenderAvatarUrl'

const ROWS_AHEAD_MARGIN = '1700px 0px'

interface Props {
  name?: string
  address?: string
  color: AvatarColor
}

export default function SenderAvatar({ name, address, color }: Props) {
  const [ref, inView] = useInView<HTMLDivElement>(ROWS_AHEAD_MARGIN, '.mail-list-scroll')
  const url = useSenderAvatarUrl(address, inView)
  const style = color === 'sender' ? { '--avatar-h': avatarHue(address) } as CSSProperties : undefined

  return (
    <div ref={ref} className={`message-row-avatar ${url ? 'is-photo' : `is-${color}`}`} aria-hidden="true" style={style}>
      {url ? <img src={url} alt="" /> : avatarInitials(name, address)}
    </div>
  )
}
