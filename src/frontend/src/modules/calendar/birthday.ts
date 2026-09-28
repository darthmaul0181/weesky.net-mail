import type { TFunction } from 'i18next'
import type { BirthdayReminder, Calendar, Occurrence } from './calendarTypes'

export const REMINDER_OPTIONS: BirthdayReminder[] = ['none', 'same_day', 'day_before', 'week_before']

/** The hour the server sets a birthday's alarm at, on the day or the day before. */
const REMINDER_HOUR = 9

export const isBirthday = (o: Occurrence): boolean => o.contactId !== undefined

/** Whether an event may be written into it: the birthdays calendar is read-only. */
export const isWritable = (c: Calendar): boolean => c.kind !== 'birthdays'

/** The age reached on the occurrence shown; null when the card holds no year. */
export function ageOf(o: Occurrence): number | null {
  if (o.birthYear === undefined || !o.startDate) return null
  return Number(o.startDate.slice(0, 4)) - o.birthYear
}

export const contactUrlOf = (id: string): string => `/contacts?id=${encodeURIComponent(id)}`

/** Literal keys, one per value: the i18n rule forbids building a key from a variable. */
export function reminderLabel(value: BirthdayReminder, t: TFunction<'calendar'>): string {
  const hour = REMINDER_HOUR
  switch (value) {
    case 'none': return t('dialogs.reminders.none', { ns: 'calendar' })
    case 'same_day': return t('dialogs.reminders.same_day', { hour, ns: 'calendar' })
    case 'day_before': return t('dialogs.reminders.day_before', { hour, ns: 'calendar' })
    case 'week_before': return t('dialogs.reminders.week_before', { ns: 'calendar' })
  }
}

/** The bubble's bell line; never asked for `none`, which shows no line. */
export function reminderLine(
  value: Exclude<BirthdayReminder, 'none'>, t: TFunction<'calendar'>,
): string {
  const hour = REMINDER_HOUR
  switch (value) {
    case 'same_day': return t('preview.reminderAt.same_day', { hour, ns: 'calendar' })
    case 'day_before': return t('preview.reminderAt.day_before', { hour, ns: 'calendar' })
    case 'week_before': return t('preview.reminderAt.week_before', { ns: 'calendar' })
  }
}
