import { Fragment, useMemo, type MouseEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { useCalendar } from './calendarContext'
import { monthGrid, weekNumberOf } from './calendarLocale'
import type { Occurrence } from './calendarTypes'
import EventChip from './EventChip'
import { itemsByDay, placeAll, wallClockOf } from './multiDay'
import { colorOf, occurrenceKey } from './occurrenceStyle'
import { type PlainDate, utcOfLocalTime } from './plainDate'

export interface MonthViewProps {
  onOpen(o: Occurrence, anchor: HTMLElement): void
  onOpenEditor(o: Occurrence): void
  selectedKey?: string
  /** A click on an empty cell is spent closing an open bubble, the hour grid's rule. */
  previewOpen?: boolean
}

/** Past this a cell would draw more rows than it has, whatever the window's height. */
const MAX_PER_CELL = 3
/** What a click on a day with nothing to go by opens: nine to ten. */
const DEFAULT_START = 9 * 60
const HOUR = 60
const QUARTER = 15

/** The hour a click on the empty part of a cell means. A month cell names a day and no hour, but
    its chips are stacked in order, so where the click lands among them is the one hint there is:
    below a timed chip, the new event starts when that one ends; above the first, it ends when
    that one starts; on a day with no timed chip, nine o'clock. */
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
  if (above !== null) return Math.min(Math.ceil(above / QUARTER) * QUARTER, 23 * HOUR)
  if (below !== null) return Math.max(below - HOUR, 0)
  return DEFAULT_START
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
    tz, rules, anchor, today, visible, calendarById, setView, setAnchor, createAt,
  } = useCalendar()

  const weeks = useMemo(
    () => monthGrid(Number(anchor.slice(0, 4)), Number(anchor.slice(5, 7)), rules),
    [anchor, rules])
  const days = useMemo(() => weeks.flat(), [weeks])
  const byDay = useMemo(
    () => itemsByDay(placeAll(visible, tz, days), days), [visible, tz, days])
  const month = anchor.slice(0, 7)

  const openDay = (day: PlainDate) => {
    setView('day')
    setAnchor(day)
  }

  /** The chips and the count are buttons with clicks of their own, which bubble here: only a
      click that started on the cell itself — its empty part — is a creation. */
  const onCellClick = (day: PlainDate, timed: Occurrence[], event: MouseEvent<HTMLDivElement>) => {
    if ((event.target as HTMLElement).closest('.event-chip, .month-more')) return
    if (previewOpen) return
    const start = startMinuteOf(event.currentTarget, event.clientY, timed, tz)
    createAt(utcOfLocalTime(day, start, tz), utcOfLocalTime(day, start + HOUR, tz), false)
  }

  return (
    <div className="month-view">
      {weeks.map(week => (
        <Fragment key={week[0]}>
          <div className="month-week-number">{weekNumberOf(week[0], rules)}</div>
          {week.map(day => {
            const items = byDay.get(day) ?? []
            const hidden = items.length - MAX_PER_CELL
            // The timed chips the cell draws, in the order it draws them — what the click's
            // height is read against. Bands carry no hour and are skipped by the selector.
            const timed = items.slice(0, MAX_PER_CELL)
              .filter(({ band }) => !band).map(({ occurrence }) => occurrence)
            return (
              <div key={day} className={`month-cell${day.slice(0, 7) === month ? '' : ' is-outside'}${day === today ? ' is-today' : ''}`}
                onClick={event => onCellClick(day, timed, event)}>
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
        </Fragment>
      ))}
    </div>
  )
}
