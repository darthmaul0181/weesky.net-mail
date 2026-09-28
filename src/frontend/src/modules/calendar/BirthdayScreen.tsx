import { useId, useRef, type CSSProperties, type RefObject } from 'react'
import { useTranslation } from 'react-i18next'
import { useLayer } from '../../hooks/useLayer'
import UserIcon from '../../icons/UserIcon'
import BirthdayLines from './BirthdayLines'
import { useCalendar } from './calendarContext'
import type { Calendar, Occurrence } from './calendarTypes'
import { colorOf } from './occurrenceStyle'
import { whenPartsOf } from './occurrenceWhen'

export interface BirthdayScreenProps {
  occurrence: Occurrence
  calendar: Calendar | null
  /** Where focus goes when the chip that opened the screen is gone. */
  returnFocusRef?: RefObject<HTMLElement | null>
  onClose: () => void
  onOpenContact: () => void
}

/** The phone's reading screen for a birthday, standing where the editor would for any other
 * event: the bubble's content, larger, and the one way on to the card. */
export default function BirthdayScreen({
  occurrence, calendar, returnFocusRef, onClose, onOpenContact,
}: BirthdayScreenProps) {
  const { t } = useTranslation('calendar')
  const { tz, lang, region, cycle, calendarById } = useCalendar()
  const screenRef = useRef<HTMLDivElement>(null)
  const closeRef = useRef<HTMLButtonElement>(null)
  const titleId = useId()

  useLayer({
    active: true, ref: screenRef, onEscape: onClose, initialFocusRef: closeRef, returnFocusRef,
  })

  return (
    <div className="calendar-editor-screen birthday-screen" role="dialog" aria-modal="true"
      aria-labelledby={titleId} tabIndex={-1} ref={screenRef}
      style={{ '--cal': colorOf(occurrence, calendarById) } as CSSProperties}>
      <div className="calendar-editor-head">
        <span className="modal-title" id={titleId}>{t('preview.birthdayTitle')}</span>
        <button type="button" className="modal-close" aria-label={t('preview.close')}
          ref={closeRef} onClick={onClose}>✕</button>
      </div>

      <div className="birthday-screen-body">
        <div className="event-preview-head">
          <span className="event-preview-dot" aria-hidden="true" />
          <span className="event-preview-title">{occurrence.summary || t('views.noTitle')}</span>
        </div>
        <p className="event-preview-when">
          {whenPartsOf(occurrence, { tz, lang, region, cycle }, t).join(' · ')}
        </p>
        <BirthdayLines occurrence={occurrence} calendar={calendar} />

        <button type="button" className="btn btn-primary birthday-screen-open"
          onClick={onOpenContact}>
          <UserIcon size={16} />{t('preview.openCard')}
        </button>
      </div>
    </div>
  )
}
