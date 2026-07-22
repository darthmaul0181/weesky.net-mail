import { type ReactNode, useEffect, useRef, useState } from 'react'

export interface MenuItem {
  label: string
  icon?: ReactNode
  onSelect: () => void
}

interface Props {
  ariaLabel: string
  trigger: ReactNode
  items: MenuItem[]
  className?: string
}

/** Click-toggled dropdown on the AvatarMenu pattern: outside mousedown and Escape close it. */
export default function DropdownMenu({ ariaLabel, trigger, items, className }: Props) {
  const [open, setOpen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)

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

  return (
    <div className="dropdown-root" ref={rootRef}>
      <button type="button" className={className} aria-label={ariaLabel} aria-expanded={open}
        onClick={() => setOpen(o => !o)}>
        {trigger}
      </button>
      {open && (
        <div className="dropdown-menu" role="menu">
          {items.map(item => (
            <button key={item.label} type="button" role="menuitem" className="dropdown-item"
              onClick={() => { setOpen(false); item.onSelect() }}>
              {item.icon}
              {item.label}
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
