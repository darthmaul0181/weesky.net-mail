import type { CSSProperties } from 'react'
import { dateFormat } from '../../../lib/intl'
import { useCalendar } from '../calendarContext'
import { dateLocaleOf, dayNames, monthGrid } from '../calendarLocale'
import { type PlainDate, utcMidnightOf } from '../plainDate'

export interface PhoneMonthProps {
  /** The month the grid draws; the toolbar names it. */
  anchor: PlainDate
  selected: PlainDate
  /** The colours of the day's first events. */
  dotsByDay: Map<PlainDate, string[]>
  onPick(day: PlainDate): void
}

/** Three is what a 48px cell holds beside its number; a fourth is drawn outside it. */
const MAX_DOTS = 3

/**
 * The phone's month: a picker rather than a grid of events. A 360px screen gives a cell 48px,
 * which is a number and a hint of what the day holds — so the day's own events are listed under
 * it instead of crammed inside it.
 */
export default function PhoneMonth({ anchor, selected, dotsByDay, onPick }: PhoneMonthProps) {
  const { rules, lang, region, today } = useCalendar()
  const locale = dateLocaleOf(lang, region)
  const month = anchor.slice(0, 7)
  const grid = monthGrid(Number(anchor.slice(0, 4)), Number(anchor.slice(5, 7)), rules)
  const fullDate = dateFormat(
    { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' }, locale)

  return (
    <div className="phone-month">
      <div className="phone-month-names">
        {dayNames(locale, rules, 'narrow').map((name, index) => (
          <span key={index} className="phone-day-name">{name}</span>
        ))}
      </div>

      {grid.map(week => (
        <div key={week[0]} className="phone-month-week">
          {week.map(day => {
            const dots = (dotsByDay.get(day) ?? []).slice(0, MAX_DOTS)
            const classes = ['phone-month-cell']
            if (day.slice(0, 7) !== month) classes.push('is-outside')
            if (day === today) classes.push('is-today')
            if (day === selected) classes.push('is-selected')
            return (
              <button key={day} type="button" data-day={day} className={classes.join(' ')}
                aria-label={fullDate.format(utcMidnightOf(day))}
                aria-current={day === selected ? 'date' : undefined}
                onClick={() => onPick(day)}>
                <span className="phone-month-number">{Number(day.slice(8))}</span>
                <span className="phone-month-dots">
                  {dots.map((colour, index) => (
                    <span key={index} className="phone-month-dot"
                      style={{ '--cal': colour } as CSSProperties} />
                  ))}
                </span>
              </button>
            )
          })}
        </div>
      ))}
    </div>
  )
}
