import { useCallback, useEffect, useRef } from 'react'
import { useMatch, useSearchParams } from 'react-router'
import { readStored, writeStored } from '../../lib/safeStorage'
import { addDays, isPlainDate, shiftMonth, splitPlainDate, type PlainDate } from './plainDate'
import { LIST_DAYS, type View } from './windowOf'

export const VIEWS: View[] = ['day', 'week', 'month', 'list']
/** Day is the phone's week: seven columns in 360px is six unreadable ones and a sideways scroll.
    Month leads: a phone opens on the shape of the month. */
export const PHONE_VIEWS: View[] = ['month', 'day', 'list']
const VIEW_KEY = 'calendar.view'

function isView(value: string | null): value is View {
  return value !== null && (VIEWS as string[]).includes(value)
}

/** The device remembers the view, never the account: a 4K screen and a laptop want different
    ones, exactly as the splitter sizes do. A blocked store leaves the default standing. */
function storedView(): View | null {
  const stored = readStored(VIEW_KEY)
  return isView(stored) ? stored : null
}

function rememberView(view: View) {
  writeStored(VIEW_KEY, view)
}

/** Same day of the month, clamped: 31 January plus a month is the last day of February. */
function addMonths(day: PlainDate, delta: number): PlainDate {
  const { year, month, date } = splitPlainDate(day)
  const next = shiftMonth({ year, month }, delta)
  const last = new Date(Date.UTC(next.year, next.month, 0)).getUTCDate()
  return new Date(Date.UTC(next.year, next.month - 1, Math.min(date, last))).toISOString().slice(0, 10)
}

const STEP_DAYS: Record<Exclude<View, 'month'>, number> = { day: 1, week: 7, list: LIST_DAYS }

export function stepAnchor(view: View, anchor: PlainDate, delta: number): PlainDate {
  return view === 'month' ? addMonths(anchor, delta) : addDays(anchor, STEP_DAYS[view] * delta)
}

export function useCalendarUrlState(phone: boolean, today: PlainDate) {
  const [params, setParams] = useSearchParams()
  const rawView = params.get('view')
  const rawDate = params.get('date')
  // Week is the one view a phone cannot draw: seven columns in 360px is six unreadable ones.
  const chosen = isView(rawView) ? rawView : storedView() ?? 'week'
  const view: View = phone && chosen === 'week' ? 'day' : chosen
  const anchor: PlainDate = rawDate && isPlainDate(rawDate) ? rawDate : today

  // Replace, not push: Back must leave the module rather than bounce off the normalisation.
  useEffect(() => {
    if (rawView === view && rawDate === anchor) return
    setParams(previous => {
      const next = new URLSearchParams(previous)
      next.set('view', view)
      next.set('date', anchor)
      return next
    }, { replace: true })
  }, [rawView, rawDate, view, anchor, setParams])

  // A date still on the day that just ended is the one the normalisation wrote, or one the user
  // left on today: it moves on with the day, in place. Any other date was chosen, and stays.
  const shownToday = useRef(today)
  useEffect(() => {
    const ended = shownToday.current
    shownToday.current = today
    if (ended === today || rawDate !== ended) return
    setParams(previous => {
      const next = new URLSearchParams(previous)
      next.set('date', today)
      return next
    }, { replace: true })
  }, [today, rawDate, setParams])

  const creating = useMatch('/calendar/new') != null
  const editMatch = useMatch('/calendar/:id/edit')
  const routeId = editMatch?.params.id ?? null
  const inEditor = creating || routeId != null
  const instanceParam = params.get('instance') ?? ''

  /** Every navigation inside the module keeps the grid where it was: the editor is a surface over
      this screen, not a trip away from it. */
  const searchWith = useCallback((extra: Record<string, string> = {}) => {
    const next = new URLSearchParams({ view, date: anchor, ...extra })
    return `?${next.toString()}`
  }, [view, anchor])

  const setView = useCallback((next: View) => {
    rememberView(next)
    setParams(previous => {
      const search = new URLSearchParams(previous)
      search.set('view', next)
      return search
    })
  }, [setParams])

  const setAnchor = useCallback((day: PlainDate) => {
    setParams(previous => {
      const search = new URLSearchParams(previous)
      search.set('date', day)
      return search
    })
  }, [setParams])

  return { params, view, anchor, routeId, inEditor, instanceParam, searchWith, setView, setAnchor }
}
