import { describe, expect, it } from 'vitest'
import { columnAt, HOUR_PX, minutesToPx, pxToMinutes, SNAP_MINUTES } from './gridGeometry'

describe('minutesToPx', () => {
  it('spends one hour of grid per hour of clock', () => {
    expect(minutesToPx(0)).toBe(0)
    expect(minutesToPx(60)).toBe(HOUR_PX)
    expect(minutesToPx(90)).toBe(HOUR_PX * 1.5)
  })
})

describe('pxToMinutes', () => {
  it('snaps to the quarter hour', () => {
    expect(pxToMinutes(HOUR_PX * 9 + 20)).toBe(555)
    expect(pxToMinutes(HOUR_PX * 9)).toBe(540)
    expect(pxToMinutes(HOUR_PX * 9 + HOUR_PX / 4)).toBe(555)
    expect(pxToMinutes(0) % SNAP_MINUTES).toBe(0)
  })

  it('never leaves the day', () => {
    expect(pxToMinutes(-40)).toBe(0)
    expect(pxToMinutes(HOUR_PX * 30)).toBe(1440)
  })
})

describe('columnAt', () => {
  it('reads the column the pointer is over', () => {
    expect(columnAt(56, 0, 700, 7)).toBe(0)
    expect(columnAt(150, 0, 700, 7)).toBe(1)
    expect(columnAt(699, 0, 700, 7)).toBe(6)
  })

  it('counts from the grid left edge, not from the window', () => {
    expect(columnAt(250, 100, 700, 7)).toBe(1)
  })

  it('clamps a pointer dragged outside the grid', () => {
    expect(columnAt(-200, 0, 700, 7)).toBe(0)
    expect(columnAt(2000, 0, 700, 7)).toBe(6)
  })
})
