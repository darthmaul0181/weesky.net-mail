import { useCallback, useEffect } from 'react'
import { useMatch, useSearchParams } from 'react-router'
import { addDays, isPlainDate, splitPlainDate, type PlainDate } from './plainDate'
import type { View } from './windowOf'

const VIEWS: View[] = ['day', 'week', 'month', 'list']
const VIEW_KEY = 'calendar.view'

function isView(value: string | null): value is View {
  return value !== null && (VIEWS as string[]).includes(value)
}

/** The device remembers the view, never the account: a 4K screen and a laptop want different
    ones, exactly as the splitter sizes do. A blocked store leaves the default standing. */
function storedView(): View | null {
  try {
    const stored = localStorage.getItem(VIEW_KEY)
    return isView(stored) ? stored : null
  } catch {
    return null
  }
}

function rememberView(view: View) {
  try {
    localStorage.setItem(VIEW_KEY, view)
  } catch { /* a private window refuses the write; the URL still carries the choice */ }
}

/** Same day of the month, clamped: 31 January plus a month is the last day of February. */
function addMonths(day: PlainDate, delta: number): PlainDate {
  const { year, month, date } = splitPlainDate(day)
  const index = (year * 12 + month - 1) + delta
  const target = new Date(Date.UTC(Math.floor(index / 12), index % 12, 1))
  const last = new Date(Date.UTC(target.getUTCFullYear(), target.getUTCMonth() + 1, 0)).getUTCDate()
  target.setUTCDate(Math.min(date, last))
  return target.toISOString().slice(0, 10)
}

const STEP_DAYS: Record<Exclude<View, 'month'>, number> = { day: 1, week: 7, list: 30 }

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
