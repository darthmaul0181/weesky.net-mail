import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { dateFormat } from '../../lib/intl'
import AllDayBand from './AllDayBand'
import { useCalendar } from './calendarContext'
import { dateLocaleOf, formatTime, weekNumberOf } from './calendarLocale'
import type { Occurrence } from './calendarTypes'
import DayColumn from './DayColumn'
import { FIRST_VISIBLE_HOUR, HOURS, minutesToPx } from './gridGeometry'
import type { GridGestures } from './gridGestures'
import { placeAll } from './multiDay'
import NowLine from './NowLine'
import { utcMidnightOf, type PlainDate } from './plainDate'
import { useCreateByDrag } from './useCreateByDrag'
import { useDragEvent } from './useDragEvent'
import { useResizeEvent } from './useResizeEvent'

export interface WeekViewProps {
  /** One day is the day view: the same grid, one column wide, rather than a second component. */
  days: PlainDate[]
  onOpen(o: Occurrence, anchor: HTMLElement): void
  onOpenEditor(o: Occurrence): void
  /** Off below 640px: a 360px column has no room to drag a block through. */
  gestures: boolean
  selectedKey?: string
  /** A bubble standing over the grid swallows the click that dismisses it, the way Google does:
      that click closes the bubble and creates nothing. */
  previewOpen?: boolean
}

/** A reference day with no transition on it: the gutter names hours, not instants, and reading
    them in the user's own zone would print 01:00 twice on the night the clocks go back. */
const HOUR_REFERENCE = Date.UTC(2026, 0, 1)

export default function WeekView({
  days, onOpen, onOpenEditor, gestures: enabled, selectedKey, previewOpen,
}: WeekViewProps) {
  const { t } = useTranslation('calendar')
  const {
    tz, rules, lang, region, cycle, today, visible,
    createAt, startGesture, moveOccurrence, resizeOccurrence,
  } = useCalendar()

  const drag = useDragEvent({ enabled, days, onDrop: moveOccurrence })
  const resize = useResizeEvent({ enabled, onResize: resizeOccurrence })
  const create = useCreateByDrag({
    enabled, days, tz, previewOpen, onCreate: (start, end) => createAt(start, end, false),
  })
  // Held by the grid and not by each chip: an evening cut at midnight is two chips of one key,
  // and `:hover` only ever knows the one under the pointer.
  const [hoverKey, setHoverKey] = useState<string | null>(null)
  const gesturing = drag.drag !== null || resize.resize !== null || create.ghost !== null
  const gestures: GridGestures = {
    drag: drag.drag, resize: resize.resize, ghost: create.ghost,
    onChipDown: drag.onPointerDown, onResizeDown: resize.onPointerDown,
    onEmptyDown: create.onPointerDown,
  }

  // The bubble is anchored to a chip that is about to move, and it hangs over the grid the
  // gesture is being drawn on: it closes the moment one begins.
  useEffect(() => { if (gesturing) startGesture() }, [gesturing, startGesture])

  const locale = dateLocaleOf(lang, region)
  // The gutter is a token rather than a number here: the phone narrows it, and a width written
  // in JS could not be narrowed by a media query at all.
  const template = `var(--cal-gutter) repeat(${days.length}, minmax(0, 1fr))`
  const placements = useMemo(() => placeAll(visible, tz, days), [visible, tz, days])

  const body = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (body.current) body.current.scrollTop = minutesToPx(FIRST_VISIBLE_HOUR * 60)
  }, [])

  const nameOf = (day: PlainDate) => dateFormat({ weekday: 'short', timeZone: 'UTC' }, locale)
    .format(utcMidnightOf(day))
  const hourLabel = (hour: number) => formatTime(
    new Date(HOUR_REFERENCE + hour * 3_600_000), lang, cycle, 'UTC', region)

  return (
    <div className={`week-view${gesturing ? ' is-gesturing' : ''}`}>
      <div className="week-head" style={{ gridTemplateColumns: template }}>
        <div className="week-head-gutter">
          {t('views.weekShort', { number: weekNumberOf(days[0], rules) })}
        </div>
        {days.map(day => (
          <div key={day} className={`week-day-head${day === today ? ' is-today' : ''}`}>
            <span className="week-day-name">{nameOf(day)}</span>
            <span className="week-day-number">{Number(day.slice(8))}</span>
          </div>
        ))}
      </div>

      <AllDayBand days={days} entries={placements.bands} selectedKey={selectedKey}
        hoverKey={hoverKey} onHover={setHoverKey}
        onOpen={onOpen} onOpenEditor={onOpenEditor}
        gestures={enabled ? gestures : undefined} />

      <div className="week-body" ref={body} style={{ gridTemplateColumns: template }}>
        {/* Midnight carries no label, the way Google Calendar draws it: the -8px lift that puts
            every other label astride its own rule falls above `scrollTop: 0` for the first hour,
            and a scroll container cannot be scrolled above its own start. */}
        <div className="week-hours">
          {HOURS.map(hour => (
            <div key={hour} className="week-hour">
              {hour > 0 && <span>{hourLabel(hour)}</span>}
            </div>
          ))}
        </div>
        {days.map(day => (
          <DayColumn key={day} day={day} isToday={day === today}
            entries={placements.slices.get(day) ?? []} selectedKey={selectedKey}
            hoverKey={hoverKey} onHover={setHoverKey}
            onOpen={onOpen} onOpenEditor={onOpenEditor}
            gestures={enabled ? gestures : undefined} />
        ))}
        <NowLine days={days} />
      </div>
    </div>
  )
}
