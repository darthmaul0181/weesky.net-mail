import type { TFunction } from 'i18next'
import { clockOf, MINUTES_PER_DAY } from './plainDate'

/** Minutes before the start, the ladder a dated event's bell offers. */
export const DATED_PRESETS = [0, 5, 10, 15, 30, 60, 120, 1440, 2880, 10080] as const

/** The same field, counted from the day's own midnight: 900 is 15 hours before it, so the bell
    rings at 09:00 the day before. A whole day has no hour, so a distance would say nothing and
    the ladder is written as the moments the phones actually offer. */
export const ALL_DAY_PRESETS = [360, 900, 2340, 9540] as const

export const MAX_REMINDERS = 5

/** The value the other ladder falls back to, and the one a fresh bell opens on: fifteen minutes
    is every client's own default. */
export const DATED_DEFAULT = 15

// `{ ns: 'calendar' }` is redundant to i18next, which reads the namespace off the TFunction, and
// not redundant to `locales/keys.test.ts`, which binds a file's namespace from its
// `useTranslation(...)` call and finds none here. Do not tidy it away.
function datedLabel(minutes: number, t: TFunction<'calendar'>): string {
  if (minutes <= 0) return t('reminders.atStart', { ns: 'calendar' })
  if (minutes % 10080 === 0) return t('reminders.weeks', { count: minutes / 10080, ns: 'calendar' })
  if (minutes % MINUTES_PER_DAY === 0) {
    return t('reminders.days', { count: minutes / MINUTES_PER_DAY, ns: 'calendar' })
  }
  if (minutes % 60 === 0 && minutes < MINUTES_PER_DAY) {
    return t('reminders.hours', { count: minutes / 60, ns: 'calendar' })
  }
  return t('reminders.minutes', { count: minutes, ns: 'calendar' })
}

/** The bell as a sentence: a dated reminder is a distance, an all-day one the day it rings on
    plus that day's hour. The hour is written `HH:mm` rather than through `Intl` — this is
    arithmetic on a stored offset, so there is no instant and no zone to hand a formatter. */
export function reminderLabel(
  minutes: number, allDay: boolean, t: TFunction<'calendar'>,
): string {
  if (!allDay) return datedLabel(minutes, t)

  const daysBefore = Math.max(0, Math.ceil(minutes / MINUTES_PER_DAY))
  const time = clockOf(daysBefore * MINUTES_PER_DAY - minutes)

  if (daysBefore === 0) return t('reminders.onTheDay', { time, ns: 'calendar' })
  if (daysBefore % 7 === 0) {
    return t('reminders.weeksBefore', { count: daysBefore / 7, time, ns: 'calendar' })
  }
  return t('reminders.daysBefore', { count: daysBefore, time, ns: 'calendar' })
}

/** What a reminder becomes when the all-day switch is flipped. A value the target ladder cannot
    express falls back to that ladder's default rather than being kept: 15 minutes before an event
    with no hour would ring at 23:45 the night before, which nobody asked for. */
export function convertReminder(minutes: number, toAllDay: boolean): number {
  const presets: readonly number[] = toAllDay ? ALL_DAY_PRESETS : DATED_PRESETS
  if (presets.includes(minutes)) return minutes
  return toAllDay ? ALL_DAY_PRESETS[0] : DATED_DEFAULT
}
