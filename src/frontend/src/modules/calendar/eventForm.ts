import type {
  Availability, EditScope, EventDetail, EventUpdateBody, EventWrite, Occurrence, RecurrenceWrite,
  Visibility,
} from './calendarTypes'
import { durationMinutesOf, wallClockOf, type WallClock } from './multiDay'
import {
  addDays, clockOf, daysBetween, isoWeekdayOf, isPlainDate, MINUTES_PER_DAY, minutesIntoDay,
  type PlainDate, plainDateOf, utcOfLocalTime,
} from './plainDate'
import { WEEKDAY_TOKENS } from './calendarLocale'

/** What the repeat picker can say. Anything richer a phone wrote comes back as `custom`, rule
    and all, so nothing is narrowed by being opened in an editor that cannot draw it. */
export type RepeatChoice =
  | { kind: 'never' }
  | { kind: 'daily' | 'weekly' | 'monthly' | 'yearly' }
  | { kind: 'custom'; rule: RecurrenceWrite }

/** The editor's whole state. Dates and times are kept apart because the two controls are apart
    on the screen, and a whole-day event has dates with no times to speak of. */
export interface EventFormState {
  calendarId: string
  title: string
  isAllDay: boolean
  startDate: PlainDate
  /** `'HH:mm'`. */
  startTime: string
  /** The last day shown when `isAllDay`, inclusive — never the morning after. */
  endDate: PlainDate
  endTime: string
  timeZone: string
  repeat: RepeatChoice
  reminders: number[]
  location: string
  description: string
  availability: Availability
  visibility: Visibility
  url: string
  /** The stored rule is more than the picker can show, so a save must keep it as it stands. */
  keepRepeat: boolean
  foreignAlarms: string[]
}

const FREQUENCIES = { daily: 'DAILY', weekly: 'WEEKLY', monthly: 'MONTHLY', yearly: 'YEARLY' }

function readInstant(instant: Date, tz: string): [PlainDate, string] {
  return [plainDateOf(instant, tz), clockOf(minutesIntoDay(instant, tz))]
}

/** A wall-clock string as the API writes it: `2026-09-14T08:00:00`, no zone suffix. */
function splitWallClock(value: string): [PlainDate, string] {
  return [value.slice(0, 10), value.slice(11, 16)]
}

export function newEventForm(
  start: Date, end: Date, allDay: boolean, calendarId: string, tz: string,
): EventFormState {
  const [startDate, startTime] = readInstant(start, tz)
  const [endDate, endTime] = readInstant(end, tz)

  return {
    calendarId, title: '', isAllDay: allDay,
    startDate, startTime, endDate, endTime, timeZone: tz,
    // Born silent, whole day or not: a reminder is asked for with "+ Add a reminder", never
    // handed out — décision 13's fifteen minutes were withdrawn by the owner on 2026-09-10.
    repeat: { kind: 'never' }, reminders: [],
    location: '', description: '',
    // A day off does not block a free/busy, and that is how Apple's clients write one.
    availability: allDay ? 'Free' : 'Busy',
    visibility: 'Default', url: '', keepRepeat: false, foreignAlarms: [],
  }
}

function repeatChoiceOf(rule: RecurrenceWrite | undefined): RepeatChoice {
  if (!rule) return { kind: 'never' }

  const plain = rule.interval === 1 && rule.end === 'Never' && rule.byDay.length === 0
    && rule.byMonthDay === undefined && rule.bySetPos === undefined
  const kind = (Object.keys(FREQUENCIES) as (keyof typeof FREQUENCIES)[])
    .find(name => FREQUENCIES[name] === rule.frequency)

  return plain && kind ? { kind } : { kind: 'custom', rule }
}

export function ruleOf(choice: RepeatChoice): RecurrenceWrite | undefined {
  if (choice.kind === 'never') return undefined
  if (choice.kind === 'custom') return choice.rule
  return { frequency: FREQUENCIES[choice.kind], interval: 1, byDay: [], end: 'Never' }
}

/** What the switch turns on: every week, on the start's own weekday, for ever — Outlook's
    default, and the one rule nobody has to correct before saving. */
export function defaultRule(startDate: PlainDate): RecurrenceWrite {
  return {
    frequency: 'WEEKLY', interval: 1, byDay: [WEEKDAY_TOKENS[isoWeekdayOf(startDate) - 1]],
    end: 'Never',
  }
}

/** A day-of-month or a weekday position is a shape the block no longer draws: the rule stays
    exact on the wire, but a screen that cannot show it must not be allowed to rewrite it. */
export function beyondTheBlock(rule: RecurrenceWrite | undefined): boolean {
  return rule !== undefined && (rule.byMonthDay !== undefined || rule.bySetPos !== undefined)
}

/** A repeating event ends on the day it starts: the End row's date is drawn greyed on the start
    date, and the one thing that can move it is a time that crosses midnight — 23:00 to 01:00
    ends the morning after, and pinning it to the start day would refuse the save. */
export function alignEnd(form: EventFormState): EventFormState {
  if (form.repeat.kind === 'never') return form
  const crosses = !form.isAllDay && form.endTime < form.startTime
  const endDate = crosses ? addDays(form.startDate, 1) : form.startDate
  return endDate === form.endDate ? form : { ...form, endDate }
}

/** The event as the editor opens it. An occurrence's instant is read in the *event's* own zone,
    never the browser's: one written in New York and opened from Brussels still has to say the
    hour its author chose, or saving it unchanged would move it. */
export function formOf(
  detail: EventDetail, occurrence: Occurrence | null, browserTz: string,
): EventFormState {
  const f = detail.fields
  const timeZone = f.timeZone ?? browserTz

  const [startDate, startTime, endDate, endTime] = seedSpan(detail, occurrence, timeZone)

  return {
    calendarId: detail.calendarId, title: f.summary ?? '', isAllDay: f.isAllDay,
    startDate, startTime, endDate, endTime, timeZone,
    repeat: repeatChoiceOf(f.repeat), reminders: [...f.reminderMinutesBefore],
    location: f.location ?? '', description: f.description ?? '',
    availability: f.availability, visibility: f.visibility, url: f.url ?? '',
    keepRepeat: !detail.repeatIsExact || beyondTheBlock(f.repeat), foreignAlarms: detail.foreignAlarms,
  }
}

/** The days and clocks the editor is sown with: the occurrence's own when one is opened, the
    event's otherwise. A whole-day occurrence carries its own days, never the master's —
    `movedBody`'s rule: an instance of a series sits weeks away from the event `fields` describe. */
function seedSpan(
  detail: EventDetail, occurrence: Occurrence | null, timeZone: string,
): [PlainDate, string, PlainDate, string] {
  const f = detail.fields
  if (f.isAllDay) {
    const [startDate, endDate] = occurrence
      ? allDaySpan(detail, occurrence)
      : [f.startDate ?? '', f.endDateInclusive ?? '']
    return [startDate, '09:00', endDate, '10:00']
  }
  if (occurrence?.startUtc && occurrence.endUtc) {
    return [...readInstant(new Date(occurrence.startUtc), timeZone),
      ...readInstant(new Date(occurrence.endUtc), timeZone)]
  }
  if (occurrence?.localStart && occurrence.localEnd) {
    return [...splitWallClock(occurrence.localStart), ...splitWallClock(occurrence.localEnd)]
  }
  return [...splitWallClock(f.start ?? ''), ...splitWallClock(f.end ?? '')]
}

/** The day the whole series starts on, as the stored event says it. */
function masterStartDay(detail: EventDetail): PlainDate {
  const f = detail.fields
  return f.isAllDay ? f.startDate ?? '' : splitWallClock(f.start ?? '')[0]
}

/** Scope All, saved from an occurrence that is not the first: the form was sown with THAT
    occurrence's days, and written as they stand they would become the series' new DTSTART —
    every occurrence before the one opened would vanish. What the whole series takes from the form
    is the change, never the day: the clocks, the length, and the days the occurrence was moved
    by, re-posed on the day the series actually starts. */
function rebasedOnMaster(
  form: EventFormState, detail: EventDetail, occurrence: Occurrence | null,
): EventFormState {
  if (!occurrence?.instanceId) return form
  const [sown] = seedSpan(detail, occurrence, form.timeZone)
  const master = masterStartDay(detail)
  if (![sown, master, form.startDate, form.endDate].every(isPlainDate)) return form
  const startDate = addDays(master, daysBetween(sown, form.startDate))
  const endDate = addDays(startDate, daysBetween(form.startDate, form.endDate))
  return startDate === form.startDate && endDate === form.endDate
    ? form : { ...form, startDate, endDate }
}

/** The body of a creation. `keepRepeat` is never on it: there is no stored rule to keep, and the
    API refuses it beside a `repeat` anyway. */
export function writeOf(form: EventFormState): EventWrite {
  const write: EventWrite = {
    calendarId: form.calendarId,
    isAllDay: form.isAllDay,
    reminderMinutesBefore: form.reminders,
    availability: form.availability,
    visibility: form.visibility,
  }

  if (form.title) write.summary = form.title
  if (form.location) write.location = form.location
  if (form.description) write.description = form.description
  if (form.url) write.url = form.url

  if (form.isAllDay) {
    write.startDate = form.startDate
    write.endDateInclusive = form.endDate
  } else {
    write.start = `${form.startDate}T${form.startTime}:00`
    write.end = `${form.endDate}T${form.endTime}:00`
    write.timeZone = form.timeZone
  }

  const rule = ruleOf(form.repeat)
  if (rule) write.repeat = rule
  return write
}

/** The two are refused together, so the flag is cleared before anything else: a body built by
    spreading `detail.fields` inherits whatever the API chose to send back, and the forbidden pair
    must be impossible to write here whatever that is. */
function keepRule(body: EventWrite, keep: boolean) {
  delete body.keepRepeat
  if (!keep) return
  delete body.repeat
  body.keepRepeat = true
}

export function updateBodyOf(
  form: EventFormState, detail: EventDetail, occurrence: Occurrence | null, scope: EditScope,
): EventUpdateBody {
  const written = scope === 'All' ? rebasedOnMaster(form, detail, occurrence) : form
  const body: EventUpdateBody = { ...writeOf(written), scope, ifHash: detail.icsHash }
  if (scope !== 'All' && occurrence?.instanceId) body.instanceId = occurrence.instanceId
  keepRule(body, form.keepRepeat)
  return body
}

/** Which scopes the save dialog may offer. Moving an event to another calendar moves the whole
    file, so a narrow scope — which writes an exception into the series it stays in — cannot be
    asked for at the same time. */
export function allowedScopes(
  form: EventFormState, detail: EventDetail, occurrence: Occurrence | null,
): EditScope[] {
  if (!isRecurring(occurrence) || form.calendarId !== detail.calendarId) return ['All']
  return ['This', 'ThisAndFollowing', 'All']
}

/** An event is recurring on screen when the occurrence opened carries a RECURRENCE-ID (décision
    8). A repeat the picker has just added is not one: the series has no other occurrence yet, so
    the scope question would offer two answers the server cannot honour. */
export function isRecurring(occurrence: Occurrence | null): boolean {
  return Boolean(occurrence?.instanceId)
}

/** A drag or a resize on the grid: the stored fields are kept whole and only the times move, so
    nothing the editor never showed can be lost on the way. */
export function movedBody(
  detail: EventDetail, occurrence: Occurrence, deltaMinutes: number,
  newDurationMinutes: number | null, scope: EditScope,
): EventUpdateBody {
  const f = detail.fields
  const body: EventUpdateBody = { ...f, scope, ifHash: detail.icsHash }
  if (scope !== 'All' && occurrence.instanceId) body.instanceId = occurrence.instanceId

  if (f.isAllDay) {
    // The occurrence's own days, never the master's: an instance of a series sits weeks away from
    // the event `fields` describe, and shifting those would write the change onto the wrong day
    // while the block on the screen — patched from the occurrence — sat on the right one.
    const days = Math.round(deltaMinutes / MINUTES_PER_DAY)
    const [from, to] = allDaySpan(detail, occurrence)
    body.startDate = addDays(from, days)
    body.endDateInclusive = addDays(to, days)
  } else {
    const [start, end] = movedClocks(
      occurrence, zoneOf(detail, occurrence), deltaMinutes, newDurationMinutes)
    body.start = localOf(start)
    body.end = localOf(end)
  }

  keepRule(body, !detail.repeatIsExact)
  return body
}

/** Minutes added to a wall clock, carrying into the day — never to an instant. A drag is a
    distance in grid pixels, so an hour dragged is an hour of clock face: applied to the instant
    instead, the block would land back on 02:30 the morning the clocks go back. */
function shiftWallClock(day: PlainDate, minute: number, delta: number): [PlainDate, number] {
  const total = minute + delta
  const days = Math.floor(total / MINUTES_PER_DAY)
  return [addDays(day, days), total - days * MINUTES_PER_DAY]
}

/** Where a gesture leaves an occurrence, as the two wall clocks it now spans. Shared by the body
    a drop sends and by the occurrence the cache holds until the server answers, so the block on
    the screen and the event on the wire can never name two different hours. */
function movedClocks(
  occurrence: Occurrence, tz: string, deltaMinutes: number, newDurationMinutes: number | null,
): [WallClock, WallClock] {
  const [start] = wallClockOf(occurrence, tz)
  const duration = newDurationMinutes ?? durationMinutesOf(occurrence, tz)

  const [day, minute] = shiftWallClock(start.day, start.minute, deltaMinutes)
  const [lastDay, lastMinute] = shiftWallClock(day, minute, duration)

  return [{ day, minute }, { day: lastDay, minute: lastMinute }]
}

/** The days an all-day occurrence covers, ends inclusive — read the way the grid reads them
    (`placeOccurrence`), with the master's own pair as the fallback for an occurrence carrying
    neither date. */
function allDaySpan(detail: EventDetail, occurrence: Occurrence): [PlainDate, PlainDate] {
  const f = detail.fields
  if (!occurrence.startDate) return [f.startDate ?? '', f.endDateInclusive ?? '']
  const exclusive = occurrence.endDateExclusive ?? addDays(occurrence.startDate, 1)
  return [occurrence.startDate, addDays(exclusive, -1)]
}

/** The event's own zone, never the machine's: an event written in Tokyo keeps its hours when a
    Brussels laptop drags it. */
function zoneOf(detail: EventDetail, occurrence: Occurrence): string {
  return detail.fields.timeZone ?? occurrence.timeZone ?? 'UTC'
}

const localOf = (clock: WallClock) => `${clock.day}T${clockOf(clock.minute)}:00`

/**
 * The occurrence a gesture leaves behind, for the window cache to draw until the server answers.
 * The shifted wall clock is re-posed in the event's own zone through `Intl` — a block dragged an
 * hour on the grid is an hour of clock face, and re-posing it anywhere else would land it on the
 * wrong minute the morning the clocks change.
 */
export function movedOccurrence(
  detail: EventDetail, occurrence: Occurrence, deltaMinutes: number,
  newDurationMinutes: number | null,
): Occurrence {
  if (occurrence.isAllDay) {
    const days = Math.round(deltaMinutes / MINUTES_PER_DAY)
    return {
      ...occurrence,
      startDate: occurrence.startDate && addDays(occurrence.startDate, days),
      endDateExclusive: occurrence.endDateExclusive
        && addDays(occurrence.endDateExclusive, days),
    }
  }

  const tz = zoneOf(detail, occurrence)
  const [start, end] = movedClocks(occurrence, tz, deltaMinutes, newDurationMinutes)
  if (occurrence.isFloating) {
    return { ...occurrence, localStart: localOf(start), localEnd: localOf(end) }
  }
  const instant = (clock: WallClock) =>
    utcOfLocalTime(clock.day, clock.minute, tz).toISOString()
  return { ...occurrence, startUtc: instant(start), endUtc: instant(end) }
}

/** The key of the refusal, or null. Only an end *before* its start is refused, so the key always
    says what happened; the editor spells that key out itself rather than passing this to `t()`,
    a key reaching `t()` as a variable being invisible to `keys.test.ts`. */
export function validate(form: EventFormState): string | null {
  if (form.isAllDay) {
    return daysBetween(form.startDate, form.endDate) < 0 ? 'editor.endBeforeStart' : null
  }
  const start = `${form.startDate}T${form.startTime}`
  const end = `${form.endDate}T${form.endTime}`
  return end < start ? 'editor.endBeforeStart' : null
}
