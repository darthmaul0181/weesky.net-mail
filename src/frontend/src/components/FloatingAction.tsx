import type { ReactNode } from 'react'

/**
 * The primary action of a module, below 1024px, where its home is behind a drawer. Rendered
 * unconditionally: CSS hides it on desktop, which takes it out of the tab order too, so no
 * component has to reason about the tier for this.
 */
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
