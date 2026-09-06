import { useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { useCalendar } from './calendarContext'
import { dateLocaleOf, formatLongDay } from './calendarLocale'
import type { Occurrence } from './calendarTypes'
import EventChip from './EventChip'
import { itemsByDay, placeAll } from './multiDay'
import { colorOf, occurrenceKey } from './occurrenceStyle'
import { addDays, type PlainDate } from './plainDate'

export interface UpcomingListProps {
  days: PlainDate[]
  onOpen(o: Occurrence, anchor: HTMLElement): void
  onOpenEditor(o: Occurrence): void
  selectedKey?: string
  /** What an empty stretch says. The month ahead by default; the phone's month hands its own
      sentence in, since one day holding nothing is not the same news. */
  empty?: string
}

/** The month ahead, one heading per day that holds something. A day with nothing is not drawn:
    thirty empty headings would bury the four days that matter. */
export default function UpcomingList({
  days, onOpen, onOpenEditor, selectedKey, empty,
}: UpcomingListProps) {
  const { t } = useTranslation('calendar')
  const { tz, lang, region, today, visible, calendarById } = useCalendar()

  const byDay = useMemo(
    () => itemsByDay(placeAll(visible, tz, days), days), [visible, tz, days])
  const groups = days
    .map(day => ({ day, items: byDay.get(day) ?? [] }))
    .filter(group => group.items.length > 0)

  if (groups.length === 0) {
    return <p className="calendar-empty">{empty ?? t('views.emptyUpcoming')}</p>
  }

  const locale = dateLocaleOf(lang, region)
  const headingOf = (day: PlainDate) => {
    const date = formatLongDay(day, locale)
    if (day === today) return `${t('views.today')} · ${date}`
    if (day === addDays(today, 1)) return `${t('views.tomorrow')} · ${date}`
    return date
  }

  return (
    <div className="upcoming-list">
      {groups.map(({ day, items }) => (
        <section key={day} className="upcoming-group">
          <h3 className="upcoming-day">{headingOf(day)}</h3>
          {items.map(({ occurrence }) => (
            <EventChip key={occurrenceKey(occurrence)} occurrence={occurrence}
              color={colorOf(occurrence, calendarById)} variant="row"
              selected={occurrenceKey(occurrence) === selectedKey}
              onOpen={onOpen} onOpenEditor={onOpenEditor} />
          ))}
        </section>
      ))}
    </div>
  )
}
