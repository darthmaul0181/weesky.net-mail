import { useCallback, useEffect, useLayoutEffect, useRef } from 'react'
import type { AddToast } from '../../../hooks/useToasts'
import { createHold, type Hold } from '../hold'

export const UNDO_MS = 5000

interface Pending { hold: Hold; toastId: number }

/** One Undo at a time. The send follows the toast's own expiry, so a toast paused under the
 * keyboard also holds the move back. */
export function useDeferredMove({ notify, dismiss, flushKey }: {
  notify?: AddToast
  dismiss?: (id: number) => void
  /** Any change sends the pending move: folder, page, search, account. */
  flushKey: string
}) {
  const pending = useRef<Pending | null>(null)
  // Read through a ref: a caller's fresh handlers must not change flush, whose cleanup sends.
  const handlers = useRef({ notify, dismiss })
  useLayoutEffect(() => { handlers.current = { notify, dismiss } })

  const flush = useCallback(() => {
    const current = pending.current
    if (!current) return
    pending.current = null
    handlers.current.dismiss?.(current.toastId)
    current.hold.release()
  }, [])

  const start = useCallback((message: string, undoLabel: string) => {
    flush()
    const hold = createHold()
    const { notify } = handlers.current
    if (!notify) { hold.release(); return hold.promise }
    const entry: Pending = { hold, toastId: 0 }
    const settle = (run: () => void) => () => {
      if (pending.current === entry) pending.current = null
      run()
    }
    pending.current = entry
    entry.toastId = notify(message, 'success', { label: undoLabel, onClick: settle(hold.cancel) },
      { durationMs: UNDO_MS, countdown: true, onExpire: settle(hold.release) })
    return hold.promise
  }, [flush])

  // The key is only read to rerun the cleanup: a new folder, page, search or account sends.
  useEffect(() => flush, [flush, flushKey])

  useEffect(() => {
    const onHidden = () => { if (document.visibilityState === 'hidden') flush() }
    document.addEventListener('visibilitychange', onHidden)
    window.addEventListener('pagehide', flush)
    return () => {
      document.removeEventListener('visibilitychange', onHidden)
      window.removeEventListener('pagehide', flush)
    }
  }, [flush])

  return { start }
}
