import { render } from '@testing-library/react'
import type { ReactNode } from 'react'
import { CalendarContext, type CalendarContextValue } from './calendarContext'
import type { Calendar, Occurrence } from './calendarTypes'
import { windowOf } from './windowOf'

/** The zone every grid test states out loud: a top computed against the machine's own would
    answer one thing in Brussels and another on a UTC runner. */
export const TZ = 'Europe/Brussels'
export const RULES = { firstDay: 1, minimalDays: 4 } as const

export function calendarOf(id: string, color = '#3b82c4', displayName = id): Calendar {
  return {
    id, davName: id, displayName, description: '', color, order: 0,
    timeZone: TZ, isVisible: true, isDefault: false,
  }
}

export function occurrenceOf(fields: Partial<Occurrence> & { eventId: string }): Occurrence {
  return {
    calendarId: 'a', uid: fields.eventId, instanceId: '', isOverride: false, isAllDay: false,
    isFloating: false, transparency: 'OPAQUE', hasAlarm: false, ...fields,
  }
}

/** Every view reads the module's context rather than a dozen props; this is that context with
    nothing behind it, so a view mounts with neither a router nor a query client. */
export function renderInCalendar(node: ReactNode, overrides: Partial<CalendarContextValue> = {}) {
  const anchor = overrides.anchor ?? '2026-09-16'
  const view = overrides.view ?? 'week'
  const calendars = overrides.calendars ?? [calendarOf('a')]
  const value: CalendarContextValue = {
    tz: TZ, rules: RULES, lang: 'en', region: 'en-BE', cycle: 'h23',
    view, anchor, today: '2026-09-16',
    setView: () => {}, setAnchor: () => {},
    calendars, calendarById: new Map(calendars.map(one => [one.id, one])),
    window: windowOf(view, anchor, TZ, RULES),
    occurrences: [], visible: [],
    windowError: null, retryWindow: () => {},
    openEditor: () => {}, createAt: () => {}, askScope: async () => 'All',
    startGesture: () => {}, moveOccurrence: () => {}, resizeOccurrence: () => {},
    ...overrides,
  }
  return render(<CalendarContext.Provider value={value}>{node}</CalendarContext.Provider>)
}
