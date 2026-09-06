import { useEffect, type CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import BellIcon from '../../icons/BellIcon'
import CalendarIcon from '../../icons/CalendarIcon'
import MapPinIcon from '../../icons/MapPinIcon'
import PencilIcon from '../../icons/PencilIcon.jsx'
import RepeatIcon from '../../icons/RepeatIcon'
import TrashIcon from '../../icons/TrashIcon.jsx'
import { useCalendar } from './calendarContext'
import { dateLocaleOf, formatLongDay, formatLongDayRange, formatTime } from './calendarLocale'
import type { Calendar, Occurrence } from './calendarTypes'
import { wallClockOf } from './multiDay'
import { colorOf } from './occurrenceStyle'
import { addDays, utcOfLocalTime, type PlainDate } from './plainDate'
import { usePopoverPosition } from './usePopoverPosition'

export interface EventPreviewProps {
  occurrence: Occurrence
  /** The calendar the occurrence names, when the list already holds it: an occurrence whose
      calendar has not arrived is drawn rather than withheld, exactly as the grid draws it. */
  calendar: Calendar | null
  anchor: HTMLElement
  /** Read when the chip was clicked, not when the bubble mounts: a search result clears the
      results it was clicked in, so the chip has left the screen by then. */
  rect: DOMRect
  onClose(): void
  onEdit(): void
  onDelete(): void
}

/** The two ends an occurrence spans, whichever shape its time came in — `null` when the server
    sent none of the three, which the grid already draws rather than throwing over. */
function daysOf(o: Occurrence, tz: string): [PlainDate, PlainDate] | null {
  if (o.isAllDay) {
    const from = o.startDate
    if (!from) return null
    return [from, addDays(o.endDateExclusive ?? addDays(from, 1), -1)]
  }
  const [start, end] = wallClockOf(o, tz)
  if (start.day === '') return null
  // A start the server sent and an end it did not is still a day worth naming.
  if (end.day === '') return [start.day, start.day]
  // An event closing exactly at midnight belongs to the evening it started in.
  return [start.day, end.minute === 0 ? addDays(end.day, -1) : end.day]
}

/**
 * The bubble a click on a chip opens: what the occurrence knows, and the two things to do about
 * it. It never queries — the minutes of a reminder and the attendees live in the detail, which is
 * the editor's business, so the bell here says only that one is set.
 */
export default function EventPreview({
  occurrence, calendar, anchor, rect, onClose, onEdit, onDelete,
}: EventPreviewProps) {
  const { t } = useTranslation('calendar')
  const { tz, lang, region, cycle, calendarById } = useCalendar()
  const { ref, left, top } = usePopoverPosition(rect)

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') onClose() }
    const outside = (event: MouseEvent) => {
      const target = event.target as Node
      if (!anchor.contains(target) && !(target as Element).closest?.('.event-preview')) onClose()
    }
    document.addEventListener('keydown', onKey)
    document.addEventListener('mousedown', outside)
    // Capture, so any scroller carries it — the week body, the month stage, the upcoming list.
    document.addEventListener('scroll', onClose, true)
    return () => {
      document.removeEventListener('keydown', onKey)
      document.removeEventListener('mousedown', outside)
      document.removeEventListener('scroll', onClose, true)
    }
  }, [anchor, onClose])

  const locale = dateLocaleOf(lang, region)
  const span = daysOf(occurrence, tz)
  const when = !span ? null : span[0] === span[1]
    ? formatLongDay(span[0], locale)
    : formatLongDayRange(span[0], span[1], locale)

  const clocks = occurrence.isAllDay ? null : wallClockOf(occurrence, tz)
  const at = (clock: { day: PlainDate; minute: number }) => formatTime(
    utcOfLocalTime(clock.day, clock.minute, tz),
    lang, cycle, tz, region)
  const readable = clocks?.every(clock => clock.day !== '' && Number.isFinite(clock.minute))
  // Three lines, not two: a whole day of several days is its range and nothing else, a single one
  // says so, and a dated event whose clocks the server did not send says neither — announcing
  // "All day" for an event that has an hour is worse than saying nothing about it.
  const hours = !occurrence.isAllDay && clocks && readable
    ? `${at(clocks[0])} – ${at(clocks[1])}` : null
  const allDay = occurrence.isAllDay && (!span || span[0] === span[1]) ? t('preview.allDay') : null
  const line = [when, allDay ?? hours].filter(Boolean).join(' · ')

  const title = occurrence.summary || t('views.noTitle')
  const color = colorOf(occurrence, calendarById)

  return (
    <div className="event-preview" role="dialog" aria-label={title} ref={ref}
      style={{ left, top, '--cal': color } as CSSProperties}>
      <div className="event-preview-head">
        <span className="event-preview-dot" aria-hidden="true" />
        <span className="event-preview-title">{title}</span>
        <button type="button" className="modal-close" aria-label={t('preview.close')}
          onClick={onClose}>✕</button>
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
          <RepeatIcon size={14} />{occurrence.recurrenceText}
        </p>
      )}
      {calendar && (
        <p className="event-preview-row">
          <CalendarIcon size={14} />{calendar.displayName}
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
