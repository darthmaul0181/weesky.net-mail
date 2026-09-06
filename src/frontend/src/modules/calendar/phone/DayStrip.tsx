import { useLayoutEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { dateFormat } from '../../../lib/intl'
import { useCalendar } from '../calendarContext'
import { dateLocaleOf, startOfWeek } from '../calendarLocale'
import { addDays, type PlainDate, utcMidnightOf } from '../plainDate'

export interface DayStripProps {
  selected: PlainDate
  onPick(day: PlainDate): void
}

/** Two weeks either side of the one on screen: a swipe reaches them without a request, and the
    strip is rebuilt around whatever day is picked, so the band never runs out on one side. */
const BEFORE = 2
const AFTER = 2

/**
 * The week over the phone's day grid. It is a scroll-snap band of whole weeks rather than a
 * gesture of our own: the browser's own snapping handles the fling, the rubber band and the
 * accessibility of it, and nothing here has to measure a pointer.
 */
export default function DayStrip({ selected, onPick }: DayStripProps) {
  const { t } = useTranslation('calendar')
  const { rules, lang, region, today } = useCalendar()
  const locale = dateLocaleOf(lang, region)
  const first = startOfWeek(selected, rules)
  const weeks = Array.from({ length: BEFORE + 1 + AFTER }, (_, index) =>
    Array.from({ length: 7 }, (_, day) => addDays(first, (index - BEFORE) * 7 + day)))

  const band = useRef<HTMLDivElement>(null)
  // The picked week is what the band opens on, and the weeks are rebuilt around it — so a tap
  // in a swiped-to week re-centres on the week already under the finger and nothing jumps.
  useLayoutEffect(() => {
    if (band.current) band.current.scrollLeft = band.current.clientWidth * BEFORE
  }, [first])

  const fullDate = dateFormat(
    { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' }, locale)
  const shortName = dateFormat({ weekday: 'narrow', timeZone: 'UTC' }, locale)

  // Named, unlike the month picker: seven bare day buttons under a toolbar that says only the
  // date are a group a screen reader has nothing to announce for.
  return (
    <div className="day-strip" role="group" aria-label={t('phone.pickDay')} ref={band}>
      {weeks.map(week => (
        <div key={week[0]} className="day-strip-week" data-week={week[0]}>
          {week.map(day => {
            const classes = ['day-strip-day']
            if (day === today) classes.push('is-today')
            if (day === selected) classes.push('is-selected')
            return (
              <button key={day} type="button" data-day={day} className={classes.join(' ')}
                aria-label={fullDate.format(utcMidnightOf(day))}
                aria-current={day === selected ? 'date' : undefined}
                onClick={() => onPick(day)}>
                <span className="day-strip-name">{shortName.format(utcMidnightOf(day))}</span>
                <span className="day-strip-number">{Number(day.slice(8))}</span>
              </button>
            )
          })}
        </div>
      ))}
    </div>
  )
}
