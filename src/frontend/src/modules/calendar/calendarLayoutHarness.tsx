import { QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterEach, beforeEach, vi, type Mock } from 'vitest'
import CalendarLayout from './CalendarLayout'
import { calendarNamed, occurrenceOf } from './calendarTestHarness'
import type { EventUpdated } from './calendarTypes'
import * as apiModule from '../../api'
import { createTestQueryClient, resetViewport, setupUser } from '../../test-utils'

/** The mocked api, typed: every file using this harness first runs
    `vi.mock('../../api.js', () => import('./calendarApiMock'))`. */
export const api = apiModule.api as unknown as Record<'getCalendars' | 'createCalendar'
  | 'updateCalendar' | 'setCalendarVisible' | 'deleteCalendar' | 'exportCalendar' | 'importCalendar'
  | 'importCalendarAsNew' | 'searchEvents' | 'getEvent' | 'createEvent' | 'deleteEvent'
  | 'getContacts' | 'setBirthdays' | 'getPreferences',
  Mock<(...args: unknown[]) => unknown>>
  & { getOccurrences: Mock<typeof apiModule.api.getOccurrences>; updateEvent: Mock<typeof apiModule.api.updateEvent> }
export const ApiError = apiModule.ApiError as unknown as new (message: string, status: number) => Error

export const BROWSER_TZ = Intl.DateTimeFormat().resolvedOptions().timeZone
/** A save that mailed nobody: what every write here answers unless a case says otherwise. */
export const UPDATED: EventUpdated = { scheduling: { sent: 0 } }

export const calendar = (id: string, displayName: string, isDefault = false) =>
  calendarNamed(id, displayName, isDefault, { timeZone: BROWSER_TZ })

export const CALENDARS = [calendar('a', 'Personal', true), calendar('b', 'Work')]

/** This test's delay-free userEvent, set up afresh by `installLayoutMocks` before each one. */
export let user = setupUser()

/** Every file's defaults: an empty window, the two calendars, no preference stored. */
export function installLayoutMocks() {
  afterEach(resetViewport)
  beforeEach(() => {
    user = setupUser()
    localStorage.clear()
    vi.clearAllMocks()
    api.getCalendars.mockResolvedValue({ calendars: CALENDARS })
    api.getOccurrences.mockResolvedValue({ occurrences: [] })
    api.searchEvents.mockResolvedValue({ occurrences: [] })
    // The preview now always fetches the detail; a test with nothing to say about it
    // still needs an answer, or the query settles on the undefined react-query refuses to hold.
    api.getEvent.mockResolvedValue(detail())
    api.getContacts.mockResolvedValue({ contacts: [] })
    api.getPreferences.mockResolvedValue({})
  })
}

export function occurrence(eventId: string, summary: string) {
  return occurrenceOf({
    eventId, summary, startUtc: '2026-09-16T07:00:00Z', endUtc: '2026-09-16T08:00:00Z',
  })
}

/** Floating on purpose: a wall clock is read as it is written, so the seeded hour is the same
    number on a Brussels laptop and on a UTC runner. */
export function floating(eventId: string, summary: string, instanceId = '') {
  return {
    ...occurrence(eventId, summary), isFloating: true, instanceId,
    startUtc: undefined, endUtc: undefined,
    localStart: '2026-09-16T09:00:00', localEnd: '2026-09-16T10:00:00',
  }
}

export const REPEAT = { frequency: 'MONTHLY', interval: 6, byDay: [] as string[], end: 'Never' }

export function detail(fields: Record<string, unknown> = {}) {
  return {
    id: 'e1', calendarId: 'a', uid: 'u1', icsHash: 'h1',
    fields: {
      calendarId: 'a', summary: 'Dentist', isAllDay: false,
      // Deliberately another day: what the editor shows must come from the occurrence.
      start: '2026-01-05T14:00:00', end: '2026-01-05T15:00:00', timeZone: BROWSER_TZ,
      reminderMinutesBefore: [], availability: 'Busy', visibility: 'Default', ...fields,
    },
    attendees: [], repeatIsExact: true, foreignAlarms: [], canInvite: true,
  }
}

const routes = [
  { path: '/calendar', element: <CalendarLayout /> },
  { path: '/calendar/new', element: <CalendarLayout /> },
  { path: '/calendar/:id/edit', element: <CalendarLayout /> },
  { path: '/contacts', element: null },
]

/** `previous` puts an entry under `path`, so a test can press the browser's own Back. */
export function mount(path = '/calendar', previous?: string) {
  const client = createTestQueryClient()
  const router = createMemoryRouter(routes, {
    initialEntries: previous ? [previous, path] : [path],
    initialIndex: previous ? 1 : 0,
  })
  render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>)
  return { router, client }
}

export const renderAt = (path?: string) => mount(path).router

export const params = (router: ReturnType<typeof renderAt>) =>
  new URLSearchParams(router.state.location.search)
