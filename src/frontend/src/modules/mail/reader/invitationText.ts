import type { TFunction } from 'i18next'
import type { MailInvitation } from '../api/mailTypes'
import { dateLocaleOf, formatLongDay, formatLongDayRange, formatTime } from '../../calendar/calendarLocale'
import { addDays, plainDateOf, type PlainDate } from '../../calendar/plainDate'

/** What the card is for, in the reader's own terms: a date to answer, one already answered, one
    that is not the user's to answer, a revision, a cancellation, a dead end, or a broken file. */
export type CardState = 'invite' | 'answered' | 'forwarded' | 'updated' | 'cancelled' | 'noAction' | 'unreadable'

export function cardStateOf(i: MailInvitation): CardState {
  if (i.unreadable) return 'unreadable'
  if (i.occurrenceOnly || i.inCalendar === 'Newer') return 'noAction'
  if (i.method === 'Cancel') return i.inCalendar === 'Cancelled' ? 'cancelled' : 'noAction'
  switch (i.inCalendar) {
    case 'Current': return 'answered'
    case 'Outdated': return 'updated'
    default: return i.addressedTo ? 'invite' : 'forwarded'
  }
}

/** Why there is nothing to press. Read in the order the states are decided in, so a cancellation
    of an event that is also one date of a series says the more useful of the two. */
export function noActionKey(i: MailInvitation): 'occurrenceOnly' | 'newer' | 'cancelAbsent' {
  if (i.occurrenceOnly) return 'occurrenceOnly'
  if (i.inCalendar === 'Newer') return 'newer'
  return 'cancelAbsent'
}

/** The date in words, in the browser's zone — the one the grid places every hour against, so the
    card and the calendar name the same clock for the same event. */
export function whenOf(
  i: MailInvitation, tz: string, lang: string, region: string, cycle: 'h12' | 'h23',
  t: TFunction<'mail'>,
): string {
  const locale = dateLocaleOf(lang, region)
  if (i.isAllDay && i.startDate) {
    const first = i.startDate as PlainDate
    const last = i.endDateExclusive ? addDays(i.endDateExclusive as PlainDate, -1) : first
    return first === last
      ? `${formatLongDay(first, locale, true)} · ${t('reader.invitation.allDay', { ns: 'mail' })}`
      : formatLongDayRange(first, last, locale, true)
  }
  if (!i.start) return ''
  const start = new Date(i.start)
  const end = i.end ? new Date(i.end) : null
  const day = (at: Date) => formatLongDay(plainDateOf(at, tz), locale, true)
  const clock = (at: Date) => formatTime(at, lang, cycle, tz, region)
  if (!end) return `${day(start)}, ${clock(start)}`
  return plainDateOf(start, tz) === plainDateOf(end, tz)
    ? `${day(start)}, ${clock(start)} – ${clock(end)}`
    : `${day(start)}, ${clock(start)} – ${day(end)}, ${clock(end)}`
}
