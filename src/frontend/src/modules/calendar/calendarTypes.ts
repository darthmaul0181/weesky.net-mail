/** One calendar as `GET /api/Calendars` answers it. `isDefault` is derived server-side: the
    collection no deletion may take. The API serialises with `WhenWritingNull`, so an absent value
    is omitted from the JSON rather than sent as `null` — every optional field below is therefore
    `?:`, never `| null`. */
export interface Calendar {
  id: string
  davName: string
  displayName: string
  description: string
  color: string
  order: number
  timeZone: string
  isVisible: boolean
  isDefault: boolean
}

/** The body of `POST /api/Calendars` and `PUT /api/Calendars/{id}`. A field the request does not
    name keeps the calendar's own — the same convention `ContactWrite` documents. */
export interface CalendarWrite {
  displayName: string
  description?: string
  color?: string
  order?: number
}

export type EditScope = 'This' | 'ThisAndFollowing' | 'All'

export type Availability = 'Busy' | 'Tentative' | 'Free'

export type Visibility = 'Default' | 'Private'

export type RecurrenceEnd = 'Never' | 'Count' | 'Until'

/** The `repeat` block of an event, as the editor states it and as `GET`/`fields.repeat` answers
    it back. `byDay` is always an array, even empty — the API never omits a non-nullable list. */
export interface RecurrenceWrite {
  frequency: string
  interval: number
  byDay: string[]
  byMonthDay?: number
  bySetPos?: number
  bySetPosDay?: string
  end: RecurrenceEnd
  count?: number
  until?: string
}

/** One event, in the two shapes it has on the wire — POST/PUT's body, and `EventDetail.fields`
    coming back. A dated one carries `start`/`end` (wall-clock, no zone suffix) plus `timeZone`;
    an all-day one carries `startDate`/`endDateInclusive`, the last day shown. */
export interface EventWrite {
  calendarId: string
  summary?: string
  location?: string
  description?: string
  isAllDay: boolean
  start?: string
  end?: string
  timeZone?: string
  startDate?: string
  endDateInclusive?: string
  repeat?: RecurrenceWrite
  reminderMinutesBefore: number[]
  availability: Availability
  visibility: Visibility
  url?: string
}

/** One ORGANIZER or ATTENDEE line, carrying the RECURRENCE-ID of the component it was written on
    — absent for the master. */
export interface AttendeeProjection {
  recurrenceId?: string
  email: string
  name?: string
  role?: string
  partStat?: string
  isOrganizer: boolean
}

/** `GET /api/Calendar/Events/{id}` — the resource as the editor opens it. `fields` is the event
    read back as an `EventWrite`, so saving it unchanged writes the same event; `icsHash` is what a
    save sends back as `ifHash` to prove it edited this version. */
export interface EventDetail {
  id: string
  calendarId: string
  uid: string
  icsHash: string
  fields: EventWrite
  recurrenceText?: string
  attendees: AttendeeProjection[]
  status?: string
}

/**
 * One instance inside a window (`GET /api/Calendar/Events` and `.../Search`), in the shape its own
 * time has — never more than one of the three at once:
 * - dated: `startUtc`/`endUtc` (ISO instants) and `timeZone`;
 * - all-day: `startDate`/`endDateExclusive` (the morning after);
 * - floating: `localStart`/`localEnd`, wall-clock readings that belong to no zone.
 *
 * `instanceId` is the literal `RECURRENCE-ID` a client would write to address this instance —
 * never the UTC instant — and `''` for an event that does not repeat.
 */
export interface Occurrence {
  eventId: string
  calendarId: string
  uid: string
  instanceId: string
  isOverride: boolean
  isAllDay: boolean
  isFloating: boolean
  timeZone?: string
  startUtc?: string
  endUtc?: string
  startDate?: string
  endDateExclusive?: string
  localStart?: string
  localEnd?: string
  summary?: string
  location?: string
  status?: string
  transparency: string
  class?: string
  hasAlarm: boolean
  recurrenceText?: string
}

export interface CalendarListResponse {
  calendars: Calendar[]
}

export interface OccurrenceListResponse {
  occurrences: Occurrence[]
}

export interface CalendarImportError {
  /** The resource's 1-based rank in the imported file, not a line of the file: grouping by UID
      goes through the object model, which loses the original line numbers. */
  line: number
  reason: string
}

/** `POST /api/Calendars/{id}/Import` — `totalErrors` counts every resource that failed, including
    those past the server's cap on `errors`. */
export interface CalendarImportReport {
  created: number
  replaced: number
  ignoredTodos: number
  ignoredJournals: number
  failed: number
  totalErrors: number
  errors: CalendarImportError[]
}
