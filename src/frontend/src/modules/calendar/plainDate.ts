/**
 * A day the way a calendar means it: `'2026-09-14'`, with no hour and no zone. Nothing here ever
 * builds a `Date` from local components — that reads the machine's own zone, and a Brussels
 * laptop would then answer what a CI runner in UTC does not.
 */
export type PlainDate = string

export const DAY_MS = 86_400_000
export const MINUTES_PER_DAY = 1440

/** 5 January 2026 is a Monday: the anchor every weekday name in this module is counted from. */
export const MONDAY_UTC_MS = Date.UTC(2026, 0, 5)

const DAY = /^\d{4}-\d{2}-\d{2}$/

/** Whether a string is a day and not a half-typed one: the URL's `date`, the first ten characters
    of a RECURRENCE-ID and every date box in the editor are read through here. */
export function isPlainDate(value: string): boolean {
  return DAY.test(value)
}

const PARTS_OPTIONS: Intl.DateTimeFormatOptions = {
  year: 'numeric', month: '2-digit', day: '2-digit',
  hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23',
}

const partFormats = new Map<string, Intl.DateTimeFormat>()

function partsOf(instant: Date, tz: string): Record<string, number> {
  let formatter = partFormats.get(tz)
  if (!formatter) {
    formatter = new Intl.DateTimeFormat('en-US', { ...PARTS_OPTIONS, timeZone: tz })
    partFormats.set(tz, formatter)
  }
  const parts: Record<string, number> = {}
  for (const { type, value } of formatter.formatToParts(instant)) {
    if (type !== 'literal') parts[type] = Number(value)
  }
  return parts
}

const pad = (value: number, width = 2) => String(value).padStart(width, '0')

export function plainDateOf(instant: Date, tz: string): PlainDate {
  const { year, month, day } = partsOf(instant, tz)
  return `${pad(year, 4)}-${pad(month)}-${pad(day)}`
}

export function todayIn(tz: string): PlainDate {
  return plainDateOf(new Date(), tz)
}

/** The day as an instant in a zone that never shifts, so day arithmetic is plain milliseconds. */
function utcMsOf(day: PlainDate): number {
  const [year, month, date] = day.split('-').map(Number)
  return Date.UTC(year, month - 1, date)
}

function dayOf(ms: number): PlainDate {
  return new Date(ms).toISOString().slice(0, 10)
}

export function addDays(day: PlainDate, count: number): PlainDate {
  return dayOf(utcMsOf(day) + count * DAY_MS)
}

/** Whole days from `from` to `to`, negative when `to` is the earlier of the two. */
export function daysBetween(from: PlainDate, to: PlainDate): number {
  return Math.round((utcMsOf(to) - utcMsOf(from)) / DAY_MS)
}

/** 1 = Monday … 7 = Sunday, the ISO numbering `WeekRules.firstDay` is written in. */
export function isoWeekdayOf(day: PlainDate): number {
  return new Date(utcMsOf(day)).getUTCDay() || 7
}

/** How far the zone's wall clock runs ahead of UTC at that instant, in milliseconds. */
function offsetMsAt(instant: Date, tz: string): number {
  const p = partsOf(instant, tz)
  return Date.UTC(p.year, p.month - 1, p.day, p.hour, p.minute, p.second) - instant.getTime()
}

/** The instant a wall clock reads in a zone. Iterated because an offset is only knowable at an
    instant: the first pass guesses, the second corrects it on the days a transition moves it —
    which is also why the minutes go in here rather than being added to midnight afterwards. The
    day the clocks go back is 25 hours long, so midnight plus three hours is 02:00, not 03:00. */
export function utcOfLocalTime(day: PlainDate, minute: number, tz: string): Date {
  const wanted = utcMsOf(day) + minute * 60_000
  let instant = wanted - offsetMsAt(new Date(wanted), tz)
  instant = wanted - offsetMsAt(new Date(instant), tz)
  return new Date(instant)
}

export function utcOfLocalMidnight(day: PlainDate, tz: string): Date {
  return utcOfLocalTime(day, 0, tz)
}

/** The day's own midnight in UTC: what every formatter pinned to `timeZone: 'UTC'` is handed, so
    a date prints as the day it names rather than as an instant read in the machine's zone. */
export function utcMidnightOf(day: PlainDate): Date {
  return utcOfLocalMidnight(day, 'UTC')
}

/** Minutes since the zone's midnight — a grid column's vertical coordinate. */
export function minutesIntoDay(instant: Date, tz: string): number {
  const { hour, minute } = partsOf(instant, tz)
  return hour * 60 + minute
}

/** `'HH:mm'`, the shape the editor's time fields and a band's label are written in. */
export function clockOf(minutes: number): string {
  return `${pad(Math.floor(minutes / 60))}:${pad(minutes % 60)}`
}
