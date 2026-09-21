import { useMemo, useRef, type KeyboardEvent, type MouseEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { useGridNav } from '../../hooks/useGridNav'
import { useCalendar } from './calendarContext'
import {
  dateLocaleOf, formatLongDay, formatRangeTitle, monthGrid, weekNumberOf,
} from './calendarLocale'
import type { Occurrence } from './calendarTypes'
import EventChip from './EventChip'
import { itemsByDay, placeAll, wallClockOf } from './multiDay'
import { colorOf, occurrenceKey } from './occurrenceStyle'
import { type PlainDate, utcOfLocalTime } from './plainDate'

export interface MonthViewProps {
  onOpen(o: Occurrence, anchor: HTMLElement): void
  onOpenEditor(o: Occurrence): void
  selectedKey?: string
  /** A click or an Enter on an empty cell is spent closing an open bubble, the hour grid's
      rule: the bubble has no trap, so Shift+Tab comes back to the grid with it still standing. */
  previewOpen?: boolean
}

/** Past this a cell would draw more rows than it has, whatever the window's height. */
const MAX_PER_CELL = 3
/** What a click on a day with nothing to go by opens: nine to ten. */
const DEFAULT_START = 9 * 60
const HOUR = 60
const QUARTER = 15

/** The hour a month cell means, once the chips on either side of the point are known: after the
    one above, rounded up to the quarter, before the one below, or nine o'clock with neither. */
function startMinuteBetween(above: number | null, below: number | null): number {
  if (above !== null) return Math.min(Math.ceil(above / QUARTER) * QUARTER, 23 * HOUR)
  if (below !== null) return Math.max(below - HOUR, 0)
  return DEFAULT_START
}

/** A month cell names a day and no hour, but its chips are stacked in order, so where the click
    lands among them is the one hint there is. Bands carry no hour and are skipped. */
function startMinuteOf(cell: HTMLElement, clientY: number, timed: Occurrence[], tz: string): number {
  const chips = [...cell.querySelectorAll<HTMLElement>('.event-chip.is-month')]
  let above: number | null = null
  let below: number | null = null
  chips.forEach((chip, index) => {
    const rect = chip.getBoundingClientRect()
    const [start, end] = wallClockOf(timed[index], tz)
    if (rect.bottom <= clientY) above = end.minute
    else if (rect.top >= clientY && below === null) below = start.minute
  })
  return startMinuteBetween(above, below)
}

/** The same rule with no pointer to read it against: a key creates below every chip the cell
    drew, which is the empty part of it a click would have had to aim at anyway. */
function startMinuteBelowAll(timed: Occurrence[], tz: string): number {
  const last = timed[timed.length - 1]
  return startMinuteBetween(last ? wallClockOf(last, tz)[1].minute : null, null)
}

/**
 * The month, always on the six rows `monthGrid` answers: a grid that changed height between
 * September and October would move every row under the cursor. A row entirely outside the month
 * is drawn sunken like each of its cells rather than dropped.
 */
export default function MonthView({
  onOpen, onOpenEditor, selectedKey, previewOpen,
}: MonthViewProps) {
  const { t } = useTranslation('calendar')
  const {
    tz, rules, lang, region, anchor, today, visible, calendarById, setView, setAnchor, createAt,
  } = useCalendar()
  // The grid is this component's own root, so it stands at the hook's first layout effect
  // without the small component `MessageGrid` and `ContactGrid` were extracted to be.
  const grid = useRef<HTMLDivElement>(null)
  useGridNav({ ref: grid, cellEntry: true })

  const weeks = useMemo(
    () => monthGrid(Number(anchor.slice(0, 4)), Number(anchor.slice(5, 7)), rules),
    [anchor, rules])
  const days = useMemo(() => weeks.flat(), [weeks])
  const byDay = useMemo(
    () => itemsByDay(placeAll(visible, tz, days), days), [visible, tz, days])
  const month = anchor.slice(0, 7)
  const locale = dateLocaleOf(lang, region)

  const openDay = (day: PlainDate) => {
    setView('day')
    setAnchor(day)
  }

  const createOn = (day: PlainDate, start: number) => createAt(
    utcOfLocalTime(day, start, tz), utcOfLocalTime(day, start + HOUR, tz), false)

  /** The chips and the count are buttons with clicks of their own, which bubble here: only a
      click that started on the cell itself — its empty part — is a creation. */
  const onCellClick = (day: PlainDate, timed: Occurrence[], event: MouseEvent<HTMLDivElement>) => {
    if ((event.target as HTMLElement).closest('.event-chip, .month-more')) return
    if (previewOpen) return
    createOn(day, startMinuteOf(event.currentTarget, event.clientY, timed, tz))
  }

  /** Enter creates, where the pattern would also enter the cell: creating is a day's primary
      action and has to mean the same thing on an empty day as on a full one, and F2 is already
      the documented way in. A key from a chip inside the cell is the chip's own. */
  const onCellKey = (day: PlainDate, timed: Occurrence[], event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key !== 'Enter' || event.target !== event.currentTarget || previewOpen) return
    event.preventDefault()
    createOn(day, startMinuteBelowAll(timed, tz))
  }

  return (
    <div className="month-view" role="grid" ref={grid}
      aria-label={t('views.monthGrid', {
        month: formatRangeTitle(anchor, anchor, 'month', lang, region),
      })}>
      {weeks.map(week => (
        <div className="month-week" role="row" key={week[0]}>
          <div className="month-week-number" role="rowheader">{weekNumberOf(week[0], rules)}</div>
          {week.map(day => {
            const items = byDay.get(day) ?? []
            const hidden = items.length - MAX_PER_CELL
            // The timed chips the cell draws, in the order it draws them — what the click's
            // height is read against. Bands carry no hour and are skipped by the selector.
            const timed = items.slice(0, MAX_PER_CELL)
              .filter(({ band }) => !band).map(({ occurrence }) => occurrence)
            // A name on the cell is what a reader hears instead of what the cell holds, so an
            // empty Tuesday and one carrying three events would sound the same — and nothing
            // would say there is an F2 to press. The count is drawn and hidden alike.
            const date = formatLongDay(day, locale, true)
            return (
              // The cell is the activation target itself, so it carries the roving attribute's
              // constant -1 and the widgets it holds are reached with F2 rather than walked into.
              <div key={day} role="gridcell" tabIndex={-1}
                aria-label={items.length > 0
                  ? t('views.dayCell', { date, count: items.length }) : date}
                aria-current={day === today ? 'date' : undefined}
                // The stop the hook opens on, ahead of today wherever both are on screen: the
                // layout keys this grid on the month, so a step reopens it here rather than
                // leaving Tab on the first cell, an outside day of the month before.
                aria-selected={day === anchor ? true : undefined}
                className={`month-cell${day.slice(0, 7) === month ? '' : ' is-outside'}${day === today ? ' is-today' : ''}`}
                onClick={event => onCellClick(day, timed, event)}
                onKeyDown={event => onCellKey(day, timed, event)}>
                <span className="month-day-number">{Number(day.slice(8))}</span>
                {items.slice(0, MAX_PER_CELL).map(({ occurrence, band }) => (
                  <EventChip key={occurrenceKey(occurrence)} occurrence={occurrence}
                    color={colorOf(occurrence, calendarById)} variant={band ? 'band' : 'month'}
                    selected={occurrenceKey(occurrence) === selectedKey}
                    onOpen={onOpen} onOpenEditor={onOpenEditor} />
                ))}
                {hidden > 0 && (
                  <button type="button" className="month-more" onClick={() => openDay(day)}>
                    {t('views.more', { count: hidden })}
                  </button>
                )}
              </div>
            )
          })}
        </div>
      ))}
    </div>
  )
}
