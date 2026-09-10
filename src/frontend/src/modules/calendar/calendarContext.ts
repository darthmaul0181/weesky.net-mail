import { createContext, useContext } from 'react'
import type { WeekRules } from './calendarLocale'
import type { Calendar, EditScope, Occurrence } from './calendarTypes'
import type { PlainDate } from './plainDate'
import type { View, Window } from './windowOf'

export interface CalendarContextValue {
  tz: string
  rules: WeekRules
  lang: string
  /** `navigator.language`, resolved once: every screen that formats a date grafts the same
      region onto the interface language, and a second reading is a second answer. */
  region: string
  cycle: 'h12' | 'h23'
  view: View
  anchor: PlainDate
  today: PlainDate
  setView: (view: View) => void
  setAnchor: (day: PlainDate) => void
  calendars: Calendar[]
  calendarById: Map<string, Calendar>
  window: Window
  occurrences: Occurrence[] | undefined
  /** The occurrences the checked calendars hold, filtered once here: every view places the same
      list rather than each re-reading the boxes. */
  visible: Occurrence[]
  windowError: string | null
  retryWindow: () => void
  openEditor: (id: string, instanceId?: string) => void
  createAt: (start: Date, end: Date, allDay: boolean) => void
  /**
   * The recurring-scope question, as a promise: the layout owns the dialog, every caller awaits
   * its answer. `null` is the ✕ — the gesture is abandoned, nothing is written. Task 6's drop is
   * the second caller after the editor and the preview.
   */
  askScope: (mode: 'save' | 'delete', name: string, repeatText: string | null,
    allowed?: EditScope[]) => Promise<EditScope | null>
  /** A pointer gesture has begun on the grid: the bubble is anchored to a chip that is about to
      move, and it hangs over the very surface being drawn on. */
  startGesture: () => void
  /** The two drops the grid can make. The hooks that raise them know nothing of React Query —
      the layout resolves the event, asks the scope of a series and writes. */
  moveOccurrence: (o: Occurrence, deltaMinutes: number, deltaDays: number) => void
  resizeOccurrence: (o: Occurrence, newDurationMinutes: number) => void
}

/** Its own file rather than `CalendarLayout`'s: every view and the editor read this, and the
    layout imports every one of them back — a cycle that held only because `useCalendar` is a
    hoisted declaration nobody calls at module evaluation. */
export const CalendarContext = createContext<CalendarContextValue | null>(null)

export function useCalendar(): CalendarContextValue {
  const ctx = useContext(CalendarContext)
  if (!ctx) throw new Error('useCalendar must be used within CalendarLayout')
  return ctx
}
