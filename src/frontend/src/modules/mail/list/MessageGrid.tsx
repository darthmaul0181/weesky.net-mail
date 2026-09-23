import { useRef } from 'react'
import type { ReactNode } from 'react'
import { useGridNav } from '../../../hooks/useGridNav'

// A component of its own because `useGridNav`'s effect is keyed on the ref alone: a grid absent at
// its owner's first layout never gets the hook, and a loading, failed or empty list draws none.
export default function MessageGrid({ label, rowCount, selecting, children }: {
  label: string
  /** The folder's own row total, or -1 where it is not knowable in rows. */
  rowCount: number
  selecting: boolean
  children: ReactNode
}) {
  const grid = useRef<HTMLDivElement>(null)
  useGridNav({ ref: grid })
  return (
    <div
      className={`message-list${selecting ? ' has-selection' : ''}`}
      role="grid"
      aria-label={label}
      aria-rowcount={rowCount}
      ref={grid}
    >
      {children}
    </div>
  )
}
