import { vi } from 'vitest'

/** The api module as every CalendarLayout test sees it: `vi.mock('../../api.js', () => import('./calendarApiMock'))`. */
export const api = {
  getCalendars: vi.fn(), createCalendar: vi.fn(), updateCalendar: vi.fn(),
  setCalendarVisible: vi.fn(), deleteCalendar: vi.fn(), exportCalendar: vi.fn(),
  importCalendar: vi.fn(), importCalendarAsNew: vi.fn(),
  getOccurrences: vi.fn(), searchEvents: vi.fn(), getEvent: vi.fn(),
  createEvent: vi.fn(), updateEvent: vi.fn(), deleteEvent: vi.fn(), getContacts: vi.fn(),
  setBirthdays: vi.fn(),
  getPreferences: vi.fn(),
}

// The very class the layout imports from the mocked module, so `instanceof ApiError` holds
// against what these tests throw; a locally-declared twin fails that check.
export class ApiError extends Error {
  status: number
  constructor(message: string, status: number) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}
