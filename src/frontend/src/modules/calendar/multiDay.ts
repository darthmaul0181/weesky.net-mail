import type { Occurrence } from './calendarTypes'
import {
  addDays, daysBetween, MINUTES_PER_DAY, minutesIntoDay, type PlainDate, plainDateOf,
} from './plainDate'

export interface Slice {
  day: PlainDate
  startMinute: number
  endMinute: number
}

export type Placement =
  | { kind: 'band'; from: PlainDate; to: PlainDate }
  | { kind: 'slices'; slices: Slice[] }

/** A dated event lasting a whole day or more goes in the all-day band rather than the hour grid
    (décision 3): a column filled end to end says nothing about when the event is, and two of them
    read as two events. An evening running from 22:00 to 02:00 is four hours, and stays sliced. */
const BAND_MIN_MINUTES = 24 * 60

export interface WallClock { day: PlainDate; minute: number }

/** The wall clock an occurrence is read at, whichever of the three shapes it came in. Shared with
    the editor's drag: a block placed by one reading and moved by another would jump on the drop. */
export function wallClockOf(o: Occurrence, tz: string): [WallClock, WallClock] {
  if (o.startUtc && o.endUtc) {
    return [o.startUtc, o.endUtc].map(iso => {
      const instant = new Date(iso)
      return { day: plainDateOf(instant, tz), minute: minutesIntoDay(instant, tz) }
    }) as [WallClock, WallClock]
  }
  return [o.localStart ?? '', o.localEnd ?? ''].map(local => ({
    day: local.slice(0, 10),
    minute: Number(local.slice(11, 13)) * 60 + Number(local.slice(14, 16)),
  })) as [WallClock, WallClock]
}

/** How long an occurrence runs, in minutes of the wall clock of `tz` — which is what the grid
    draws it as, and therefore what a resize is counted against. The zone is a parameter because a
    write knows the event's own (`detail.fields.timeZone`) where a pointer only has the
    occurrence's: one formula, the zone stated at each call. */
export function durationMinutesOf(o: Occurrence, tz: string = o.timeZone ?? 'UTC'): number {
  const [start, end] = wallClockOf(o, tz)
  return daysBetween(start.day, end.day) * MINUTES_PER_DAY + end.minute - start.minute
}

function clamp(from: PlainDate, to: PlainDate, visible: PlainDate[]): Placement {
  const first = visible[0]
  const last = visible[visible.length - 1]
  if (to < first || from > last) return { kind: 'slices', slices: [] }
  return { kind: 'band', from: from < first ? first : from, to: to > last ? last : to }
}

/** Where one occurrence goes on a grid of `visible` days: a band across the all-day strip, or a
    slice per day of the hour grid. What falls outside `visible` is cut — the grid has no column
    for it, and a slice with nowhere to land would be positioned off the screen. */
export function placeOccurrence(o: Occurrence, tz: string, visible: PlainDate[]): Placement {
  if (o.isAllDay) {
    const from = o.startDate ?? visible[0]
    return clamp(from, addDays(o.endDateExclusive ?? addDays(from, 1), -1), visible)
  }

  const [start, end] = wallClockOf(o, tz)
  // An event ending exactly at midnight closes the evening it belongs to, and opens no day.
  const lastDay = end.minute === 0 ? addDays(end.day, -1) : end.day
  // Measured on the wall clock rather than on the instants: the band is a rendering decision, and
  // an event drawn as a band would become a pair of columns across a clock change.
  const minutes = durationMinutesOf(o, tz)

  // No hour is carried with the band: the chip formats its own from `wallClockOf`, so a 12-hour
  // account reads "9:00 AM" where a stored `clockOf` string would be stuck at 09:00.
  if (minutes >= BAND_MIN_MINUTES) return clamp(start.day, lastDay, visible)

  const slices: Slice[] = []
  for (let day = start.day; day <= lastDay; day = addDays(day, 1)) {
    if (!visible.includes(day)) continue
    slices.push({
      day,
      startMinute: day === start.day ? start.minute : 0,
      endMinute: day === end.day ? end.minute : MINUTES_PER_DAY,
    })
  }
  return { kind: 'slices', slices }
}

/** The day an occurrence is filed under, whichever of the three shapes its time has. */
export function dayOf(o: Occurrence, tz: string): PlainDate {
  return o.isAllDay ? o.startDate ?? '' : wallClockOf(o, tz)[0].day
}

export interface BandEntry { occurrence: Occurrence; from: PlainDate; to: PlainDate }

export interface SliceEntry {
  occurrence: Occurrence
  slice: Slice
  /** Which end of a cut occurrence this piece is. An evening running past midnight is two chips
      in two columns: without this both would take the same provisional move and the same resized
      height, and the event would be drawn twice on the way to one place. */
  first: boolean
  last: boolean
}

export interface Placements {
  bands: BandEntry[]
  slices: Map<PlainDate, SliceEntry[]>
}

/** A whole screenful placed in one pass. The week grid draws the two halves in two different
    boxes — the band and the columns — so they come back apart rather than merged. */
export function placeAll(
  occurrences: Occurrence[], tz: string, visible: PlainDate[],
): Placements {
  const bands: BandEntry[] = []
  const slices = new Map<PlainDate, SliceEntry[]>()

  for (const occurrence of occurrences) {
    const placement = placeOccurrence(occurrence, tz, visible)
    if (placement.kind === 'band') {
      bands.push({ occurrence, from: placement.from, to: placement.to })
      continue
    }
    placement.slices.forEach((slice, index) => {
      const day = slices.get(slice.day) ?? []
      day.push({
        occurrence, slice,
        first: index === 0, last: index === placement.slices.length - 1,
      })
      slices.set(slice.day, day)
    })
  }
  return { bands, slices }
}

export interface DayItem {
  occurrence: Occurrence
  /** Drawn as a filled pill rather than as a dot and an hour: an all-day event, or a dated one
      running a day or more. */
  band: boolean
  startMinute: number
}

/** The same placement flattened to one list per day, bands repeated on every day they cover —
    what a month cell and an upcoming day both list. A band sorts ahead of every hour. */
export function itemsByDay(
  placements: Placements, visible: PlainDate[],
): Map<PlainDate, DayItem[]> {
  const map = new Map<PlainDate, DayItem[]>()
  const push = (day: PlainDate, item: DayItem) => {
    const list = map.get(day) ?? []
    list.push(item)
    map.set(day, list)
  }

  for (const { occurrence, from, to } of placements.bands) {
    for (let day = from; day <= to; day = addDays(day, 1)) {
      if (visible.includes(day)) push(day, { occurrence, band: true, startMinute: -1 })
    }
  }
  for (const [day, entries] of placements.slices) {
    // The head slice alone: an evening running to 02:00 is two columns of the hour grid and one
    // line of a month cell or of a list, filed under the day it starts on.
    for (const { occurrence, slice, first } of entries) {
      if (first) push(day, { occurrence, band: false, startMinute: slice.startMinute })
    }
  }
  for (const list of map.values()) list.sort((a, b) => a.startMinute - b.startMinute)
  return map
}
