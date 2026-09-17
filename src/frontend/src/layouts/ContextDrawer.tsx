import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { useLocation } from 'react-router-dom'
import MenuIcon from '../icons/MenuIcon'
import { useDialogFocusTrap } from '../hooks/useDialogFocusTrap'
import { useViewport } from '../hooks/useViewport'
import { hasOpenLayer } from '../lib/layerStack'

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

  useDialogFocusTrap(panel, { active: open })

  useEffect(() => {
    if (!open) return
    // A dialog opened from inside the drawer owns Escape; the column stays under it.
    function onKey(event: KeyboardEvent) {
      if (event.key === 'Escape' && !event.defaultPrevented && !hasOpenLayer()) onClose()
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [open, onClose])

  return (
    <div className={`context-drawer${open ? ' is-open' : ''}`}>
      <div className="context-drawer-scrim" onClick={onClose} />
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
  const [open, setOpen] = useState(false)
  const close = useCallback(() => setOpen(false), [])
  const toggle = useCallback(() => setOpen(value => !value), [])

  // Growing back to desktop must disarm it: the panel goes inline and an open flag would
  // otherwise reopen the drawer the moment the window narrows again.
  useEffect(() => { if (!inDrawer) setOpen(false) }, [inDrawer])

  return { inDrawer, open, toggle, close }
}
