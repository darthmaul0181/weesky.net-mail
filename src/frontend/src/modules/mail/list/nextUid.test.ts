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
})
