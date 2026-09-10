import type { PointerEvent as ReactPointerEvent } from 'react'

/** Under this a press is a click and the bubble opens; past it the pointer is dragging. */
export const THRESHOLD_PX = 4

export interface PointerTrack {
  /** Only past the threshold: everything under it is still a click. */
  move(dx: number, dy: number, at: PointerEvent): void
  drop(moved: boolean): void
  cancel(): void
}

function swallow(event: Event) {
  event.preventDefault()
  event.stopPropagation()
}

/**
 * The click the browser fires on the element a drag ended on. Without it the drop would reopen
 * the bubble over the block it has just moved — and it is disarmed on the next task rather than
 * left waiting, so a gesture that no click followed cannot swallow a later one.
 */
function suppressClick(target: HTMLElement) {
  target.addEventListener('click', swallow, { capture: true, once: true })
  setTimeout(() => target.removeEventListener('click', swallow, { capture: true }), 0)
}

/**
 * The plumbing the grid's three gestures share: the threshold that keeps a click a click, pointer
 * capture, Escape and `pointercancel` as abandonment, and listeners that always come off. It
 * returns its own stopper, so a hook can drop a gesture that outlived its component.
 */
export function trackPointer(event: ReactPointerEvent, on: PointerTrack): () => void {
  const originX = event.clientX
  const originY = event.clientY
  const { pointerId } = event
  const target = event.currentTarget as HTMLElement
  let moved = false

  function stop() {
    window.removeEventListener('pointermove', onMove)
    window.removeEventListener('pointerup', onUp)
    window.removeEventListener('pointercancel', onCancel)
    document.removeEventListener('keydown', onKey)
    if (target.hasPointerCapture?.(pointerId)) target.releasePointerCapture(pointerId)
  }

  function onMove(at: PointerEvent) {
    const dx = at.clientX - originX
    const dy = at.clientY - originY
    if (!moved) {
      if (Math.hypot(dx, dy) < THRESHOLD_PX) return
      moved = true
      // The gesture follows the pointer off the chip, out of the column and off the window.
      target.setPointerCapture?.(pointerId)
    }
    on.move(dx, dy, at)
  }

  function onUp() {
    stop()
    if (moved) suppressClick(target)
    on.drop(moved)
  }

  // Abandoned, not dropped — but the release still fires its click on the chip, which would
  // reopen the bubble on the very block the user has just given up moving.
  function onCancel() {
    stop()
    if (moved) suppressClick(target)
    on.cancel()
  }

  function onKey(key: KeyboardEvent) {
    if (key.key === 'Escape') onCancel()
  }

  window.addEventListener('pointermove', onMove)
  window.addEventListener('pointerup', onUp)
  window.addEventListener('pointercancel', onCancel)
  document.addEventListener('keydown', onKey)
  return stop
}
