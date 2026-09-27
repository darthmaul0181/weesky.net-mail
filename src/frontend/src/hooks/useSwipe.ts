import { useCallback, useRef, useState } from 'react'
import type { PointerEvent } from 'react'
import { GESTURE_TRAVEL_PX } from './gestureThresholds'

export type SwipeSide = 'left' | 'right'
export type SwipeOutcome = 'leave' | 'return'

export const SWIPE_THRESHOLD = 0.35
const SNAP_MS = 200

export interface SwipeState {
  side: SwipeSide | null
  armed: boolean
}

const IDLE: SwipeState = { side: null, armed: false }

interface Gesture { x: number; y: number; width: number; dragging: boolean }

const reducedMotion = () => window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false

/** A horizontal drag by touch or pen; the mouse never swipes. The element moves by an inline
 * transform, so a finger's travel costs no render: React hears of the side and the arming only. */
export function useSwipe<E extends HTMLElement>(
  canSwipe: (side: SwipeSide) => boolean,
  onCommit: (side: SwipeSide) => SwipeOutcome,
) {
  const ref = useRef<E | null>(null)
  const [state, setState] = useState<SwipeState>(IDLE)
  const shown = useRef<SwipeState>(IDLE)
  const gesture = useRef<Gesture | null>(null)
  const dragged = useRef(false)

  const show = useCallback((next: SwipeState) => {
    if (next.side === shown.current.side && next.armed === shown.current.armed) return
    shown.current = next
    setState(next)
  }, [])

  const place = useCallback((dx: number, animate: boolean) => {
    const el = ref.current
    if (!el) return
    el.style.transition = animate && !reducedMotion() ? `transform ${SNAP_MS}ms ease-out` : 'none'
    el.style.transform = dx === 0 ? '' : `translateX(${dx}px)`
  }, [])

  const settle = (dx: number) => {
    gesture.current = null
    place(dx, true)
    if (dx === 0) show(IDLE)
  }

  const handlers = {
    onPointerDown: (event: PointerEvent) => {
      // A second finger ends the drag it interrupts, or the primary's moves find no gesture.
      if (gesture.current?.dragging) settle(0)
      dragged.current = false
      gesture.current = null
      if ((event.pointerType !== 'touch' && event.pointerType !== 'pen')
        || !event.isPrimary || event.button !== 0) return
      gesture.current = {
        x: event.clientX, y: event.clientY, width: ref.current?.clientWidth ?? 0, dragging: false,
      }
    },
    onPointerMove: (event: PointerEvent) => {
      const g = gesture.current
      if (!g) return
      const dx = event.clientX - g.x
      const dy = event.clientY - g.y
      if (!g.dragging) {
        if (Math.hypot(dx, dy) <= GESTURE_TRAVEL_PX) return
        // The first decisive move names the gesture for good: a scroll drifting sideways stays one.
        if (Math.abs(dx) <= Math.abs(dy) || !canSwipe(dx > 0 ? 'right' : 'left')) {
          gesture.current = null
          return
        }
        g.dragging = true
        dragged.current = true
        ref.current?.setPointerCapture?.(event.pointerId)
      }
      const side: SwipeSide = dx > 0 ? 'right' : 'left'
      const offset = canSwipe(side) ? dx : 0
      place(offset, false)
      show(offset === 0 ? IDLE
        : { side, armed: g.width > 0 && Math.abs(offset) >= g.width * SWIPE_THRESHOLD })
    },
    onPointerUp: () => {
      const g = gesture.current
      if (!g?.dragging) { gesture.current = null; return }
      const { side, armed } = shown.current
      if (!side || !armed) { settle(0); return }
      const leaves = onCommit(side) === 'leave'
      settle(leaves ? (side === 'right' ? g.width : -g.width) : 0)
    },
    onPointerCancel: () => {
      if (gesture.current?.dragging) settle(0)
      else gesture.current = null
    },
  }

  const swallowClick = () => {
    const was = dragged.current
    dragged.current = false
    return was
  }

  return { ref, state, handlers, swallowClick }
}
