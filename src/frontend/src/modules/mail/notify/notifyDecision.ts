import type { MailMessageSummary } from '../api/mailTypes'

export interface NotifySettings {
  sound: boolean
  desktop: boolean
}

export interface NotifyDecision {
  count: number
  /** The uidNext before the arrivals: every new message has a uid at least this high. */
  sinceUid: number
}

/**
 * Whether this poll tick should notify, and about how many messages. uidNext alone decides:
 * a deletion or a read-flip made in another client moves the other counters, not this one.
 */
export function notifyDecision(
  previousUidNext: number | null,
  nextUidNext: number | null,
  settings: NotifySettings,
): NotifyDecision | null {
  if (!settings.sound && !settings.desktop) return null
  if (previousUidNext === null || nextUidNext === null) return null
  if (nextUidNext <= previousUidNext) return null

  return { count: nextUidNext - previousUidNext, sinceUid: previousUidNext }
}

/** The arrivals, by uid rather than by position: the list is sorted by Date header, so a
    late-delivered message sits mid-list and the top rows are not the new ones. */
export function newSince(messages: MailMessageSummary[], sinceUid: number): MailMessageSummary[] {
  return messages.filter(message => message.uid >= sinceUid)
}

export function notifyBody(messages: MailMessageSummary[], count: number): string {
  if (count === 1 && messages.length === 1) {
    const [message] = messages
    return `${message.fromName || message.fromAddress} — ${message.subject || '(no subject)'}`
  }

  return count === 1 ? '1 new message' : `${count} new messages`
}
