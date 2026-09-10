import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useCalendar } from './calendarContext'
import { minutesToPx } from './gridGeometry'
import { minutesIntoDay, plainDateOf, type PlainDate } from './plainDate'

export interface NowLineProps {
  days: PlainDate[]
}

const TICK_MS = 60_000

/**
 * The clock's own line across the grid — full strength over today's column, a shadow of itself
 * over the others, and absent from a week today is not in. The day is read off the tick rather
 * than off the layout, so at midnight the line moves to the next column on its own.
 */
export default function NowLine({ days }: NowLineProps) {
  const { t } = useTranslation('calendar')
  const { tz } = useCalendar()
  const [now, setNow] = useState(() => new Date())

  useEffect(() => {
    const id = setInterval(() => setNow(new Date()), TICK_MS)
    return () => clearInterval(id)
  }, [])

  const index = days.indexOf(plainDateOf(now, tz))
  if (index < 0) return null

  return (
    <div className="now-line" role="separator" aria-label={t('views.now')}
      style={{ top: minutesToPx(minutesIntoDay(now, tz)) }}>
      <span className="now-line-today" style={{
        left: `calc(${index} * 100% / ${days.length})`, width: `calc(100% / ${days.length})`,
      }} />
    </div>
  )
}
