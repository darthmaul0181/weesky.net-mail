import type { TFunction } from 'i18next'
import { formatLongDay, formatLongDayRange, formatTime } from './calendarLocale'
import type { Occurrence } from './calendarTypes'
import { wallClockOf } from './multiDay'
import { addDays, utcOfLocalTime, type PlainDate } from './plainDate'

/** How a screen reads an instant: the zone it is read in, and what `Intl` needs to write it.
    One object rather than five arguments — every caller holds the module's context, and
    `locale` is `dateLocaleOf(lang, region)`, grafted by the caller that already needed it. */
export interface WhenFormat {
  tz: string
  lang: string
  region: string
  cycle: 'h12' | 'h23'
  locale: string
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

/**
 * What an occurrence says about when it happens, in its two parts: the day or the days it spans,
 * then its hours or that it takes the whole day. The bubble prints them with a dot between, and
 * a chip's accessible name with a comma — a chip's own day is carried by the column or the cell
 * it sits in, which is position, and position is not something a reader can hear.
 *
 * Two parts rather than three: a whole day of several days is its range and nothing else, a
 * single one says so, and a dated event whose clocks the server did not send says neither —
 * announcing "All day" for an event that has an hour is worse than saying nothing about it.
 */
export function whenPartsOf(
  o: Occurrence, { tz, lang, region, cycle, locale }: WhenFormat, t: TFunction<'calendar'>,
): string[] {
  const span = daysOf(o, tz)
  const days = !span ? null : span[0] === span[1]
    ? formatLongDay(span[0], locale)
    : formatLongDayRange(span[0], span[1], locale)

  const clocks = o.isAllDay ? null : wallClockOf(o, tz)
  const at = (clock: { day: PlainDate; minute: number }) => formatTime(
    utcOfLocalTime(clock.day, clock.minute, tz), lang, cycle, tz, region)
  const readable = clocks?.every(clock => clock.day !== '' && Number.isFinite(clock.minute))
  const hours = !o.isAllDay && clocks && readable ? `${at(clocks[0])} – ${at(clocks[1])}` : null
  const allDay = o.isAllDay && (!span || span[0] === span[1])
    ? t('preview.allDay', { ns: 'calendar' }) : null

  return [days, allDay ?? hours].filter((part): part is string => !!part)
}
