import { useState, useCallback, useEffect, useRef } from 'react'

const DISMISS_MS = 3000
/** An actionable toast has to be read and acted on, not just noticed. */
const DISMISS_WITH_ACTION_MS = 8000

/** A counter, not Date.now(): a send raises its own toast and then the capture's, so two within a
    millisecond is routine, and equal ids give React two children under one key. */
let nextToastId = 0

export function useToasts() {
  const [toasts, setToasts] = useState([])
  // A dismissal outlives nothing: a timer left running past the unmount fires into a page that is
  // gone, and in a test into a torn-down jsdom, where React reaches for a window that no longer is.
  const timers = useRef(new Map())

  const clearTimer = useCallback((id) => {
    const timer = timers.current.get(id)
    if (timer === undefined) return
    clearTimeout(timer)
    timers.current.delete(id)
  }, [])

  useEffect(() => () => {
    for (const timer of timers.current.values()) clearTimeout(timer)
    timers.current.clear()
  }, [])

  const removeToast = useCallback((id) => {
    clearTimer(id)
    setToasts(prev => prev.filter(t => t.id !== id))
  }, [clearTimer])

  const addToast = useCallback((message, type = 'success', action) => {
    const id = ++nextToastId
    setToasts(prev => [...prev, { id, message, type, action }])
    if (type === 'error') return
    const delay = action ? DISMISS_WITH_ACTION_MS : DISMISS_MS
    timers.current.set(id, setTimeout(() => {
      timers.current.delete(id)
      setToasts(prev => prev.filter(t => t.id !== id))
    }, delay))
  }, [])

  return { toasts, addToast, removeToast }
}
