import {
  useCallback, useEffect, useLayoutEffect, useRef, type CSSProperties, type RefObject,
} from 'react'
import { useTranslation } from 'react-i18next'
import BellIcon from '../../icons/BellIcon'
import CalendarIcon from '../../icons/CalendarIcon'
import MapPinIcon from '../../icons/MapPinIcon'
import PencilIcon from '../../icons/PencilIcon.jsx'
import PeopleIcon from '../../icons/PeopleIcon'
import RepeatIcon from '../../icons/RepeatIcon'
import TrashIcon from '../../icons/TrashIcon.jsx'
import UserIcon from '../../icons/UserIcon'
import { useCalendar } from './calendarContext'
import AttendeeStatusList from './AttendeeStatusList'
import type { Calendar, Occurrence } from './calendarTypes'
import { colorOf } from './occurrenceStyle'
import { whenPartsOf } from './occurrenceWhen'
import { myAnswerOf } from './myAnswer'
import { useEvent } from './queries'
import { recurrenceSummary } from './recurrenceSummary'
import { usePopoverPosition } from './usePopoverPosition'
import { returnFocus, useDismiss } from '../../hooks/useDismiss'
import { reachable } from '../../hooks/useLayer'
import { tabbablesIn } from '../../lib/layerStack'

export interface EventPreviewProps {
  occurrence: Occurrence
  /** The calendar the occurrence names, when the list already holds it: an occurrence whose
      calendar has not arrived is drawn rather than withheld, exactly as the grid draws it. */
  calendar: Calendar | null
  anchor: HTMLElement
  /** Read when the chip was clicked, not when the bubble mounts: a search result clears the
      results it was clicked in, so the chip has left the screen by then. */
  rect: DOMRect
  /** Where focus goes when the chip is gone — detached with the results it was clicked in, or
      deleted with the event itself. `CalendarLayout` hands its `.calendar-main`. */
  returnFocusRef?: RefObject<HTMLElement | null>
  onClose(): void
  onEdit(): void
  onDelete(): void
}

/**
 * The bubble a click on a chip opens: what the occurrence knows, and the two things to do about
 * it. It never queries — the minutes of a reminder and the attendees live in the detail, which is
 * the editor's business, so the bell here says only that one is set.
 */
export default function EventPreview({
  occurrence, calendar, anchor, rect, returnFocusRef, onClose, onEdit, onDelete,
}: EventPreviewProps) {
  const { t } = useTranslation('calendar')
  const { tz, lang, region, cycle, calendarById } = useCalendar()
  const { ref, left, top } = usePopoverPosition(rect)
  const bubble = useRef<HTMLDivElement | null>(null)
  // The chip the bubble hangs off: where the focus goes back when it closes from the inside.
  const anchorRef = useRef(anchor)
  useEffect(() => { anchorRef.current = anchor })
  // Memoised: a fresh ref callback is detached and re-attached on every commit, which would drive
  // the position hook's node null and back twice per render.
  const holdBubble = useCallback((node: HTMLDivElement | null) => {
    bubble.current = node
    ref(node)
  }, [ref])

  // Non-modal: no trap, and Tab walks on into the grid. A dialog the bubble launched sits above
  // it on the stack, so neither that Escape nor a press inside it reaches here.
  useDismiss({
    open: true,
    rootRef: bubble,
    onDismiss: onClose,
    anchorRef,
    refocusRef: anchorRef,
    closeOnScroll: true,
  })

  // Opened by a click, so nothing has moved the focus onto it: a keyboard reaches its two
  // actions only if the opening does.
  useLayoutEffect(() => { if (bubble.current) tabbablesIn(bubble.current)[0]?.focus() }, [])

  // Where every closing route meets, whichever of them moved the focus first. A layout cleanup,
  // because a passive one runs after the bubble's nodes have left the document and could no longer
  // tell whether it was holding the focus at all.
  useLayoutEffect(() => () => {
    const chip = anchorRef.current
    returnFocus(bubble.current, reachable(chip) ? chip : returnFocusRef?.current)
  }, [returnFocusRef])

  // Always fetched, one request per opening (`ContactCard`'s `useContact` pattern): the bubble
  // carries neither a repeating event's rule nor its participants, and the detail holds both.
  const { data: detail } = useEvent(occurrence.eventId)
  const rule = detail?.repeatIsExact ? detail.fields.repeat : undefined
  // The raw RRULE must never reach the screen: loading, a failed fetch and a rule too rich for
  // the picker (repeatIsExact false) all fall back to the same generic label as a save-in-flight.
  const recurrenceLabel = rule ? recurrenceSummary(rule, t, lang, region) : t('preview.repeatsGeneric')

  const line = whenPartsOf(occurrence, { tz, lang, region, cycle }, t).join(' · ')

  const title = occurrence.summary || t('views.noTitle')
  const color = colorOf(occurrence, calendarById)

  // Décision 7: names only on a received event. The PARTSTAT it carries is the organizer's snapshot
  // at send time, almost always empty or stale here — the user's own answer lives in the mail's
  // card. On the user's own event the answers arrive here, so each guest wears one (décision 12).
  const master = (detail?.attendees ?? []).filter(a => !a.recurrenceId)
  const organizer = detail?.canInvite ? undefined : master.find(a => a.isOrganizer)
  const guests = master.filter(a => !a.isOrganizer)
  // The one state that is true here: the user's own, read off the occurrence the server stamped.
  const myAnswer = myAnswerOf(occurrence.myPartStat, t)

  return (
    <div className="event-preview" role="dialog" aria-label={title} ref={holdBubble}
      style={{ left, top, '--cal': color } as CSSProperties}>
      <div className="event-preview-head">
        <span className="event-preview-dot" aria-hidden="true" />
        <span className="event-preview-title">{title}</span>
        <button type="button" className="modal-close" aria-label={t('preview.close')}
          onClick={() => { returnFocus(bubble.current, anchorRef.current); onClose() }}>✕</button>
      </div>

      <p className="event-preview-when">{line}</p>

      {occurrence.location && (
        <p className="event-preview-row">
          <MapPinIcon size={14} />{occurrence.location}
        </p>
      )}
      {occurrence.hasAlarm && (
        <p className="event-preview-row">
          <BellIcon size={14} />{t('preview.reminderSet')}
        </p>
      )}
      {occurrence.recurrenceText && (
        <p className="event-preview-row">
          <RepeatIcon size={14} />{recurrenceLabel}
        </p>
      )}
      {calendar && (
        <p className="event-preview-row">
          <CalendarIcon size={14} />{calendar.displayName}
        </p>
      )}
      {organizer && (
        <p className="event-preview-row">
          <UserIcon size={14} />{t('preview.organizedBy', { name: organizer.name || organizer.email })}
        </p>
      )}
      {guests.length > 0 && (detail?.canInvite
        ? <AttendeeStatusList guests={guests} />
        : (
          <p className="event-preview-row event-preview-attendees">
            <PeopleIcon size={14} />{guests.map(a => a.name || a.email).join(', ')}
          </p>
        ))}
      {myAnswer && (
        <p className="event-preview-row event-preview-answer">
          <UserIcon size={14} />{myAnswer}
        </p>
      )}

      <div className="event-preview-actions">
        <button type="button" className="btn btn-primary" onClick={onEdit}>
          <PencilIcon size={14} />{t('preview.edit')}
        </button>
        <button type="button" className="btn btn-ghost" onClick={onDelete}>
          <TrashIcon size={14} />{t('preview.delete')}
        </button>
      </div>
    </div>
  )
}
