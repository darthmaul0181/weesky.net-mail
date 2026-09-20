import { useState, type KeyboardEvent, type PointerEvent } from 'react'
import { useTranslation } from 'react-i18next'

interface PaneSplitterProps {
  orientation: 'vertical' | 'horizontal'
  size: number
  defaultSize: number
  min: number
  /** What the other pane keeps: the drag ceiling is the parent's span minus this. */
  reserve: number
  onResize: (size: number) => void
}

const NUDGE = 16

/**
 * The draggable bar between two panes. It owns no size — the parent hands one in and hears
 * about changes — so the same bar serves the vertical and horizontal splits.
 */
export default function PaneSplitter(
  { orientation, size, defaultSize, min, reserve, onResize }: PaneSplitterProps,
) {
  const { t } = useTranslation('mail')
  const vertical = orientation === 'vertical'
  // A callback ref rather than a plain one: mount sets state, which re-renders with the node in
  // hand — the only way to read the parent's real span, which does not exist before that commit.
  const [node, setNode] = useState<HTMLDivElement | null>(null)

  // Shared by the drag and the arrow keys, so neither can crush the other pane past what the
  // parent actually has to give.
  function ceilingOf(element: HTMLElement): number {
    const parent = element.parentElement
    const span = vertical ? parent?.clientWidth : parent?.clientHeight
    // jsdom and a not-yet-laid-out parent both answer 0: no ceiling rather than a crushed pane.
    return span ? Math.max(min, span - reserve) : Number.POSITIVE_INFINITY
  }

  function startDrag(event: PointerEvent<HTMLDivElement>) {
    event.preventDefault()
    const limit = ceilingOf(event.currentTarget)
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

  function nudge(event: KeyboardEvent<HTMLDivElement>) {
    const grow = vertical ? 'ArrowRight' : 'ArrowDown'
    const shrink = vertical ? 'ArrowLeft' : 'ArrowUp'
    if (event.key !== grow && event.key !== shrink) return

    event.preventDefault()
    const grown = size + (event.key === grow ? NUDGE : -NUDGE)
    onResize(Math.min(ceilingOf(event.currentTarget), Math.max(min, grown)))
  }

  // Recomputed on every render, so a size the parent hands back down keeps it current with no
  // separate effect to go stale; jsdom's and an unmounted parent's 0 span both mean no ceiling.
  const ceiling = node ? ceilingOf(node) : undefined
  const max = ceiling !== undefined && Number.isFinite(ceiling) ? ceiling : undefined

  return (
    <div
      ref={setNode}
      role="separator"
      aria-orientation={orientation}
      aria-label={t('splitter.label')}
      aria-valuenow={size}
      aria-valuemin={min}
      aria-valuemax={max}
      tabIndex={0}
      className={`pane-splitter is-${orientation}`}
      onPointerDown={startDrag}
      onKeyDown={nudge}
      onDoubleClick={() => onResize(defaultSize)}
    />
  )
}
