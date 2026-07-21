import { useEffect, useRef } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../../../api.js'
import {
  notifyDesktopOf, notifySoundOf, requestSizeOf, usePreferences,
} from '../../../hooks/usePreferences'
import { flatten } from '../folders/folderNodes'
import { snapshotOf, uidValidityBroke, type FolderSnapshot } from '../list/folderDelta'
import { useAccountId, useFolders } from '../queries'
import { claimNotification, playNewMailSound, showDesktopNotification } from './channels'
import { newSince, notifyBody, notifyDecision } from './notifyDecision'

interface Described {
  body: string
  /** The message to open on click, when exactly one arrived and was found. */
  uid: number | null
}

/** What a baseline is worth only for the folder it was taken from, on the account it was taken
    on: carried across either, its uidNext describes messages that do not exist here. */
interface Baseline {
  accountId: string
  path: string
  snapshot: FolderSnapshot
}

/** Names the arrivals if it can. The count is already known; this only improves the wording,
    so any failure falls back to counting rather than surfacing. */
async function describeArrivals(
  folder: string, size: number, sinceUid: number, count: number, signal: AbortSignal,
): Promise<Described> {
  try {
    const page = await api.getMailMessages(folder, 0, size, { signal })
    const arrivals = newSince(page.messages, sinceUid)
    return {
      body: notifyBody(arrivals, count),
      uid: count === 1 && arrivals.length === 1 ? arrivals[0].uid : null,
    }
  } catch {
    return { body: notifyBody([], count), uid: null }
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
  const { data: folders } = useFolders(sound || desktop)
  const baseline = useRef<Baseline | null>(null)

  useEffect(() => {
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
    if (!claimNotification(arrivedAt)) return

    const controller = new AbortController()
    void describeArrivals(
      inbox.path, requestSizeOf(preferences), decision.sinceUid, decision.count, controller.signal,
    ).then(({ body, uid }) => {
      // Logged out, or the account changed, while the description was in flight.
      if (controller.signal.aborted) return

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
