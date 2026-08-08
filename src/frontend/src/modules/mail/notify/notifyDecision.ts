import i18next from 'i18next'
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

/**
 * A message moved into the inbox is appended with a fresh uid, so uidNext advances exactly as it
 * does for real delivery — only the flags tell the two apart, and an already-read arrival is not
 * new mail. Silence a batch only when the fetched page carried it whole: read messages among a
 * partial page say nothing about the arrivals it did not hold.
 */
export function allArrivalsRead(arrivals: MailMessageSummary[], count: number): boolean {
  return arrivals.length === count && arrivals.every(message => message.seen)
}

export function notifyBody(messages: MailMessageSummary[], count: number): string {
  if (count === 1 && messages.length === 1) {
    const [message] = messages
    const subject = message.subject || i18next.t('mail:list.noSubject')
    return `${message.fromName || message.fromAddress} — ${subject}`
  }

  return i18next.t('mail:notify.newMessages', { count })
}
