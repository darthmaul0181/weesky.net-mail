import { Fragment, useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { useCalendar } from './calendarContext'
import { monthGrid, weekNumberOf } from './calendarLocale'
import type { Occurrence } from './calendarTypes'
import EventChip from './EventChip'
import { itemsByDay, placeAll } from './multiDay'
import { colorOf, occurrenceKey } from './occurrenceStyle'
import type { PlainDate } from './plainDate'

export interface MonthViewProps {
  onOpen(o: Occurrence, anchor: HTMLElement): void
  onOpenEditor(o: Occurrence): void
  selectedKey?: string
}

/** Past this a cell would draw more rows than it has, whatever the window's height. */
const MAX_PER_CELL = 3

/**
 * The month, always on the six rows `monthGrid` answers: a grid that changed height between
 * September and October would move every row under the cursor. A row entirely outside the month
 * is drawn sunken like each of its cells rather than dropped.
 */
export default function MonthView({ onOpen, onOpenEditor, selectedKey }: MonthViewProps) {
  const { t } = useTranslation('calendar')
  const { tz, rules, anchor, today, visible, calendarById, setView, setAnchor } = useCalendar()

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

  return (
    <div className="month-view">
      {weeks.map(week => (
        <Fragment key={week[0]}>
          <div className="month-week-number">{weekNumberOf(week[0], rules)}</div>
          {week.map(day => {
            const items = byDay.get(day) ?? []
            const hidden = items.length - MAX_PER_CELL
            return (
              <div key={day} className={`month-cell${day.slice(0, 7) === month ? '' : ' is-outside'}${day === today ? ' is-today' : ''}`}>
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
