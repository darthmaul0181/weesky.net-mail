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

// uidNext alone decides: a deletion or a read-flip in another client moves the other counters.
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

// A read message moved into the inbox advances uidNext like delivery; its flags tell them apart when
// the page carried the batch whole, `unreadDelta` when it did not (a real delivery tops the page, so
// never lands here). A counter that cannot be compared buys no silence: announcing beats swallowing.
export function silentBatch(
  arrivals: MailMessageSummary[], count: number, unreadDelta: number | null,
): boolean {
  if (arrivals.length === count) return arrivals.every(message => message.seen)
  return unreadDelta !== null && unreadDelta <= 0
}

export function notifyBody(messages: MailMessageSummary[], count: number): string {
  const [message] = messages
  if (count === 1 && messages.length === 1 && message) {
    const subject = message.subject || i18next.t('mail:list.noSubject')
    return `${message.fromName || message.fromAddress} — ${subject}`
  }

  return i18next.t('mail:notify.newMessages', { count })
}
