import { useCallback, useEffect, useRef, useState } from 'react'
import type { Blocker } from 'react-router'
import { registerLeaveGuard } from '../../../lib/leaveGuard'

/** Both modes leave through `onLeave`. A page's is a navigation, which its blocker asks about; a
    window's is its owner's callback, so a window never blocks a navigation and asks on its ✕ itself. */
export type LeaveExit = {
  mode: 'route' | 'callback'; onLeave: () => void
  /** Releases the staged files once leaving is settled; never while `busy` may still read them. */
  onRelease?: () => void
}

// A router blocker owns every page exit (folder click, ✕, Back) and beforeunload the tab. `dirty` is
// the composer's non-empty clause gated on "changed since open or the last save". The blocker lives
// in `LeaveBlocker`, which the page renders with `blockerProps`; a window gets none.
export function useLeaveGuard(
  opensChanged: boolean, hasContent: boolean, { mode, onLeave, onRelease }: LeaveExit,
) {
  // "Changed since open or the last save". A resumed draft opens clean — its content is
  // already filed; any other seed opens changed, because its content exists nowhere else.
  const [changed, setChanged] = useState(opensChanged)

  // The blocker runs inside a navigation that can fire in the click that dirtied the form, before any
  // passive effect: the dirtying callbacks set the ref up front. A From choice counts, since on a
  // resumed draft it can be the only change, and dropping it would send from the wrong address.
  const dirty = changed && hasContent
  const dirtyRef = useRef(dirty)
  const leavingRef = useRef(false)
  const routed = mode === 'route'
  useEffect(() => { dirtyRef.current = dirty }, [dirty])
  const markDirty = useCallback(() => { dirtyRef.current = true; setChanged(true) }, [])
  const markClean = useCallback(() => { setChanged(false); dirtyRef.current = false }, [])

  const [blocker, setBlocker] = useState<Blocker | null>(null)
  const shouldBlock = useCallback(() => dirtyRef.current && !leavingRef.current, [])

  // The same question, asked by something the router cannot see — the mailbox switch in the
  // identity menu, the sign-out. It resolves on the dialog's buttons, so all roads end in one prompt.
  const [leaveAsk, setLeaveAsk] = useState<((ok: boolean) => void) | null>(null)
  // A callback exit has no blocker to hold its ✕, so the question is opened by hand.
  const [closeAsk, setCloseAsk] = useState(false)

  // A send or a save in flight is answered first: replacing the form under it would drop its
  // callbacks, the sent toast and the filed draft's deletion with them. The owner reports it through
  // `setBusy`, its busy state being built from hooks that need `markDirty` first.
  const busyRef = useRef(false)
  const idleWaiters = useRef<(() => void)[]>([])
  const setBusy = useCallback((value: boolean) => {
    busyRef.current = value
    if (!value) idleWaiters.current.splice(0).forEach(wake => wake())
  }, [])

  // Set by an answer that lets the composer go; carried out by the commit, once every guard agreed.
  const settledRef = useRef(false)
  const releaseRef = useRef(onRelease)
  useEffect(() => { releaseRef.current = onRelease })
  const leaveRef = useRef(() => {})

  useEffect(() => {
    let alive = true
    const waiters = idleWaiters.current
    const withdraw = registerLeaveGuard(async () => {
      settledRef.current = false
      if (busyRef.current) await new Promise<void>(wake => idleWaiters.current.push(wake))
      if (!alive || leavingRef.current) return true
      // A clean window goes with whatever is asking; a clean page stays where it is.
      if (!dirtyRef.current) { settledRef.current = !routed; return true }
      return new Promise<boolean>(resolve => setLeaveAsk(() => resolve))
    }, () => {
      if (!settledRef.current || leavingRef.current) return
      releaseRef.current?.()
      leaveRef.current()
    })
    return () => {
      alive = false
      withdraw()
      waiters.splice(0).forEach(wake => wake())
    }
  }, [routed])

  useEffect(() => {
    function onBeforeUnload(event: BeforeUnloadEvent) {
      if (dirtyRef.current && !leavingRef.current) event.preventDefault()
    }
    window.addEventListener('beforeunload', onBeforeUnload)
    return () => window.removeEventListener('beforeunload', onBeforeUnload)
  }, [])

  const leave = useCallback(() => {
    leavingRef.current = true
    onLeave()
  }, [onLeave])
  useEffect(() => { leaveRef.current = leave }, [leave])

  // A page's ✕ is a navigation like any other, so its blocker asks; a window has none to ask.
  function requestClose() {
    if (routed) onLeave()
    else if (dirtyRef.current) setCloseAsk(true)
    else leave()
  }

  // The dialog serves three callers, so its buttons answer whichever one opened it: the blocker
  // holds a navigation to release, the guard holds a promise to settle, a window's ✕ holds nothing.
  function keepEditing() {
    if (leaveAsk) { setLeaveAsk(null); leaveAsk(false); return }
    if (closeAsk) { setCloseAsk(false); return }
    blocker?.reset?.()
  }

  // An outside asker's answer waits for the commit: a later guard may still refuse the round.
  function leaveBehind() {
    if (leaveAsk) { setLeaveAsk(null); settledRef.current = true; leaveAsk(true); return }
    onRelease?.()
    if (closeAsk) { setCloseAsk(false); leave(); return }
    leavingRef.current = true
    blocker?.proceed?.()
  }

  const asking = blocker?.state === 'blocked' || leaveAsk !== null || closeAsk
  const blockerProps = routed ? { shouldBlock, onBlocker: setBlocker } : null
  return {
    dirty, markDirty, markClean, leave, asking, keepEditing, leaveBehind, requestClose, blockerProps, setBusy,
  }
}
