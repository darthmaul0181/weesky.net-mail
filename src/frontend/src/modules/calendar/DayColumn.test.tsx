import { screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import DayColumn from './DayColumn'
import { occurrenceOf, renderInCalendar } from './calendarTestHarness'
import { minutesToPx } from './gridGeometry'
import type { SliceEntry } from './multiDay'
import { occurrenceKey } from './occurrenceStyle'

const noop = () => {}

// September 2026 is CEST (UTC+2): 20:00Z is 22:00 local, and 00:00Z the next day is 02:00 local.
const OVERNIGHT = occurrenceOf({
  eventId: 'ov1', summary: 'Overnight',
  startUtc: '2026-09-16T20:00:00Z', endUtc: '2026-09-17T00:00:00Z',
})

const SAME_DAY = occurrenceOf({
  eventId: 'sd1', summary: 'Same day',
  startUtc: '2026-09-16T07:00:00Z', endUtc: '2026-09-16T08:00:00Z',
})

// Brussels clocks go back an hour at 2026-10-25T01:00:00Z (CEST -> CET): stored in UTC this
// event is 270 min, but its Brussels wall clock (00:30 -> 04:00) is only 210.
const DST_CROSSING = occurrenceOf({
  eventId: 'dst1', summary: 'Crossing DST', timeZone: 'UTC',
  startUtc: '2026-10-24T22:30:00Z', endUtc: '2026-10-25T03:00:00Z',
})

describe('DayColumn resize preview', () => {
  it("sizes an overnight tail by its own slice, not the whole event's new duration", () => {
    const key = occurrenceKey(OVERNIGHT)
    const tail: SliceEntry = {
      occurrence: OVERNIGHT,
      slice: { day: '2026-09-17', startMinute: 0, endMinute: 120 },
      first: false, last: true,
    }
    renderInCalendar(
      <DayColumn day="2026-09-17" isToday={false} entries={[tail]}
        onOpen={noop} onOpenEditor={noop}
        gestures={{
          drag: null, resize: { key, durationMinutes: 255, baseMinutes: 240 }, ghost: null,
          onChipDown: noop, onResizeDown: noop, onEmptyDown: noop,
        }} />,
    )

    // The tail's own slice is 120 min; the whole event grew from 240 to 255, so the tail grows
    // by the same 15 min rather than being redrawn at the full 255.
    expect(screen.getByText('Overnight').closest('button')).toHaveStyle({
      height: `${minutesToPx(135)}px`,
    })
  })

  it('keeps minutesToPx(durationMinutes) for a same-day resize', () => {
    const key = occurrenceKey(SAME_DAY)
    const entry: SliceEntry = {
      occurrence: SAME_DAY,
      slice: { day: '2026-09-16', startMinute: 540, endMinute: 600 },
      first: true, last: true,
    }
    renderInCalendar(
      <DayColumn day="2026-09-16" isToday={false} entries={[entry]}
        onOpen={noop} onOpenEditor={noop}
        gestures={{
          drag: null, resize: { key, durationMinutes: 90, baseMinutes: 60 }, ghost: null,
          onChipDown: noop, onResizeDown: noop, onEmptyDown: noop,
        }} />,
    )

    expect(screen.getByText('Same day').closest('button')).toHaveStyle({
      height: `${minutesToPx(90)}px`,
    })
  })

  it('shrinks the head across the true midnight and drops the now-empty tail', () => {
    const key = occurrenceKey(OVERNIGHT)
    const head: SliceEntry = {
      occurrence: OVERNIGHT,
      slice: { day: '2026-09-16', startMinute: 1320, endMinute: 1440 },
      first: true, last: false,
    }
    const tail: SliceEntry = {
      occurrence: OVERNIGHT,
      slice: { day: '2026-09-17', startMinute: 0, endMinute: 120 },
      first: false, last: true,
    }
    const gestures = {
      drag: null, resize: { key, durationMinutes: 90, baseMinutes: 240 }, ghost: null,
      onChipDown: noop, onResizeDown: noop, onEmptyDown: noop,
    }
    renderInCalendar(
      <>
        <DayColumn day="2026-09-16" isToday={false} entries={[head]}
          onOpen={noop} onOpenEditor={noop} gestures={gestures} />
        <DayColumn day="2026-09-17" isToday={false} entries={[tail]}
          onOpen={noop} onOpenEditor={noop} gestures={gestures} />
      </>,
    )

    // 240 min shrunk to 90: the head's own offset is 0, so it previews the full 90; the tail's
    // offset is 120 (it begins 120 min into the event), past the new 90-min end, so it has
    // nothing left and is not drawn at all.
    const chips = screen.getAllByText('Overnight')
    expect(chips).toHaveLength(1)
    expect(chips[0]!.closest('button')).toHaveStyle({ height: `${minutesToPx(90)}px` })
  })

  it('caps a lone visible head at its own midnight when the drag grows the event', () => {
    const key = occurrenceKey(OVERNIGHT)
    const head: SliceEntry = {
      occurrence: OVERNIGHT,
      slice: { day: '2026-09-16', startMinute: 1320, endMinute: 1440 },
      // The real tail lives on 2026-09-17, off screen in a day view: this slice is both the
      // first and the last *visible* one, but it is not the event's true last day.
      first: true, last: true,
    }
    renderInCalendar(
      <DayColumn day="2026-09-16" isToday={false} entries={[head]}
        onOpen={noop} onOpenEditor={noop}
        gestures={{
          drag: null, resize: { key, durationMinutes: 255, baseMinutes: 240 }, ghost: null,
          onChipDown: noop, onResizeDown: noop, onEmptyDown: noop,
        }} />,
    )

    // The drag grew the event to 255 min, but the head's own day only ever held 120 (22:00 to
    // midnight): the real growth belongs to the tail, off screen, so the head stays capped.
    expect(screen.getByText('Overnight').closest('button')).toHaveStyle({
      height: `${minutesToPx(120)}px`,
    })
  })

  it('leaves a lone visible head unchanged when the drag shrinks the event', () => {
    const key = occurrenceKey(OVERNIGHT)
    const head: SliceEntry = {
      occurrence: OVERNIGHT,
      slice: { day: '2026-09-16', startMinute: 1320, endMinute: 1440 },
      first: true, last: true,
    }
    renderInCalendar(
      <DayColumn day="2026-09-16" isToday={false} entries={[head]}
        onOpen={noop} onOpenEditor={noop}
        gestures={{
          drag: null, resize: { key, durationMinutes: 180, baseMinutes: 240 }, ghost: null,
          onChipDown: noop, onResizeDown: noop, onEmptyDown: noop,
        }} />,
    )

    // The event shrank from 240 to 180, still more than the head's own 120 min: the real result
    // is 22:00 -> 01:00, and the head — which never held more than its own midnight — is capped
    // at 120 exactly as it was before the drag.
    expect(screen.getByText('Overnight').closest('button')).toHaveStyle({
      height: `${minutesToPx(120)}px`,
    })
  })

  it('previews a resize in the grid zone even when the event is stored in another one', () => {
    const key = occurrenceKey(DST_CROSSING)
    const entry: SliceEntry = {
      occurrence: DST_CROSSING,
      slice: { day: '2026-10-25', startMinute: 30, endMinute: 240 },
      first: true, last: true,
    }
    renderInCalendar(
      <DayColumn day="2026-10-25" isToday={false} entries={[entry]}
        onOpen={noop} onOpenEditor={noop}
        gestures={{
          drag: null, resize: { key, durationMinutes: 285, baseMinutes: 270 }, ghost: null,
          onChipDown: noop, onResizeDown: noop, onEmptyDown: noop,
        }} />,
    )

    // Stored in UTC the event is 270 min; Brussels reads 210 across this DST change. The drag
    // added 15 min in UTC (270 -> 285): the preview adds that same 15 to the grid's own 210
    // (-> 225), not to the UTC number, or the chip would jump by the 60-min zone gap at the
    // very first pointer move.
    expect(screen.getByText('Crossing DST').closest('button')).toHaveStyle({
      height: `${minutesToPx(225)}px`,
    })
  })
})
