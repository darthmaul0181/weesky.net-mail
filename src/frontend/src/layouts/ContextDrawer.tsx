import { useCallback, useEffect, useRef, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { useLocation } from 'react-router'
import MenuIcon from '../icons/MenuIcon'
import { useLayer } from '../hooks/useLayer'
import { useViewport } from '../hooks/useViewport'
import { useKeyedState } from '../hooks/useKeyedState'

interface Props {
  open: boolean
  onClose: () => void
  children: ReactNode
}

/**
 * The context pane below 1024px: mail's folder tree, contacts' scopes, settings' navigation.
 * One component for all three — they differ in what they hold, never in how they open.
 *
 * Closed, it is display:none rather than unmounted, so the tree keeps its expand state and its
 * query while leaving the tab order and the accessibility tree alike.
 */
export default function ContextDrawer({ open, onClose, children }: Props) {
  const { t } = useTranslation()
  const panel = useRef<HTMLDivElement>(null)
  // pathname AND search: mail names its folder in a search param, so a folder pick — the very
  // thing the drawer exists to do — moves search and leaves pathname alone.
  const { pathname, search } = useLocation()

  // Held in a ref rather than depended on directly: three later tasks call this component, and
  // an inline `onClose={() => setOpen(false)}` gets a new identity every render. Depending on it
  // would re-run the route effect below on every toggle and close the drawer the instant it
  // opened.
  const onCloseRef = useRef(onClose)
  useEffect(() => { onCloseRef.current = onClose })

  // `open` deliberately excluded: including it would re-run this on every toggle and close the
  // drawer the instant it opens, since opening never itself moves pathname/search.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => { if (open) onCloseRef.current() }, [pathname, search])

  // A layer rather than a bare trap plus a listener of its own: the stack gives Escape to the
  // topmost surface, so a dialog opened from a row of the drawer answers it and the column stays.
  useLayer({ active: open, ref: panel, onEscape: onClose })

  return (
    <div className={`context-drawer${open ? ' is-open' : ''}`}>
      <div className="context-drawer-scrim" role="presentation" onClick={onClose} />
      <div className="context-drawer-panel" ref={panel} role="dialog" aria-modal="true"
        aria-label={t('drawer.label')} tabIndex={-1}>
        {children}
      </div>
    </div>
  )
}

/** The hamburger. It lives in whichever header band the module already owns. */
export function DrawerToggle({ onClick }: { onClick: () => void }) {
  const { t } = useTranslation()
  return (
    <button type="button" className="drawer-toggle" aria-label={t('drawer.open')}
      title={t('drawer.open')} onClick={onClick}>
      <MenuIcon size={20} />
    </button>
  )
}

/** The state the three layouts share, so none of them re-derives the tier rule. */
export function useContextDrawer() {
  const inDrawer = useViewport() !== 'desktop'
  // Growing back to desktop must disarm it: the panel goes inline and an open flag would
  // otherwise reopen the drawer the moment the window narrows again. Phone and tablet share a key.
  const [open, setOpen] = useKeyedState(() => false, String(inDrawer))
  const close = useCallback(() => setOpen(false), [setOpen])
  const toggle = useCallback(() => setOpen(value => !value), [setOpen])

  return { inDrawer, open, toggle, close }
}
