import { useTranslation } from 'react-i18next'
import BellIcon from '../../icons/BellIcon'
import CakeIcon from '../../icons/CakeIcon'
import CalendarIcon from '../../icons/CalendarIcon'
import RepeatIcon from '../../icons/RepeatIcon'
import { ageOf, reminderLine } from './birthday'
import type { Calendar, Occurrence } from './calendarTypes'

export interface BirthdayLinesProps {
  occurrence: Occurrence
  calendar: Calendar | null
}

/** What a birthday says under its date, in the bubble and on the phone's reading screen alike:
 * the age first, since it is what one comes to read, then the real reminder, the rule, the calendar. */
export default function BirthdayLines({ occurrence, calendar }: BirthdayLinesProps) {
  const { t } = useTranslation('calendar')
  const age = ageOf(occurrence)
  const reminder = calendar?.birthdayReminder

  return (
    <>
      {age !== null && (
        <p className="event-preview-row is-strong">
          <CakeIcon size={14} />{t('preview.age', { count: age })}
        </p>
      )}
      {reminder && reminder !== 'none' && (
        <p className="event-preview-row">
          <BellIcon size={14} />{reminderLine(reminder, t)}
        </p>
      )}
      <p className="event-preview-row">
        <RepeatIcon size={14} />{t('preview.yearly')}
      </p>
      {calendar && (
        <p className="event-preview-row">
          <CalendarIcon size={14} />{calendar.displayName}
        </p>
      )}
    </>
  )
}
