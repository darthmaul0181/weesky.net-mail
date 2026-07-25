import { type CSSProperties, type ReactNode, useEffect, useLayoutEffect, useRef, useState } from 'react'

export interface MenuItem {
  label: string
  /** Rich rendering for the row; `label` stays the key and the accessible-name fallback. */
  node?: ReactNode
  icon?: ReactNode
  onSelect: () => void
  disabled?: boolean
  title?: string
}

export type MenuEntry = MenuItem | 'separator'

interface Props {
  ariaLabel: string
  trigger: ReactNode
  items: MenuEntry[]
  className?: string
  /** Which way the menu opens relative to the trigger. Defaults to 'down'. */
  direction?: 'down' | 'up'
}

/** Click-toggled dropdown on the IdentityMenu pattern: outside mousedown and Escape close it. */
export default function DropdownMenu({ ariaLabel, trigger, items, className, direction = 'down' }: Props) {
  const [open, setOpen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const [fixedStyle, setFixedStyle] = useState<CSSProperties | undefined>(undefined)

  // 'up' menus escape any ancestor scroll clip (the reader's attachment band is one) by going
  // `position: fixed` off the trigger's own rect instead of the CSS `bottom: calc(100% + …)`,
  // which is relative to a containing block that can sit inside that clipped band.
  useLayoutEffect(() => {
    if (!open || direction !== 'up' || !triggerRef.current) { setFixedStyle(undefined); return }
    const rect = triggerRef.current.getBoundingClientRect()
    setFixedStyle({
      position: 'fixed',
      bottom: `${window.innerHeight - rect.top + 4}px`,
      right: `${window.innerWidth - rect.right}px`,
    })
  }, [open, direction])

  useEffect(() => {
    if (!open) return
    function onMouseDown(e: MouseEvent) {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) setOpen(false)
    }
    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape') setOpen(false)
    }
    document.addEventListener('mousedown', onMouseDown)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onMouseDown)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  // A fixed menu does not travel with a scrolled trigger the way an absolutely-positioned one
  // does, so any scroll or resize while it is open just closes it rather than leaving it
  // stranded. Capture:true so a scroll inside an ancestor band (the attachment row) counts too.
  useEffect(() => {
    if (!open || direction !== 'up') return
    function onScrollOrResize() { setOpen(false) }
    window.addEventListener('scroll', onScrollOrResize, true)
    window.addEventListener('resize', onScrollOrResize, true)
    return () => {
      window.removeEventListener('scroll', onScrollOrResize, true)
      window.removeEventListener('resize', onScrollOrResize, true)
    }
  }, [open, direction])

  return (
    <div className={`dropdown-root${direction === 'up' ? ' is-up' : ''}`} ref={rootRef}>
      <button type="button" className={className} aria-label={ariaLabel} aria-expanded={open}
        ref={triggerRef} onClick={() => setOpen(o => !o)}>
        {trigger}
      </button>
      {open && (
        <div className="dropdown-menu" role="menu" style={fixedStyle}>
          {items.map((entry, index) =>
            entry === 'separator' ? (
              <hr key={index} className="dropdown-rule" />
            ) : (
              <button key={entry.label} type="button" role="menuitem" className="dropdown-item"
                disabled={entry.disabled} title={entry.title}
                onClick={() => { setOpen(false); entry.onSelect() }}>
                {entry.icon}
                {entry.node ?? entry.label}
              </button>
            )
          )}
        </div>
      )}
    </div>
  )
}
