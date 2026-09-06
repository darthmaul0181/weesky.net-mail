import { renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { occurrenceOf } from './calendarTestHarness'
import {
  fireEscape, firePointer, installPointerEvents, pointerDownOn,
} from '../../test-utils'
import { useResizeEvent } from './useResizeEvent'

/** An hour long, stated as instants so the base duration is the same number on any machine. */
const DENTIST = occurrenceOf({
  eventId: 'a1', summary: 'Dentist',
  startUtc: '2026-09-16T07:00:00Z', endUtc: '2026-09-16T08:00:00Z',
})

beforeEach(installPointerEvents)
afterEach(() => { document.body.innerHTML = '' })

function handle() {
  const chip = document.createElement('button')
  chip.className = 'event-chip'
  const grip = document.createElement('span')
  grip.className = 'event-resize-handle'
  chip.append(grip)
  document.body.append(chip)
  return grip
}

function resizing(onResize = vi.fn(), enabled = true) {
  const { result } = renderHook(() => useResizeEvent({ enabled, onResize }))
  return { result, onResize }
}

describe('useResizeEvent', () => {
  // 28px is half an hour of grid, so an hour becomes ninety minutes.
  it('lengthens the event by what the handle travelled', () => {
    const grip = handle()
    const { result, onResize } = resizing()
    result.current.onPointerDown(DENTIST, pointerDownOn(grip, 100, 560))
    firePointer('pointermove', 100, 588)
    expect(result.current.resize).toEqual({ key: 'a1#', durationMinutes: 90 })
    firePointer('pointerup', 100, 588)
    expect(onResize).toHaveBeenCalledWith(DENTIST, 90)
  })

  it('never takes an event under a quarter of an hour', () => {
    const grip = handle()
    const { result, onResize } = resizing()
    result.current.onPointerDown(DENTIST, pointerDownOn(grip, 100, 560))
    firePointer('pointermove', 100, 360)
    expect(result.current.resize?.durationMinutes).toBe(15)
    firePointer('pointerup', 100, 360)
    expect(onResize).toHaveBeenCalledWith(DENTIST, 15)
  })

  it('leaves a press on the handle a press', () => {
    const grip = handle()
    const { result, onResize } = resizing()
    result.current.onPointerDown(DENTIST, pointerDownOn(grip, 100, 560))
    firePointer('pointermove', 100, 563)
    firePointer('pointerup', 100, 563)
    expect(result.current.resize).toBeNull()
    expect(onResize).not.toHaveBeenCalled()
  })

  it('sends nothing when the duration did not change', () => {
    const grip = handle()
    const { result, onResize } = resizing()
    result.current.onPointerDown(DENTIST, pointerDownOn(grip, 100, 560))
    firePointer('pointermove', 100, 565)
    firePointer('pointerup', 100, 565)
    expect(onResize).not.toHaveBeenCalled()
  })

  it('abandons the gesture on Escape', () => {
    const grip = handle()
    const { result, onResize } = resizing()
    result.current.onPointerDown(DENTIST, pointerDownOn(grip, 100, 560))
    firePointer('pointermove', 100, 588)
    fireEscape()
    expect(result.current.resize).toBeNull()
    firePointer('pointerup', 100, 588)
    expect(onResize).not.toHaveBeenCalled()
  })

  it('is inert when gestures are off', () => {
    const grip = handle()
    const { result, onResize } = resizing(vi.fn(), false)
    result.current.onPointerDown(DENTIST, pointerDownOn(grip, 100, 560))
    firePointer('pointermove', 100, 588)
    firePointer('pointerup', 100, 588)
    expect(result.current.resize).toBeNull()
    expect(onResize).not.toHaveBeenCalled()
  })
})
