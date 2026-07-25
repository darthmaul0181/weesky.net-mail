import type { ReactNode } from 'react'

interface Props {
  content: ReactNode
  placement?: 'top-right' | 'bottom-left' | 'bottom-right'
  children: ReactNode
}

export default function Tooltip({ content, placement = 'top-right', children }: Props) {
  return (
    <span className="tooltip-wrap">
      {children}
      <span className={`tooltip-bubble is-${placement}`} role="tooltip">{content}</span>
    </span>
  )
}
