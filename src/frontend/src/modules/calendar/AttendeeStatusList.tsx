import { useTranslation } from 'react-i18next'
import type { AttendeeProjection } from './calendarTypes'
import { dotClassOf, guestAnswerOf } from './attendeeStatus'

/** The guests and what each answered: one dot, one name, one word. Drawn only where the answers
    are true — the user's own events (décision 12). */
export default function AttendeeStatusList({ guests }: { guests: AttendeeProjection[] }) {
  const { t } = useTranslation('calendar')
  return (
    <ul className="attendee-status">
      {guests.map((guest, index) => (
        <li key={`${index}-${guest.email}`}>
          <span className={`attendee-dot ${dotClassOf(guest.partStat)}`} aria-hidden="true" />
          <span className="attendee-name">{guest.name || guest.email}</span>
          <span className="attendee-answer">{guestAnswerOf(guest.partStat, t)}</span>
        </li>
      ))}
    </ul>
  )
}
