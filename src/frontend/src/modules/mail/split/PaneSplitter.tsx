import type { KeyboardEvent, PointerEvent } from 'react'
import { useTranslation } from 'react-i18next'
import ChevronLeftIcon from '../../../icons/ChevronLeftIcon'
import ChevronRightIcon from '../../../icons/ChevronRightIcon'

interface PaneSplitterProps {
  orientation: 'vertical' | 'horizontal'
  size: number
  defaultSize: number
  min: number
  /** What the other pane keeps: the drag ceiling is the parent's span minus this. */
  reserve: number
  onResize: (size: number) => void
  /** Names this seam where a layout draws more than one: two separators reading "Resize the panes"
      are indistinguishable to anything that lists them. Defaults to that generic wording. */
  ariaLabel?: string
  /** Given together: the seam grows a chevron folding the pane it borders away. Withheld by the
      two splits that have nothing to fold — a bar with a dead control on it is worse than none. */
  collapsed?: boolean
  onToggleCollapse?: () => void
}

const NUDGE = 16

/**
 * The draggable bar between two panes. It owns no size — the parent hands one in and hears
 * about changes — so the same bar serves the vertical and horizontal splits.
 */
export default function PaneSplitter(
  {
    orientation, size, defaultSize, min, reserve, onResize, ariaLabel, collapsed, onToggleCollapse,
  }: PaneSplitterProps,
) {
  const { t } = useTranslation('mail')
  const vertical = orientation === 'vertical'

  function startDrag(event: PointerEvent<HTMLDivElement>) {
    event.preventDefault()
    const parent = event.currentTarget.parentElement
    const span = vertical ? parent?.clientWidth : parent?.clientHeight
    // jsdom and a not-yet-laid-out parent both answer 0: no ceiling rather than a crushed pane.
    const limit = span ? Math.max(min, span - reserve) : Number.POSITIVE_INFINITY
    const origin = vertical ? event.clientX : event.clientY
    const base = size

    function move(moveEvent: globalThis.PointerEvent) {
      const at = vertical ? moveEvent.clientX : moveEvent.clientY
      onResize(Math.min(limit, Math.max(min, base + (at - origin))))
    }
    // A touch drag can end in pointercancel (OS scroll takeover, a notification banner)
    // instead of pointerup — without this branch too, the listeners never come off.
    function stop() {
      window.removeEventListener('pointermove', move)
      window.removeEventListener('pointerup', stop)
      window.removeEventListener('pointercancel', stop)
    }
    window.addEventListener('pointermove', move)
    window.addEventListener('pointerup', stop)
    window.addEventListener('pointercancel', stop)
  }

  function nudge(event: KeyboardEvent) {
    const grow = vertical ? 'ArrowRight' : 'ArrowDown'
    const shrink = vertical ? 'ArrowLeft' : 'ArrowUp'
    if (event.key !== grow && event.key !== shrink) return

    event.preventDefault()
    onResize(Math.max(min, size + (event.key === grow ? NUDGE : -NUDGE)))
  }

  return (
    <div
      role="separator"
      aria-orientation={orientation}
      aria-label={ariaLabel ?? t('splitter.label')}
      tabIndex={0}
      className={`pane-splitter is-${orientation}${collapsed ? ' is-collapsed' : ''}`}
      onPointerDown={startDrag}
      onKeyDown={nudge}
      onDoubleClick={() => onResize(defaultSize)}
    >
      {onToggleCollapse && (
        /* Inside the bar rather than beside it, and that is the drag's doing: `startDrag` reads
           `parentElement` for the ceiling, so wrapping the two in a box of their own would hand it
           a 5px span. Both handlers are stopped instead — without them a click on the chevron
           starts a drag, and a double-click on it resets the width it was folding away. */
        <button
          type="button"
          className="pane-collapse"
          aria-label={t(collapsed ? 'splitter.expandFolders' : 'splitter.collapseFolders')}
          aria-expanded={!collapsed}
          onPointerDown={event => event.stopPropagation()}
          onDoubleClick={event => event.stopPropagation()}
          onClick={onToggleCollapse}
        >
          {collapsed ? <ChevronRightIcon size={13} /> : <ChevronLeftIcon size={13} />}
        </button>
      )}
    </div>
  )
}
