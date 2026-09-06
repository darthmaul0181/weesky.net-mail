import { renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { TZ } from './calendarTestHarness'
import {
  fireEscape, firePointer, installPointerEvents, pointerDownOn,
} from '../../test-utils'
import { useCreateByDrag } from './useCreateByDrag'

const WEEK = ['2026-09-14', '2026-09-15', '2026-09-16', '2026-09-17', '2026-09-18',
  '2026-09-19', '2026-09-20']
const DAY = '2026-09-16'

/** September is CEST, so a Brussels 09:00 is 07:00Z — stated rather than derived from the
    machine's own zone, which is what a runner in UTC would otherwise answer. */
const NINE = '2026-09-16T07:00:00.000Z'
const TEN = '2026-09-16T08:00:00.000Z'
const HALF_PAST_TEN = '2026-09-16T08:30:00.000Z'

beforeEach(installPointerEvents)
afterEach(() => { document.body.innerHTML = '' })

function column() {
  const node = document.createElement('div')
  node.className = 'day-column'
  node.dataset.day = DAY
  node.getBoundingClientRect = () => ({
    left: 200, width: 100, top: 0, height: 1344, right: 300, bottom: 1344, x: 200, y: 0,
    toJSON: () => ({}),
  } as DOMRect)
  document.body.append(node)
  return node
}

function creating(onCreate = vi.fn(), enabled = true) {
  const { result } = renderHook(() => useCreateByDrag({ enabled, days: WEEK, tz: TZ, onCreate }))
  return { result, onCreate }
}

const instants = (onCreate: ReturnType<typeof vi.fn>) =>
  (onCreate.mock.calls[0] as Date[]).map(one => one.toISOString())

describe('useCreateByDrag', () => {
  // 520px is 09:17 of grid; the slot a click opens is the half hour it fell in, plus an hour.
  it('opens the clicked half hour plus an hour on a plain click', () => {
    const node = column()
    const { result, onCreate } = creating()
    result.current.onPointerDown(DAY, pointerDownOn(node, 250, 520))
    firePointer('pointerup', 250, 520)
    expect(instants(onCreate)).toEqual([NINE, TEN])
  })

  it('traces the range a drag draws', () => {
    const node = column()
    const { result, onCreate } = creating()
    result.current.onPointerDown(DAY, pointerDownOn(node, 250, 504))
    firePointer('pointermove', 250, 588)
    expect(result.current.ghost).toEqual({ day: DAY, startMinute: 540, endMinute: 630 })
    firePointer('pointerup', 250, 588)
    expect(instants(onCreate)).toEqual([NINE, HALF_PAST_TEN])
    expect(result.current.ghost).toBeNull()
  })

  it('reads a drag upwards the same way round', () => {
    const node = column()
    const { result, onCreate } = creating()
    result.current.onPointerDown(DAY, pointerDownOn(node, 250, 588))
    firePointer('pointermove', 250, 504)
    expect(result.current.ghost).toEqual({ day: DAY, startMinute: 540, endMinute: 630 })
    firePointer('pointerup', 250, 504)
    expect(instants(onCreate)).toEqual([NINE, HALF_PAST_TEN])
  })

  it('abandons the gesture on Escape', () => {
    const node = column()
    const { result, onCreate } = creating()
    result.current.onPointerDown(DAY, pointerDownOn(node, 250, 504))
    firePointer('pointermove', 250, 588)
    fireEscape()
    expect(result.current.ghost).toBeNull()
    firePointer('pointerup', 250, 588)
    expect(onCreate).not.toHaveBeenCalled()
  })

  // The chips are children of the column their block sits in: without this, grabbing one would
  // draw a ghost underneath it and open an editor on the drop.
  it('ignores a press that landed on a chip', () => {
    const node = column()
    const chip = document.createElement('button')
    chip.className = 'event-chip'
    node.append(chip)
    const { result, onCreate } = creating()
    result.current.onPointerDown(DAY, pointerDownOn(node, 250, 504, chip))
    firePointer('pointerup', 250, 504)
    expect(onCreate).not.toHaveBeenCalled()
  })

  it('is inert when gestures are off', () => {
    const node = column()
    const { result, onCreate } = creating(vi.fn(), false)
    result.current.onPointerDown(DAY, pointerDownOn(node, 250, 504))
    firePointer('pointermove', 250, 588)
    firePointer('pointerup', 250, 588)
    expect(result.current.ghost).toBeNull()
    expect(onCreate).not.toHaveBeenCalled()
  })
})
