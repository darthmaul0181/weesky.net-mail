import { renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { occurrenceOf } from './calendarTestHarness'
import {
  fireEscape, firePointer, installPointerEvents, pointerDownOn,
} from '../../test-utils'
import { useDragEvent } from './useDragEvent'

const WEEK = ['2026-09-14', '2026-09-15', '2026-09-16', '2026-09-17', '2026-09-18',
  '2026-09-19', '2026-09-20']

const DENTIST = occurrenceOf({
  eventId: 'a1', summary: 'Dentist',
  startUtc: '2026-09-16T07:00:00Z', endUtc: '2026-09-16T08:00:00Z',
})

beforeEach(installPointerEvents)
afterEach(() => { document.body.innerHTML = '' })

/** A column 100px wide whose day is the third of the week, so the days area opens at x=0 and
    each column is a round hundred: a pointer at 250 is over Wednesday, at 350 over Thursday. */
function chipIn(kind: 'column' | 'band') {
  const body = document.createElement('div')
  body.className = 'week-body'
  const holder = document.createElement('div')
  if (kind === 'column') {
    holder.className = 'day-column'
    holder.dataset.day = '2026-09-16'
    holder.getBoundingClientRect = () => rect(200, 100)
  } else {
    holder.className = 'allday-days'
    holder.getBoundingClientRect = () => rect(0, 700)
  }
  const chip = document.createElement('button')
  chip.className = 'event-chip'
  holder.append(chip)
  body.append(holder)
  document.body.append(body)
  return chip
}

function rect(left: number, width: number): DOMRect {
  return {
    left, width, top: 0, height: 1344, right: left + width, bottom: 1344, x: left, y: 0,
    toJSON: () => ({}),
  } as DOMRect
}

function dragging(onDrop = vi.fn(), enabled = true) {
  const { result } = renderHook(() => useDragEvent({ enabled, days: WEEK, onDrop }))
  return { result, onDrop }
}

describe('useDragEvent', () => {
  it('leaves a click a click: three pixels are not a drag', () => {
    const chip = chipIn('column')
    const { result, onDrop } = dragging()
    result.current.onPointerDown(DENTIST, pointerDownOn(chip, 250, 500))
    firePointer('pointermove', 250, 503)
    expect(result.current.drag).toBeNull()
    firePointer('pointerup', 250, 503)
    expect(onDrop).not.toHaveBeenCalled()
  })

  it('takes the pointer once it travels five', () => {
    const chip = chipIn('column')
    chip.setPointerCapture = vi.fn()
    const { result } = dragging()
    result.current.onPointerDown(DENTIST, pointerDownOn(chip, 250, 500))
    firePointer('pointermove', 250, 505)
    expect(result.current.drag).toEqual({ key: 'a1#', deltaMinutes: 0, deltaDays: 0 })
    expect(chip.setPointerCapture).toHaveBeenCalledWith(1)
  })

  // 84px is 90 minutes of grid at HOUR_PX, and 90 is already a multiple of the quarter hour.
  it('snaps the minutes it moved to the quarter hour', () => {
    const chip = chipIn('column')
    const { result } = dragging()
    result.current.onPointerDown(DENTIST, pointerDownOn(chip, 250, 500))
    firePointer('pointermove', 250, 580)
    expect(result.current.drag?.deltaMinutes).toBe(90)
  })

  it('drops on the occurrence, the minutes and the days it crossed', () => {
    const chip = chipIn('column')
    const { result, onDrop } = dragging()
    result.current.onPointerDown(DENTIST, pointerDownOn(chip, 250, 500))
    firePointer('pointermove', 350, 584)
    firePointer('pointerup', 350, 584)
    expect(onDrop).toHaveBeenCalledWith(DENTIST, 90, 1)
    expect(result.current.drag).toBeNull()
  })

  it('sends nothing when the block came back to where it started', () => {
    const chip = chipIn('column')
    const { result, onDrop } = dragging()
    result.current.onPointerDown(DENTIST, pointerDownOn(chip, 250, 500))
    firePointer('pointermove', 255, 500)
    firePointer('pointerup', 255, 500)
    expect(onDrop).not.toHaveBeenCalled()
  })

  it('abandons the gesture on Escape', () => {
    const chip = chipIn('column')
    const { result, onDrop } = dragging()
    result.current.onPointerDown(DENTIST, pointerDownOn(chip, 250, 500))
    firePointer('pointermove', 250, 584)
    fireEscape()
    expect(result.current.drag).toBeNull()
    firePointer('pointerup', 250, 584)
    expect(onDrop).not.toHaveBeenCalled()
  })

  // The release still fires its click on the chip, which would reopen the bubble over the very
  // block the user has just given up moving.
  it('does not reopen the bubble on the block it gave up', () => {
    const chip = chipIn('column')
    const { result } = dragging()
    result.current.onPointerDown(DENTIST, pointerDownOn(chip, 250, 500))
    firePointer('pointermove', 250, 584)
    fireEscape()
    firePointer('pointerup', 250, 584)

    expect(chip.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true })))
      .toBe(false)
  })

  it('leaves the click of a press that never moved alone', () => {
    const chip = chipIn('column')
    const { result } = dragging()
    result.current.onPointerDown(DENTIST, pointerDownOn(chip, 250, 500))
    firePointer('pointerup', 250, 500)

    expect(chip.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true })))
      .toBe(true)
  })

  it('abandons it on pointercancel', () => {
    const chip = chipIn('column')
    const { result, onDrop } = dragging()
    result.current.onPointerDown(DENTIST, pointerDownOn(chip, 250, 500))
    firePointer('pointermove', 250, 584)
    firePointer('pointercancel', 250, 584)
    expect(result.current.drag).toBeNull()
    expect(onDrop).not.toHaveBeenCalled()
  })

  // The band has no hours to move through: a bandeau dragged sideways changes its day and
  // nothing else, and dragged up or down changes nothing at all.
  it('moves a band chip by days alone', () => {
    const chip = chipIn('band')
    const { result, onDrop } = dragging()
    result.current.onPointerDown(DENTIST, pointerDownOn(chip, 250, 10))
    firePointer('pointermove', 350, 94)
    firePointer('pointerup', 350, 94)
    expect(onDrop).toHaveBeenCalledWith(DENTIST, 0, 1)
  })

  it('is inert when gestures are off', () => {
    const chip = chipIn('column')
    const { result, onDrop } = dragging(vi.fn(), false)
    result.current.onPointerDown(DENTIST, pointerDownOn(chip, 250, 500))
    firePointer('pointermove', 350, 584)
    firePointer('pointerup', 350, 584)
    expect(result.current.drag).toBeNull()
    expect(onDrop).not.toHaveBeenCalled()
  })
})
