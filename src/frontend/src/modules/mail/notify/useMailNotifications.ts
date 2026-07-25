import { useEffect, useRef } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../../../api.js'
import {
  notifiesOf, notifyDesktopOf, notifySoundOf, requestSizeOf, usePreferences,
} from '../../../hooks/usePreferences'
import { flatten } from '../folders/folderNodes'
import { snapshotOf, uidValidityBroke, type FolderSnapshot } from '../list/folderDelta'
import { useAccountId, useFolders } from '../queries'
import { claimNotification, playNewMailSound, showDesktopNotification } from './channels'
import { allArrivalsRead, newSince, notifyBody, notifyDecision } from './notifyDecision'

interface Described {
  body: string
  /** The message to open on click, when exactly one arrived and was found. */
  uid: number | null
  /** Every arrival was already read: a message moved into the inbox, not new mail. */
  silent: boolean
}

/** A baseline is comparable only against the same account and the same inbox folder: carried
    across either, its uidNext describes messages that do not exist here. */
interface Baseline {
  accountId: string
  path: string
  snapshot: FolderSnapshot
}

/** Names the arrivals if it can, and reports a batch that turned out to be already read. The
    count is already known; a failure falls back to counting, and announces rather than guessing
    at flags it could not read. */
async function describeArrivals(
  folder: string, size: number, sinceUid: number, count: number, signal: AbortSignal,
): Promise<Described> {
  try {
    const page = await api.getMailMessages(folder, 0, size, { signal })
    const arrivals = newSince(page.messages, sinceUid)
    return {
      body: notifyBody(arrivals, count),
      uid: count === 1 && arrivals.length === 1 ? arrivals[0].uid : null,
      silent: allArrivalsRead(arrivals, count),
    }
  } catch {
    return { body: notifyBody([], count), uid: null, silent: false }
  }
}

/**
 * Rings for mail arriving in the inbox, whatever the user is looking at. Lives in AppShell,
 * not the mail module, so it also fires from settings and the other sections.
 */
export function useMailNotifications(): void {
  const accountId = useAccountId()
  const navigate = useNavigate()
  const { data: preferences } = usePreferences()
  const sound = preferences ? notifySoundOf(preferences) : false
  const desktop = preferences ? notifyDesktopOf(preferences) : false
  // Nobody asked to be told, so nobody pays for the poll: the shell's own use of the folder
  // query stays off until a setting turns on. /mail enables it separately.
  const { data: folders } = useFolders(preferences ? notifiesOf(preferences) : false)
  const baseline = useRef<Baseline | null>(null)
  const mounted = useRef(true)

  useEffect(() => {
    // Set on mount, not only cleared on unmount: StrictMode's double-invoke runs
    // mount → cleanup → mount on the same ref, and would latch this false for the session.
    mounted.current = true
    return () => { mounted.current = false }
  }, [])

  useEffect(() => {
    // Off, the query is disabled and the tree freezes: kept, the baseline would be hours stale
    // by the time a setting comes back on, and the whole backlog would be announced at once.
    if (!sound && !desktop) {
      baseline.current = null
      return
    }
    if (!folders || !preferences) return

    const inbox = flatten(folders).find(entry => entry.node.specialUse === 'inbox')?.node
    if (!inbox) return

    const snapshot = snapshotOf(inbox)
    const last = baseline.current
    baseline.current = { accountId, path: inbox.path, snapshot }

    // Another account, another folder, or a rebuilt one: the previous uidNext is not comparable
    // with this one, and a leap across it would announce thousands of arrivals.
    const comparable = last !== null && last.accountId === accountId && last.path === inbox.path
      && !uidValidityBroke(last.snapshot, snapshot)

    const decision = notifyDecision(
      comparable ? last.snapshot.uidNext : null, inbox.uidNext, { sound, desktop })
    if (!decision) return

    // Derived rather than read off the node: after a decision this is exactly the new uidNext,
    // and it needs no non-null assertion.
    const arrivedAt = decision.sinceUid + decision.count

    // Both tabs decided to notify; only one may. The bubble would dedupe itself through its
    // tag, the sound would not.
    if (!claimNotification(inbox.uidValidity, arrivedAt)) return

    const controller = new AbortController()
    void describeArrivals(
      inbox.path, requestSizeOf(preferences), decision.sinceUid, decision.count, controller.signal,
    ).then(({ body, uid, silent }) => {
      // Two reasons to hold back: the hook is gone, or the batch turned out to be mail moved in
      // rather than delivered. An aborted fetch still announces, with the count fallback.
      // The claim is banked either way, which is what keeps the other tab quiet too.
      if (!mounted.current || silent) return

      if (sound) playNewMailSound()
      if (!desktop) return

      showDesktopNotification(body, `weesky-mail-${arrivedAt}`, () => {
        window.focus()
        if (uid !== null) {
          navigate(`/mail?folder=${encodeURIComponent(inbox.path)}&uid=${uid}`)
        }
      })
    })

    return () => controller.abort()
  }, [folders, preferences, sound, desktop, accountId, navigate])
}
