import type { ReactNode } from 'react'

/** A module's primary action below 1024px. Always rendered: CSS hides it on desktop, which also
 * takes it out of the tab order, so no component reasons about the tier. */
export default function FloatingAction(
  { label, onClick, children }: { label: string; onClick: () => void; children: ReactNode },
) {
  return (
    <button type="button" className="floating-action" aria-label={label} title={label}
      onClick={onClick}>
      {children}
    </button>
  )
}
