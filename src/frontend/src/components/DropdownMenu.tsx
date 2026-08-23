import { type CSSProperties, type ReactNode, useEffect, useLayoutEffect, useRef, useState } from 'react'

interface MenuItemBase {
  label: string
  /** Rich rendering for the row; `label` stays the key and the accessible-name fallback. */
  node?: ReactNode
  icon?: ReactNode
}

interface MenuAction extends MenuItemBase {
  onSelect: () => void
  disabled?: boolean
  title?: string
  href?: never
}

/** A row that navigates rather than acts. Always a new tab: middle-click, Ctrl+click and the
    browser's own context menu all do nothing on a <button>, and a control that navigates while
    looking like a command teaches the wrong thing about the menu. Never disabled — a greyed
    link has no honest markup, and no caller needs one. */
interface MenuLink extends MenuItemBase {
  href: string
  onSelect?: never
}

export type MenuItem = MenuAction | MenuLink

export type MenuEntry = MenuItem | 'separator'

interface Props {
  ariaLabel: string
  trigger: ReactNode
  items: MenuEntry[]
  className?: string
  /**
   * Which way the menu opens relative to the trigger. Defaults to 'down'.
   *
   * 'auto' measures the open menu against the viewport and flips it up only when it would not
   * fit below and fits better above. For a trigger that sits at the end of a column whose length
   * the contact decides — the editor's "add a field" — neither fixed choice is right: measured on
   * a 763px viewport the menu ran to 838, seventy-five pixels under the fold, and a contact with
   * no postal address puts the same trigger high enough that opening upward would run off the top
   * instead.
   */
  direction?: 'down' | 'up' | 'auto'
  /**
   * Which edge the menu shares with its trigger, i.e. the direction it grows in. Right by
   * default, which suits a trigger sitting against the right edge of its column — the reader
   * kebab, the banner chevron, the account block. A trigger at the *left* of a wide row needs
   * 'left', or the menu grows away from the space it has and slides under the column beside it.
   */
  align?: 'right' | 'left'
}

/** Click-toggled dropdown on the IdentityMenu pattern: outside mousedown and Escape close it. */
export default function DropdownMenu(
  { ariaLabel, trigger, items, className, direction = 'down', align = 'right' }: Props) {
  const [open, setOpen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const menuRef = useRef<HTMLDivElement>(null)
  const [fixedStyle, setFixedStyle] = useState<CSSProperties | undefined>(undefined)
  // What 'auto' resolved to, and what the class and the scroll guard below read. A fixed
  // direction answers itself; 'auto' answers 'down' until the layout effect has measured, which
  // is the same frame — the menu never paints in the wrong place.
  const [placement, setPlacement] = useState<'down' | 'up'>(direction === 'up' ? 'up' : 'down')

  // 'up' menus escape any ancestor scroll clip (the reader's attachment band is one) by going
  // `position: fixed` off the trigger's own rect instead of the CSS `bottom: calc(100% + …)`,
  // which is relative to a containing block that can sit inside that clipped band.
  useLayoutEffect(() => {
    if (!open || direction === 'down' || !triggerRef.current) {
      setFixedStyle(undefined)
      setPlacement('down')
      return
    }
    const rect = triggerRef.current.getBoundingClientRect()
    // Measured on the menu itself rather than counted off the items: a row's height is the
    // language's, not the component's, and a wrapped label makes it two.
    const height = menuRef.current?.offsetHeight ?? 0
    const below = window.innerHeight - rect.bottom
    // Flip only when the menu would spill below *and* fits whole above. A menu too tall for
    // either side stays down, where the rows past the fold can still be scrolled to; flipped up
    // it would be clipped at the viewport's block-start edge, which nothing can reveal.
    const up = direction === 'up' || (below < height + 8 && rect.top >= height + 8)
    setPlacement(up ? 'up' : 'down')
    setFixedStyle(up
      ? {
          position: 'fixed',
          bottom: `${window.innerHeight - rect.top + 4}px`,
          ...(align === 'left'
            ? { left: `${rect.left}px` }
            : { right: `${window.innerWidth - rect.right}px` }),
        }
      : undefined)
  }, [open, direction, align])

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
    if (!open || placement !== 'up') return
    function onScrollOrResize() { setOpen(false) }
    window.addEventListener('scroll', onScrollOrResize, true)
    window.addEventListener('resize', onScrollOrResize, true)
    return () => {
      window.removeEventListener('scroll', onScrollOrResize, true)
      window.removeEventListener('resize', onScrollOrResize, true)
    }
  }, [open, placement])

  return (
    <div
      className={`dropdown-root${placement === 'up' ? ' is-up' : ''}${align === 'left' ? ' is-left' : ''}`}
      ref={rootRef}
    >
      <button type="button" className={className} aria-label={ariaLabel} aria-expanded={open}
        ref={triggerRef} onClick={() => setOpen(o => !o)}>
        {trigger}
      </button>
      {open && (
        <div className="dropdown-menu" role="menu" ref={menuRef} style={fixedStyle}>
          {items.map((entry, index) =>
            entry === 'separator' ? (
              <hr key={index} className="dropdown-rule" />
            ) : entry.href !== undefined ? (
              <a key={entry.label} role="menuitem" className="dropdown-item" href={entry.href}
                target="_blank" rel="noopener noreferrer" onClick={() => setOpen(false)}
                onAuxClick={() => setOpen(false)}>
                {entry.icon}
                {entry.node ?? entry.label}
              </a>
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
