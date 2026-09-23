import { useCalendar } from './calendarContext'
import type { Occurrence } from './calendarTypes'
import EventChip from './EventChip'
import type { GridGestures } from './gridGestures'
import { HOUR_PX, HOURS, minutesToPx } from './gridGeometry'
import { durationMinutesOf, resizePreviewMinutes, type SliceEntry } from './multiDay'
import { colorOf, occurrenceKey } from './occurrenceStyle'
import { layoutColumn } from './overlapLayout'
import type { PlainDate } from './plainDate'

export interface DayColumnProps {
  day: PlainDate
  isToday: boolean
  entries: SliceEntry[]
  onOpen: (o: Occurrence, anchor: HTMLElement) => void
  onOpenEditor: (o: Occurrence) => void
  selectedKey?: string
  hoverKey?: string | null
  onHover?: (key: string | null) => void
  gestures?: GridGestures
}

/** Under this a chip has no room for its own title, and a quarter-hour event would be a rule. */
const MIN_CHIP_PX = 20

/**
 * One day of the hour grid: the cells that draw its rules, then the blocks over them. The column
 * carries its own day on `data-day`, which is what the pointer handlers read to know where a
 * block was dropped without measuring anything.
 */
export default function DayColumn({
  day, isToday, entries, onOpen, onOpenEditor, selectedKey, hoverKey, onHover, gestures,
}: DayColumnProps) {
  const { calendarById, tz } = useCalendar()
  const placed = layoutColumn(entries, entry => entry.slice.startMinute,
    entry => entry.slice.endMinute, HOUR_PX, MIN_CHIP_PX)
  const ghost = gestures?.ghost?.day === day ? gestures.ghost : null

  return (
    <div className={`day-column${isToday ? ' is-today' : ''}`} data-day={day}
      onPointerDown={gestures && (event => gestures.onEmptyDown(day, event))}>
      {HOURS.map(hour => <div key={hour} className="hour-cell" />)}
      {placed.map(({ item, column, columns, top, height }) => {
        const key = occurrenceKey(item.occurrence)
        // An evening running past midnight is two chips in two columns, and the gesture names the
        // occurrence rather than the piece: the move is previewed on the head and the stretch on
        // the tail, so one event is never drawn moving twice.
        const drag = gestures?.drag?.key === key && item.first ? gestures.drag : null
        // Every slice of the resized event previews, not only the one carrying the grip: the
        // grip always sits on the last *visible* slice, which in a day view showing an overnight
        // event's head is that head chip, not the event's real tail.
        const resize = gestures?.resize?.key === key ? gestures.resize : null
        // `resize.durationMinutes` is counted in the occurrence's own zone (`useResizeEvent`'s
        // `durationMinutesOf(o)`); the grid's own zone may read a different length across a DST
        // change, so only the drag's *delta* crosses over, added to the grid-zone duration.
        const previewMinutes = resize
          ? resizePreviewMinutes(item.occurrence, item.slice,
            durationMinutesOf(item.occurrence, tz) + (resize.durationMinutes - resize.baseMinutes),
            tz)
          : null
        // A slice the drag has shrunk past is dropped rather than drawn at a stub: the gesture's
        // listeners are on window/document, so losing the grip's pointer capture is harmless.
        if (resize && previewMinutes === null) return null
        return (
          <EventChip key={`${key}@${item.slice.startMinute}`}
            occurrence={item.occurrence} color={colorOf(item.occurrence, calendarById)}
            variant="column" selected={key === selectedKey} hovered={key === hoverKey}
            onHover={onHover} dragging={drag !== null}
            style={{
              top,
              height: previewMinutes !== null
                ? Math.max(MIN_CHIP_PX, minutesToPx(previewMinutes)) : height,
              left: `calc(${column} * 100% / ${columns})`, width: `calc(100% / ${columns})`,
              // A chip is a fraction of its column wide, so one column of travel is that many
              // times its own width: the block follows the pointer into the next day without
              // anything having to measure the grid.
              transform: drag
                ? `translate(${drag.deltaDays * columns * 100}%, ${minutesToPx(drag.deltaMinutes)}px)`
                : undefined,
            }}
            onOpen={onOpen} onOpenEditor={onOpenEditor}
            onPointerDown={gestures?.onChipDown}
            onResizeStart={item.last ? gestures?.onResizeDown : undefined} />
        )
      })}
      {ghost && (
        <div className="event-ghost" style={{
          top: minutesToPx(ghost.startMinute),
          height: minutesToPx(ghost.endMinute - ghost.startMinute),
        }} />
      )}
    </div>
  )
}
