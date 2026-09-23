import { useCallback, useEffect, useRef, useState } from 'react'
import { useBlocker, useNavigate } from 'react-router'
import { registerLeaveGuard } from '../../../lib/leaveGuard'

// A router blocker owns every exit (folder click, ✕, Back) and beforeunload the tab. `dirty` is the
// composer's non-empty clause gated on "changed since open or the last save".
export function useLeaveGuard(opensChanged: boolean, hasContent: boolean, backTarget: string) {
  const navigate = useNavigate()
  // "Changed since open or the last save". A resumed draft opens clean — its content is
  // already filed; any other seed opens changed, because its content exists nowhere else.
  const [changed, setChanged] = useState(opensChanged)

  // The blocker runs inside a navigation that can fire in the click that dirtied the form, before any
  // passive effect: the dirtying callbacks set the ref up front. A From choice counts, since on a
  // resumed draft it can be the only change, and dropping it would send from the wrong address.
  const dirty = changed && hasContent
  const dirtyRef = useRef(dirty)
  const leavingRef = useRef(false)
  useEffect(() => { dirtyRef.current = dirty }, [dirty])
  const markDirty = useCallback(() => { dirtyRef.current = true; setChanged(true) }, [])
  const markClean = useCallback(() => { setChanged(false); dirtyRef.current = false }, [])

  const blocker = useBlocker(useCallback(() => dirtyRef.current && !leavingRef.current, []))

  // The same question, asked by something the router cannot see — today, the mailbox switch in
  // the identity menu. It resolves on the dialog's buttons, so both roads end in one prompt.
  const [leaveAsk, setLeaveAsk] = useState<((ok: boolean) => void) | null>(null)

  useEffect(() => {
    registerLeaveGuard(() => dirtyRef.current && !leavingRef.current
      ? new Promise<boolean>(resolve => setLeaveAsk(() => resolve))
      : Promise.resolve(true))
    return () => registerLeaveGuard(null)
  }, [])

  useEffect(() => {
    function onBeforeUnload(event: BeforeUnloadEvent) {
      if (dirtyRef.current && !leavingRef.current) event.preventDefault()
    }
    window.addEventListener('beforeunload', onBeforeUnload)
    return () => window.removeEventListener('beforeunload', onBeforeUnload)
  }, [])

  const leave = useCallback(() => {
    leavingRef.current = true
    void navigate(backTarget)
  }, [navigate, backTarget])

  // The dialog serves two callers, so its buttons answer whichever one opened it: the blocker
  // holds a navigation to release, the guard holds a promise to settle.
  function keepEditing() {
    if (leaveAsk) { setLeaveAsk(null); leaveAsk(false); return }
    blocker.reset?.()
  }

  function leaveBehind() {
    leavingRef.current = true
    if (leaveAsk) { setLeaveAsk(null); leaveAsk(true); void navigate(backTarget); return }
    blocker.proceed?.()
  }

  const asking = blocker.state === 'blocked' || leaveAsk !== null
  return { dirty, markDirty, markClean, leave, asking, keepEditing, leaveBehind }
}
