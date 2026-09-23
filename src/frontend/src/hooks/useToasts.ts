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

export interface Toast {
  id: number
  message: string
  type: 'success' | 'error'
  action?: ToastAction
}

export type AddToast = (message: string, type?: Toast['type'], action?: ToastAction) => void

interface TimerEntry {
  timeoutId: ReturnType<typeof setTimeout> | null
  remaining: number
  startedAt: number | null
}

export function useToasts() {
  const [toasts, setToasts] = useState<Toast[]>([])
  // A dismissal outlives nothing: a timer left running past the unmount fires into a page that is
  // gone, and in a test into a torn-down jsdom, where React reaches for a window that no longer is.
  // Each entry also carries what a pause needs to resume correctly: the time left when it was
  // armed and when that arming started — an error toast, which never arms one, has no entry here.
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

  const arm = useCallback((id: number, delay: number) => {
    const timeoutId = setTimeout(() => {
      timers.current.delete(id)
      setToasts(prev => prev.filter(t => t.id !== id))
    }, delay)
    timers.current.set(id, { timeoutId, remaining: delay, startedAt: Date.now() })
  }, [])

  const addToast: AddToast = useCallback((message, type = 'success', action) => {
    const id = ++nextToastId
    setToasts(prev => [...prev, { id, message, type, action }])
    if (type === 'error') return
    arm(id, action ? DISMISS_WITH_ACTION_MS : DISMISS_MS)
  }, [arm])

  // WCAG 2.2.1's "no timing" escape hatch. An id with no entry here — an error toast, which
  // never arms one, or one already removed — has nothing to pause; this is then a no-op.
  const pauseToast = useCallback((id: number) => {
    const entry = timers.current.get(id)
    if (entry === undefined || entry.timeoutId === null || entry.startedAt === null) return
    clearTimeout(entry.timeoutId)
    const elapsed = Date.now() - entry.startedAt
    timers.current.set(id, { timeoutId: null, remaining: Math.max(0, entry.remaining - elapsed), startedAt: null })
  }, [])

  const resumeToast = useCallback((id: number) => {
    const entry = timers.current.get(id)
    if (entry === undefined || entry.timeoutId !== null) return
    arm(id, entry.remaining)
  }, [arm])

  return { toasts, addToast, removeToast, pauseToast, resumeToast }
}
