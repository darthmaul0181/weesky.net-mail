import { useState, useCallback } from 'react'

const DISMISS_MS = 3000
/** An actionable toast has to be read and acted on, not just noticed. */
const DISMISS_WITH_ACTION_MS = 8000

/** A counter, not Date.now(): a send raises its own toast and then the capture's, so two within a
    millisecond is routine, and equal ids give React two children under one key. */
let nextToastId = 0

export function useToasts() {
  const [toasts, setToasts] = useState([])

  const removeToast = useCallback((id) => {
    setToasts(prev => prev.filter(t => t.id !== id))
  }, [])

  const addToast = useCallback((message, type = 'success', action) => {
    const id = ++nextToastId
    setToasts(prev => [...prev, { id, message, type, action }])
    if (type !== 'error') {
      const delay = action ? DISMISS_WITH_ACTION_MS : DISMISS_MS
      setTimeout(() => setToasts(prev => prev.filter(t => t.id !== id)), delay)
    }
  }, [])

  return { toasts, addToast, removeToast }
}
