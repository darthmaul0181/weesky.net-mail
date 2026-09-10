import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { dateFormat } from '../../lib/intl'
import ChevronLeftIcon from '../../icons/ChevronLeftIcon'
import ChevronRightIcon from '../../icons/ChevronRightIcon'
import { dayNames, monthGrid, weekNumberOf, type WeekRules } from './calendarLocale'
import { type PlainDate, utcMidnightOf } from './plainDate'

export interface MiniMonthProps {
  /** The day the grid is showing, filled. The month follows it until the arrows move on. */
  anchor: PlainDate
  today: PlainDate
  rules: WeekRules
  /** Already region-grafted by `dateLocaleOf`: the layout resolves it once. */
  locale: string
  onPick: (day: PlainDate) => void
}

interface Cursor { year: number; month: number }

function cursorOf(day: PlainDate): Cursor {
  return { year: Number(day.slice(0, 4)), month: Number(day.slice(5, 7)) }
}

function shift({ year, month }: Cursor, delta: number): Cursor {
  const index = (year * 12 + month - 1) + delta
  return { year: Math.floor(index / 12), month: (index % 12) + 1 }
}

/**
 * The sidebar's month picker. It walks a month at a time on its own and never moves the anchor
 * doing it — looking ahead is not choosing — but it follows the anchor whenever the grid does,
 * so picking a week in the toolbar cannot leave the two showing different months.
 */
export default function MiniMonth({ anchor, today, rules, locale, onPick }: MiniMonthProps) {
  const { t } = useTranslation('calendar')
  const [cursor, setCursor] = useState<Cursor>(() => cursorOf(anchor))
  const [followed, setFollowed] = useState(anchor)

  if (followed !== anchor) {
    setFollowed(anchor)
    setCursor(cursorOf(anchor))
  }

  const grid = monthGrid(cursor.year, cursor.month, rules)
  const month = String(cursor.month).padStart(2, '0')
  const heading = dateFormat({ month: 'long', year: 'numeric', timeZone: 'UTC' }, locale)
    .format(utcMidnightOf(`${cursor.year}-${month}-01`))
  const fullDate = dateFormat(
    { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' }, locale)

  return (
    <div className="mini-month">
      <div className="mini-month-head">
        <span className="mini-month-title">{heading}</span>
        <button type="button" className="mini-month-step" aria-label={t('sidebar.previousMonth')}
          onClick={() => setCursor(shift(cursor, -1))}><ChevronLeftIcon size={14} /></button>
        <button type="button" className="mini-month-step" aria-label={t('sidebar.nextMonth')}
          onClick={() => setCursor(shift(cursor, 1))}><ChevronRightIcon size={14} /></button>
      </div>

      <div className="mini-month-names">
        <span className="mini-week-number" />
        {dayNames(locale, rules, 'narrow').map((name, index) => (
          <span key={index} className="mini-day-name">{name}</span>
        ))}
      </div>

      {grid.map(week => (
        <div key={week[0]} className="mini-month-week">
          <span className="mini-week-number">{weekNumberOf(week[0], rules)}</span>
          {week.map(day => {
            const outside = day.slice(0, 7) !== `${cursor.year}-${month}`
            return (
              <button key={day} type="button" onClick={() => onPick(day)}
                aria-label={fullDate.format(utcMidnightOf(day))}
                aria-current={day === anchor ? 'date' : undefined}
                className={`mini-day${outside ? ' is-outside' : ''}`
                  + `${day === anchor ? ' is-anchor' : ''}${day === today ? ' is-today' : ''}`}>
                {Number(day.slice(8))}
              </button>
            )
          })}
        </div>
      ))}
    </div>
  )
}
