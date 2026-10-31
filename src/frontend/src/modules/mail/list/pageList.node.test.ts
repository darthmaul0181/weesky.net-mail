import { describe, it, expect } from 'vitest'
import { buildPageList } from './pageList'

describe('buildPageList', () => {
  it('returns nothing when there are no pages', () => {
    expect(buildPageList(0, -1)).toEqual([])
  })

  it('shows a single page', () => {
    expect(buildPageList(0, 0)).toEqual([0])
  })

  it('shows every page while they all fit', () => {
    expect(buildPageList(0, 2)).toEqual([0, 1, 2])
    expect(buildPageList(3, 6)).toEqual([0, 1, 2, 3, 4, 5, 6])
  })

  it('elides the middle when there are too many', () => {
    expect(buildPageList(10, 20)).toEqual([0, 'gap', 9, 10, 11, 'gap', 20])
  })

  it('keeps the first and last reachable from anywhere', () => {
    const far = buildPageList(19, 39)

    expect(far[0]).toBe(0)
    expect(far[far.length - 1]).toBe(39)
  })

  it('elides only on the far side when near an end', () => {
    expect(buildPageList(0, 20)).toEqual([0, 1, 'gap', 20])
    expect(buildPageList(20, 20)).toEqual([0, 'gap', 19, 20])
  })

  // A gap that hides one page is the same width as the page it hides, and costs a click.
  it('shows the single hidden page instead of a gap standing for it', () => {
    expect(buildPageList(2, 20)).toEqual([0, 1, 2, 3, 'gap', 20])
  })

  it('never repeats a page', () => {
    for (let current = 0; current <= 20; current++) {
      const numbers = buildPageList(current, 20).filter(item => item !== 'gap')

      expect(new Set(numbers).size).toBe(numbers.length)
    }
  })

  it('always includes the current page', () => {
    for (let current = 0; current <= 20; current++) {
      expect(buildPageList(current, 20)).toContain(current)
    }
  })

  it('stays in ascending order', () => {
    const numbers = buildPageList(10, 20).filter((item): item is number => item !== 'gap')

    expect(numbers).toEqual([...numbers].sort((a, b) => a - b))
  })
})
