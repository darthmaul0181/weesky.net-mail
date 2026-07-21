import { describe, it, expect } from 'vitest'
import type { FolderSnapshot } from './folderDelta'
import { folderChanged, uidValidityBroke } from './folderDelta'

const base: FolderSnapshot = { uidNext: 10, total: 5, unread: 2, highestModSeq: 40, uidValidity: 100 }

describe('folderChanged', () => {
  it('sees nothing when nothing moved', () => {
    expect(folderChanged(base, { ...base })).toBe(false)
  })

  it.each([
    ['an arrival', { ...base, uidNext: 11 }],
    ['a deletion made elsewhere', { ...base, total: 4 }],
    ['a read/unread flip elsewhere', { ...base, unread: 1 }],
    ['a flags-only change elsewhere', { ...base, highestModSeq: 41 }],
  ])('sees %s', (_label, next) => {
    expect(folderChanged(base, next)).toBe(true)
  })

  // A server without CONDSTORE answers null forever; a null must never look like a change.
  it('ignores fields the server does not report', () => {
    const previous = { ...base, highestModSeq: null }
    expect(folderChanged(previous, { ...previous })).toBe(false)
  })

  // Null-to-value is discovery, not change: without this, the first poll after a deploy
  // that adds a counter would refresh every open list for nothing.
  it('does not fire on discovery of a counter', () => {
    expect(folderChanged({ ...base, highestModSeq: null }, base)).toBe(false)
    expect(folderChanged({ ...base, uidNext: null }, base)).toBe(false)
  })

  it('leaves uidValidity to uidValidityBroke', () => {
    expect(folderChanged(base, { ...base, uidValidity: 101 })).toBe(false)
  })
})

describe('uidValidityBroke', () => {
  it('fires only when uidValidity moved', () => {
    expect(uidValidityBroke(base, { ...base, uidValidity: 101 })).toBe(true)
    expect(uidValidityBroke(base, { ...base, uidNext: 11 })).toBe(false)
  })
})
