import { useState, useCallback, useEffect, useRef } from 'react'

const DISMISS_MS = 3000
/** An actionable toast has to be read and acted on, not just noticed. */
const DISMISS_WITH_ACTION_MS = 8000

/** A counter, not Date.now(): a send raises its own toast and then the capture's, so two within a
    millisecond is routine, and equal ids give React two children under one key. */
let nextToastId = 0

export interface ToastAction {
  label: string
  onClick: () => void
}

export interface ToastOptions {
  /** Replaces the 3 s / 8 s default. */
  durationMs?: number
  /** A bar that empties over the duration, paused with the toast. */
  countdown?: boolean
  /** Runs when the toast times out — never when it is dismissed or its action is used. */
  onExpire?: () => void
}

export interface Toast {
  id: number
  message: string
  type: 'success' | 'error'
  action?: ToastAction
  durationMs?: number
  countdown?: boolean
}

export type AddToast = (message: string, type?: Toast['type'], action?: ToastAction, options?: ToastOptions) => number

interface TimerEntry {
  timeoutId: ReturnType<typeof setTimeout> | null
  remaining: number
  startedAt: number | null
  onExpire?: () => void
}

export function useToasts() {
  const [toasts, setToasts] = useState<Toast[]>([])
  // Cleared on unmount: a timer firing past it reaches a gone page (in a test, a torn-down jsdom).
  // Each entry keeps the time left and when it was armed, for a pause; an error toast has none.
  const timers = useRef(new Map<number, TimerEntry>())

  const clearTimer = useCallback((id: number) => {
    const entry = timers.current.get(id)
    if (entry === undefined) return
    if (entry.timeoutId !== null) clearTimeout(entry.timeoutId)
    timers.current.delete(id)
  }, [])

  useEffect(() => () => {
    for (const entry of timers.current.values()) {
      if (entry.timeoutId !== null) clearTimeout(entry.timeoutId)
    }
    timers.current.clear()
  }, [])

  const removeToast = useCallback((id: number) => {
    clearTimer(id)
    setToasts(prev => prev.filter(t => t.id !== id))
  }, [clearTimer])

  const arm = useCallback((id: number, delay: number, onExpire?: () => void) => {
    const timeoutId = setTimeout(() => {
      timers.current.delete(id)
      setToasts(prev => prev.filter(t => t.id !== id))
      onExpire?.()
    }, delay)
    timers.current.set(id, { timeoutId, remaining: delay, startedAt: Date.now(), onExpire })
  }, [])

  const addToast: AddToast = useCallback((message, type = 'success', action, options) => {
    const id = ++nextToastId
    setToasts(prev => [...prev, {
      id, message, type, action, durationMs: options?.durationMs, countdown: options?.countdown,
    }])
    if (type !== 'error') {
      arm(id, options?.durationMs ?? (action ? DISMISS_WITH_ACTION_MS : DISMISS_MS), options?.onExpire)
    }
    return id
  }, [arm])

  // WCAG 2.2.1's "no timing" escape hatch. An id with no entry here — an error toast, which
  // never arms one, or one already removed — has nothing to pause; this is then a no-op.
  const pauseToast = useCallback((id: number) => {
    const entry = timers.current.get(id)
    if (entry === undefined || entry.timeoutId === null || entry.startedAt === null) return
    clearTimeout(entry.timeoutId)
    const elapsed = Date.now() - entry.startedAt
    timers.current.set(id, {
      timeoutId: null, remaining: Math.max(0, entry.remaining - elapsed), startedAt: null, onExpire: entry.onExpire,
    })
  }, [])

  const resumeToast = useCallback((id: number) => {
    const entry = timers.current.get(id)
    if (entry === undefined || entry.timeoutId !== null) return
    arm(id, entry.remaining, entry.onExpire)
  }, [arm])

  return { toasts, addToast, removeToast, pauseToast, resumeToast }
}
