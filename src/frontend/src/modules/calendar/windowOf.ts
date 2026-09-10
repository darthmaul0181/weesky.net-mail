import { monthGrid, startOfWeek, type WeekRules } from './calendarLocale'
import { addDays, type PlainDate, utcOfLocalMidnight } from './plainDate'

export type View = 'day' | 'week' | 'month' | 'list'

/**
 * What one screenful asks the API for, and what it draws. The two are not the same range:
 * `from`/`to` are the ISO instants the request carries, `firstVisible`/`lastVisible` the days the
 * grid actually holds.
 */
export interface Window {
  from: string
  to: string
  firstVisible: PlainDate
  lastVisible: PlainDate
}

/** Days of slack on each side of a grid. An occurrence is selected by its instant, and a zone
    fourteen hours away puts a Tuesday morning in Sydney on Monday evening here. */
const SLACK_DAYS = 1

/** The list is thirty-one days from the day it is anchored on, and it is its own window: it
    shows exactly what it asks for, so slack would only draw days the screen has no room for. */
const LIST_DAYS = 31

function visibleOf(
  view: View, anchor: PlainDate, rules: WeekRules,
): [PlainDate, PlainDate] {
  if (view === 'day') return [anchor, anchor]
  if (view === 'week') {
    const first = startOfWeek(anchor, rules)
    return [first, addDays(first, 6)]
  }
  if (view === 'month') {
    const grid = monthGrid(Number(anchor.slice(0, 4)), Number(anchor.slice(5, 7)), rules)
    return [grid[0][0], grid[5][6]]
  }
  // The anchor, not today: the chevrons, Today and the mini-month all move it, and a list that
  // ignored it would leave three controls dead on one of the four views.
  return [anchor, addDays(anchor, LIST_DAYS - 1)]
}

export function windowOf(view: View, anchor: PlainDate, tz: string, rules: WeekRules): Window {
  const [firstVisible, lastVisible] = visibleOf(view, anchor, rules)
  const slack = view === 'list' ? 0 : SLACK_DAYS

  return {
    // Each edge reads the offset of its own date: on the day the clocks go back, the two ends of
    // a window are an hour apart in offset and a single reading would drift one of them.
    from: utcOfLocalMidnight(addDays(firstVisible, -slack), tz).toISOString(),
    to: utcOfLocalMidnight(addDays(lastVisible, 1 + slack), tz).toISOString(),
    firstVisible,
    lastVisible,
  }
}
