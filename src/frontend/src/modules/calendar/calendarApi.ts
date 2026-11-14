import { request, requestBlob } from '../../lib/http'
import type {
  CalendarImportOutcome, CalendarImportReport, CalendarListResponse, CalendarWrite, CreatedId,
  EditScope, EventDetail, EventUpdateBody, EventUpdated, EventWrite, OccurrenceListResponse,
} from './calendarTypes'

/** Calendars and their events. */
export const calendarApi = {
  // tz is required: it is the zone the default calendar is created with the first time it is
  // asked for, and the backend needs it on every call to answer that lazily. lang is the language
  // the birthdays calendar is written in, should this read create it.
  getCalendars: (tz: string, lang: string) =>
    request<CalendarListResponse>('GET',
      `/api/Calendars?tz=${encodeURIComponent(tz)}&lang=${encodeURIComponent(lang)}`),

  // The birthdays switch: the preference and the calendar change together.
  setBirthdays: (enabled: boolean, tz: string, lang: string) =>
    request<null>('PUT',
      `/api/Calendars/Birthdays?tz=${encodeURIComponent(tz)}&lang=${encodeURIComponent(lang)}`,
      { enabled }),

  // tz here is the new calendar's own zone, asked once at creation and never again.
  createCalendar: (calendar: CalendarWrite, tz: string) =>
    request<CreatedId>('POST', `/api/Calendars?tz=${encodeURIComponent(tz)}`, calendar),

  updateCalendar: (id: string, calendar: CalendarWrite) =>
    request<null>('PUT', `/api/Calendars/${id}`, calendar),

  // Its own route: the sidebar checkbox is a display state, never projected through a whole-
  // calendar PUT.
  setCalendarVisible: (id: string, visible: boolean) =>
    request<null>('PUT', `/api/Calendars/${id}/Visible`, { visible }),

  deleteCalendar: (id: string) =>
    request<null>('DELETE', `/api/Calendars/${id}`),

  exportCalendar: (id: string) => requestBlob(`/api/Calendars/${id}/Export`),

  importCalendar: (id: string, file: File) => {
    const form = new FormData()
    form.append('file', file)
    return request<CalendarImportReport>('POST', `/api/Calendars/${id}/Import`, form)
  },

  // One gesture rather than two: the calendar is created and the file poured into it. An empty
  // colour is left out so the backend gives the palette's next one.
  importCalendarAsNew: (file: File, displayName: string, color: string, tz: string) => {
    const form = new FormData()
    form.append('file', file)
    form.append('displayName', displayName)
    if (color) form.append('color', color)
    return request<CalendarImportOutcome>(
      'POST', `/api/Calendars/Import?tz=${encodeURIComponent(tz)}`, form)
  },

  // from/to are ISO instants (…Z); tz only decides which day a floating instance falls on.
  getOccurrences: (from: string, to: string, tz: string) =>
    request<OccurrenceListResponse>('GET',
      `/api/Calendar/Events?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}&tz=${encodeURIComponent(tz)}`),

  searchEvents: (q: string) =>
    request<OccurrenceListResponse>('GET', `/api/Calendar/Events/Search?q=${encodeURIComponent(q)}`),

  getEvent: (id: string) =>
    request<EventDetail>('GET', `/api/Calendar/Events/${id}`),

  createEvent: (event: EventWrite) =>
    request<CreatedId>('POST', '/api/Calendar/Events', event),

  updateEvent: (id: string, event: EventUpdateBody) =>
    request<EventUpdated>('PUT', `/api/Calendar/Events/${id}`, event),

  // scope/instanceId travel in the query string, the same rule the PUT of a narrow edit follows;
  // language is the one the cancellations are written in.
  deleteEvent: (id: string, scope: EditScope, instanceId?: string, language?: string) => {
    const params = new URLSearchParams({ scope })
    if (instanceId) params.set('instanceId', instanceId)
    if (language) params.set('language', language)
    return request<null>('DELETE', `/api/Calendar/Events/${id}?${params}`)
  },
}
