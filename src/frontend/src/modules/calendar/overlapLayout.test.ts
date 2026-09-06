import { describe, expect, it } from 'vitest'
import { HOUR_PX, minutesToPx } from './gridGeometry'
import { layoutColumn } from './overlapLayout'

interface Slot { id: string; start: number; end: number }

const at = (id: string, start: number, end: number): Slot => ({ id, start, end })

function layout(items: Slot[]) {
  return layoutColumn(items, s => s.start, s => s.end, HOUR_PX, 20)
}

describe('layoutColumn', () => {
  it('gives an event the whole width when nothing overlaps it', () => {
    const placed = layout([at('a', 540, 600), at('b', 660, 720)])

    expect(placed.map(p => [p.item.id, p.column, p.columns]))
      .toEqual([['a', 0, 1], ['b', 0, 1]])
  })

  it('splits the width between two events on the same hour', () => {
    const placed = layout([at('a', 540, 600), at('b', 540, 600)])

    expect(placed.map(p => [p.item.id, p.column, p.columns]))
      .toEqual([['a', 0, 2], ['b', 1, 2]])
  })

  // C starts when B ends, so it takes the column B has just freed rather than opening a third.
  it('reuses a freed column inside one cluster', () => {
    const placed = layout([at('a', 540, 720), at('b', 540, 600), at('c', 600, 660)])

    expect(placed.map(p => [p.item.id, p.column, p.columns]))
      .toEqual([['a', 0, 2], ['b', 1, 2], ['c', 1, 2]])
  })

  it('places the top at the start minute and floors the height', () => {
    const [placed] = layout([at('a', 540, 550)])

    expect(placed.top).toBe(minutesToPx(540))
    expect(placed.height).toBe(20)
  })

  it('measures a long event by its own duration', () => {
    const [placed] = layout([at('a', 540, 660)])

    expect(placed.height).toBe(HOUR_PX * 2)
  })

  it('answers nothing for nothing', () => {
    expect(layout([])).toEqual([])
  })
})
