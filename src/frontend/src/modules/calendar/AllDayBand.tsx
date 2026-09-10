import { useTranslation } from 'react-i18next'
import { useCalendar } from './calendarContext'
import type { Occurrence } from './calendarTypes'
import EventChip from './EventChip'
import type { BandGestures } from './gridGestures'
import type { BandEntry } from './multiDay'
import { colorOf, occurrenceKey } from './occurrenceStyle'
import { daysBetween, type PlainDate } from './plainDate'

export interface AllDayBandProps {
  days: PlainDate[]
  entries: BandEntry[]
  onOpen(o: Occurrence, anchor: HTMLElement): void
  onOpenEditor(o: Occurrence): void
  selectedKey?: string
  hoverKey?: string | null
  onHover?(key: string | null): void
  gestures?: BandGestures
}

const ROW_PX = 22

/** Greedy: a band takes the first row whose last chip ends before it starts, so two bands
    sharing no day share a row rather than each spending one on the band's height. */
function pack(entries: BandEntry[]): { entry: BandEntry; row: number }[] {
  const ends: PlainDate[] = []
  return [...entries]
    .sort((a, b) => a.from.localeCompare(b.from) || b.to.localeCompare(a.to))
    .map(entry => {
      let row = ends.findIndex(end => end < entry.from)
      if (row === -1) row = ends.push(entry.to) - 1
      ends[row] = entry.to
      return { entry, row }
    })
}

/** The strip over the hour grid: everything with no hour of its own, and everything running a
    day or more — a column filled end to end says nothing about when an event is. */
export default function AllDayBand({
  days, entries, onOpen, onOpenEditor, selectedKey, hoverKey, onHover, gestures,
}: AllDayBandProps) {
  const { t } = useTranslation('calendar')
  const { calendarById } = useCalendar()
  const packed = pack(entries)
  const rows = Math.max(1, ...packed.map(one => one.row + 1))

  return (
    <div className="allday-band">
      <div className="allday-label">{t('views.allDay')}</div>
      <div className="allday-days" style={{ height: rows * ROW_PX }}>
        {packed.map(({ entry, row }) => {
          const offset = Math.max(0, daysBetween(days[0], entry.from))
          const span = daysBetween(entry.from, entry.to) + 1
          const key = occurrenceKey(entry.occurrence)
          const drag = gestures?.drag?.key === key ? gestures.drag : null
          return (
            <EventChip key={key} occurrence={entry.occurrence}
              color={colorOf(entry.occurrence, calendarById)} variant="band"
              selected={key === selectedKey} hovered={key === hoverKey} onHover={onHover}
              dragging={drag !== null}
              style={{
                top: row * ROW_PX,
                left: `calc(${offset} * 100% / ${days.length})`,
                width: `calc(${span} * 100% / ${days.length})`,
                // A bandeau is as wide as the days it spans, so one day of travel is that
                // fraction of its own width.
                transform: drag ? `translateX(${drag.deltaDays / span * 100}%)` : undefined,
              }}
              onOpen={onOpen} onOpenEditor={onOpenEditor}
              onPointerDown={gestures?.onChipDown} />
          )
        })}
      </div>
    </div>
  )
}
