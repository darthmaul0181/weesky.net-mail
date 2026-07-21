import { useEffect, useRef } from 'react'
import { useNavigate } from 'react-router-dom'
import { useQueryClient, type InfiniteData } from '@tanstack/react-query'
import { api } from '../../../api.js'
import {
  isStreaming, notifyDesktopOf, notifySoundOf, requestSizeOf, usePreferences,
} from '../../../hooks/usePreferences'
import type { MailFolderPage } from '../api/mailTypes'
import { flatten } from '../folders/folderNodes'
import { mailKeys, useAccountId, useFolders } from '../queries'
import { claimNotification, playNewMailSound, showDesktopNotification } from './channels'
import { newSince, notifyBody, notifyDecision } from './notifyDecision'

interface Described {
  body: string
  /** The message to open on click, when exactly one arrived and was found. */
  uid: number | null
}

/** Names the arrivals if it can. The count is already known; this only improves the wording,
    so any failure falls back to counting rather than surfacing. */
async function describeArrivals(
  fetchPage: () => Promise<MailFolderPage>, sinceUid: number, count: number,
): Promise<Described> {
  try {
    const page = await fetchPage()
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
  const client = useQueryClient()
  const navigate = useNavigate()
  const { data: folders } = useFolders()
  const { data: preferences } = usePreferences()
  const previousUidNext = useRef<number | null>(null)
  const seenInbox = useRef(false)

  useEffect(() => {
    if (!folders || !preferences) return

    const inbox = flatten(folders).find(entry => entry.node.specialUse === 'inbox')?.node
    if (!inbox) return

    const previous = seenInbox.current ? previousUidNext.current : null
    seenInbox.current = true
    previousUidNext.current = inbox.uidNext

    const decision = notifyDecision(previous, inbox.uidNext, {
      sound: notifySoundOf(preferences),
      desktop: notifyDesktopOf(preferences),
    })
    if (!decision) return

    // Derived rather than read off the node: after a decision this is exactly the new uidNext,
    // and it needs no non-null assertion.
    const arrivedAt = decision.sinceUid + decision.count

    // Both tabs decided to notify; only one may. The bubble would dedupe itself through its
    // tag, the sound would not.
    if (!claimNotification(arrivedAt)) return

    // The inbox's first page is already in hand when the user is looking at it — the list
    // refresh has just fetched it. The key differs by mode, hence the branch.
    const size = requestSizeOf(preferences)
    const cached = isStreaming(preferences)
      ? client.getQueryData<InfiniteData<MailFolderPage>>(
          mailKeys.messageStream(accountId, inbox.path, size))?.pages[0]
      : client.getQueryData<MailFolderPage>(mailKeys.messages(accountId, inbox.path, 0, size))

    void describeArrivals(
      () => cached ? Promise.resolve(cached) : api.getMailMessages(inbox.path, 0, size),
      decision.sinceUid,
      decision.count,
    ).then(({ body, uid }) => {
      if (notifySoundOf(preferences)) playNewMailSound()
      if (!notifyDesktopOf(preferences)) return

      showDesktopNotification(body, `weesky-mail-${arrivedAt}`, () => {
        window.focus()
        if (uid !== null) {
          navigate(`/mail?folder=${encodeURIComponent(inbox.path)}&uid=${uid}`)
        }
      })
    })
  }, [folders, preferences, accountId, client, navigate])
}
