import { describe, expect, it } from 'vitest'
import { nextUidOf } from './nextUid'

describe('nextUidOf', () => {
  it('returns the successor when the departing uid has one', () => {
    expect(nextUidOf([10, 20, 30], 20)).toBe(30)
  })

  it('returns the previous entry when the departing uid is last', () => {
    expect(nextUidOf([10, 20, 30], 30)).toBe(20)
  })

  it('returns null when the departing uid is the only entry', () => {
    expect(nextUidOf([10], 10)).toBeNull()
  })

  it('returns null when the departing uid is not in the list', () => {
    expect(nextUidOf([10, 20, 30], 99)).toBeNull()
  })

  // A bulk action departs a whole batch: the successor must skip every member it removed, not
  // just the open row, or the reader lands on a sibling the same action deleted.
  it('skips a departing sibling after the open row and lands on the next survivor', () => {
    // Open 20, batch [20, 30]: 30 is gone too, so the survivor is 40, not 30.
    expect(nextUidOf([10, 20, 30, 40], 20, [20, 30])).toBe(40)
  })

  it('falls back to the nearest survivor before the open row when none survive after', () => {
    // Open 30, batch [30, 40]: nothing survives after, so it steps back to 10 (20 also departs).
    expect(nextUidOf([10, 20, 30, 40], 30, [30, 40, 20])).toBe(10)
  })

  it('returns null when the whole loaded set departs', () => {
    expect(nextUidOf([10, 20, 30], 20, [10, 20, 30])).toBeNull()
  })

  it('defaults the batch to the open uid alone (unchanged single-row behaviour)', () => {
    expect(nextUidOf([10, 20, 30], 20)).toBe(30)
    expect(nextUidOf([10, 20, 30], 30)).toBe(20)
  })
})
