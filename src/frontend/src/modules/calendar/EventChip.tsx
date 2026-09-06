import type { CSSProperties, MouseEvent, PointerEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { dateFormat } from '../../lib/intl'
import { useCalendar } from './calendarContext'
import { dateLocaleOf, formatTime } from './calendarLocale'
import type { Occurrence } from './calendarTypes'
import { dayOf, wallClockOf, type WallClock } from './multiDay'
import { occurrenceKey, renderingOf } from './occurrenceStyle'
import { utcMidnightOf, utcOfLocalTime } from './plainDate'

export interface EventChipProps {
  occurrence: Occurrence
  color: string
  variant: 'column' | 'band' | 'month' | 'row'
  style?: CSSProperties
  /** A search result names its own day; an upcoming row sits under a heading that already did. */
  showDate?: boolean
  onOpen(o: Occurrence, anchor: HTMLElement): void
  onOpenEditor(o: Occurrence): void
  selected?: boolean
  /** Lit by the view rather than by `:hover`: the two slices of an evening crossing midnight
      carry one key and light together (decision 3). */
  hovered?: boolean
  onHover?(key: string | null): void
  /** The gesture belongs to the view: a chip only says it is under the pointer, and offers the
      grip a resize is taken by. */
  dragging?: boolean
  onPointerDown?(o: Occurrence, event: PointerEvent): void
  onResizeStart?(o: Occurrence, event: PointerEvent): void
}

/** The heights the second and the third line become legible at, read off the mockup. */
const TIME_MIN_PX = 40
const PLACE_MIN_PX = 58

function heightOf(style: CSSProperties | undefined): number {
  const value = style?.height
  if (typeof value === 'number') return value
  return Number.parseFloat(String(value ?? '')) || 0
}

/**
 * One occurrence drawn, wherever it is drawn. Four variants rather than four components: the
 * rendering rules, the missing title and the key task 6 selects by are the same everywhere, and
 * a second copy of them is a second copy to keep in step.
 */
export default function EventChip({
  occurrence, color, variant, style, showDate, onOpen, onOpenEditor, selected, hovered, onHover,
  dragging, onPointerDown, onResizeStart,
}: EventChipProps) {
  const { t } = useTranslation('calendar')
  const { tz, lang, region, cycle, calendarById } = useCalendar()

  const rendering = renderingOf(occurrence)
  const title = occurrence.summary || t('views.noTitle')

  // An all-day occurrence carries no clock at all; the other two shapes both reduce to the wall
  // clock the grid places them by, so a chip and its block can never name two different hours.
  const clocks = occurrence.isAllDay ? null : wallClockOf(occurrence, tz)
  const at = (clock: WallClock) => formatTime(
    utcOfLocalTime(clock.day, clock.minute, tz), lang, cycle, tz, region)
  // Both ends, never one: a `localStart` with no `localEnd` reads as an empty day and a NaN
  // minute, and `Intl` on the Date that makes throws — which, with no ErrorBoundary anywhere in
  // the app, whitens the whole screen rather than one chip. The chip draws the event without the
  // hour it has not got.
  const readable = (clock: WallClock) => clock.day !== '' && Number.isFinite(clock.minute)
  const times = clocks && readable(clocks[0]) && readable(clocks[1])
    ? ([at(clocks[0]), at(clocks[1])] as const) : null

  const className = [
    variant === 'row' ? 'event-row' : `event-chip is-${variant}`,
    `is-${rendering}`,
    selected ? 'is-selected' : '',
    hovered ? 'is-hovered' : '',
    dragging ? 'is-dragging' : '',
  ].filter(Boolean).join(' ')

  const key = occurrenceKey(occurrence)
  const common = {
    type: 'button' as const,
    className,
    'data-key': key,
    onPointerEnter: onHover && (() => onHover(key)),
    onPointerLeave: onHover && (() => onHover(null)),
    style: { ...style, '--cal': color } as CSSProperties,
    onClick: (event: MouseEvent<HTMLButtonElement>) => onOpen(occurrence, event.currentTarget),
    onDoubleClick: () => onOpenEditor(occurrence),
    onPointerDown: onPointerDown && ((event: PointerEvent) => onPointerDown(occurrence, event)),
  }

  if (variant === 'row') {
    const day = dayOf(occurrence, tz)
    const date = showDate && day
      ? dateFormat({ day: 'numeric', month: 'short', timeZone: 'UTC' },
        dateLocaleOf(lang, region)).format(utcMidnightOf(day))
      : null
    const clock = times ? times[0] : t('views.allDay')
    const sub = occurrence.location || calendarById.get(occurrence.calendarId)?.displayName || ''

    return (
      <button {...common}>
        <span className="event-row-time">
          <span>{date ?? clock}</span>
          <span>{date ? clock : times?.[1] ?? ''}</span>
        </span>
        <span className="event-row-bar" aria-hidden="true" />
        <span className="event-row-body">
          <span className="event-row-title">{title}</span>
          {sub && <span className="event-row-sub">{sub}</span>}
        </span>
      </button>
    )
  }

  if (variant === 'column') {
    const height = heightOf(style)
    return (
      <button {...common}>
        <span className="event-chip-title">{title}</span>
        {height >= TIME_MIN_PX && times && <span className="event-chip-time">{times[0]}</span>}
        {height >= PLACE_MIN_PX && occurrence.location
          && <span className="event-chip-place">{occurrence.location}</span>}
        {/* A span and not a control: a button inside a button is invalid markup, and the grip has
            nothing to announce that the chip around it does not already say. */}
        {onResizeStart && (
          <span className="event-resize-handle" aria-hidden="true"
            onPointerDown={event => {
              event.stopPropagation()
              onResizeStart(occurrence, event)
            }} />
        )}
      </button>
    )
  }

  return (
    <button {...common}>
      {variant === 'month' && <span className="event-dot" aria-hidden="true" />}
      {times && <span className="event-chip-time">{times[0]}</span>}
      <span className="event-chip-title">{title}</span>
    </button>
  )
}
