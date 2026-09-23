import type { TFunction } from 'i18next'
import { dateLocaleOf, formatLongDay, formatLongDayRange, formatTime } from './calendarLocale'
import type { Occurrence } from './calendarTypes'
import { wallClockOf } from './multiDay'
import { addDays, utcOfLocalTime, type PlainDate } from './plainDate'

/** How a screen reads an instant: the zone it is read in, and what `Intl` needs to write it. One
    object rather than four arguments, every caller holding the module's context — and the date
    locale is grafted here, so no caller can hand in one its own language disagrees with. */
export interface WhenFormat {
  tz: string
  lang: string
  region: string
  cycle: 'h12' | 'h23'
}

/** The two ends an occurrence spans, whichever shape its time came in — `null` when the server
    sent none of the three, which the grid already draws rather than throwing over. */
export function daysOf(o: Occurrence, tz: string): [PlainDate, PlainDate] | null {
  if (o.isAllDay) {
    const from = o.startDate
    if (!from) return null
    return [from, addDays(o.endDateExclusive ?? addDays(from, 1), -1)]
  }
  const [start, end] = wallClockOf(o, tz)
  if (start.day === '') return null
  // A start the server sent and an end it did not is still a day worth naming.
  if (end.day === '') return [start.day, start.day]
  // An event closing exactly at midnight belongs to the evening it started in.
  return [start.day, end.minute === 0 ? addDays(end.day, -1) : end.day]
}

/** When an occurrence happens, in two parts: its days, then its hours or "all day". The bubble
 * joins them with a dot, a chip's name with a comma, since no reader hears a chip's position. */
export function whenPartsOf(
  o: Occurrence, { tz, lang, region, cycle }: WhenFormat, t: TFunction<'calendar'>,
): string[] {
  const locale = dateLocaleOf(lang, region)
  const span = daysOf(o, tz)
  const days = !span ? null : span[0] === span[1]
    ? formatLongDay(span[0], locale)
    : formatLongDayRange(span[0], span[1], locale)

  const clocks = o.isAllDay ? null : wallClockOf(o, tz)
  const at = (clock: { day: PlainDate; minute: number }) => formatTime(
    utcOfLocalTime(clock.day, clock.minute, tz), lang, cycle, tz, region)
  const readable = clocks?.every(clock => clock.day !== '' && Number.isFinite(clock.minute))
  const hours = !o.isAllDay && clocks && readable ? `${at(clocks[0])} – ${at(clocks[1])}` : null
  // Two parts and not three: a whole day of several days is its range and nothing else, and a
  // dated event whose clocks the server did not send says neither — "All day" for an event that
  // has an hour is worse than silence. The namespace is spelled for `locales/keys.test.ts`.
  const allDay = o.isAllDay && (!span || span[0] === span[1])
    ? t('preview.allDay', { ns: 'calendar' }) : null

  return [days, allDay ?? hours].filter((part): part is string => !!part)
}
