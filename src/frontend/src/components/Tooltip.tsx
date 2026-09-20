import type { ReactNode } from 'react'

interface Props {
  content: ReactNode
  placement?: 'top-right' | 'bottom-left' | 'bottom-right'
  /** So a trigger elsewhere can name the bubble as its own aria-describedby target. */
  bubbleId?: string
  children: ReactNode
}

export default function Tooltip({ content, placement = 'top-right', bubbleId, children }: Props) {
  return (
    <span className="tooltip-wrap">
      {children}
      <span id={bubbleId} className={`tooltip-bubble is-${placement}`} role="tooltip">{content}</span>
    </span>
  )
}
