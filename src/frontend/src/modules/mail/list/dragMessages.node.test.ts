import { describe, it, expect } from 'vitest'
import { folderNodeOf } from '../mailTestHarness'
import { dragUids, serializeDrag, parseDrag, canDropInto } from './dragMessages'

describe('dragUids', () => {
  it('carries the whole selection when the dragged row belongs to it', () => {
    expect(dragUids([3, 7, 9], [7])).toEqual([3, 7, 9])
  })

  it('carries the dragged row alone when it is not in the selection', () => {
    // An unchecked row dragged while others are checked must not disturb that selection.
    expect(dragUids([3, 7, 9], [5])).toEqual([5])
  })

  it('carries the row alone when nothing is selected', () => {
    expect(dragUids([], [5])).toEqual([5])
  })

  it('carries the selection when every member of a thread row is checked', () => {
    expect(dragUids([3, 7, 9], [7, 9])).toEqual([3, 7, 9])
  })

  it('carries the thread alone when only part of it is checked', () => {
    expect(dragUids([3, 7], [7, 9])).toEqual([7, 9])
  })
})

describe('serializeDrag / parseDrag', () => {
  it('round-trips a payload', () => {
    const raw = serializeDrag({ sourcePath: 'INBOX', uids: [1, 2] })
    expect(parseDrag(raw)).toEqual({ sourcePath: 'INBOX', uids: [1, 2] })
  })

  it('rejects a foreign or malformed string', () => {
    expect(parseDrag('not json')).toBeNull()
    expect(parseDrag(JSON.stringify({ sourcePath: 'INBOX' }))).toBeNull()
    expect(parseDrag(JSON.stringify({ uids: [1] }))).toBeNull()
  })

  it('rejects an empty or non-numeric uid list', () => {
    expect(parseDrag(JSON.stringify({ sourcePath: 'INBOX', uids: [] }))).toBeNull()
    expect(parseDrag(JSON.stringify({ sourcePath: 'INBOX', uids: ['a'] }))).toBeNull()
  })
})

describe('canDropInto', () => {
  it('accepts a selectable folder other than the source', () => {
    expect(canDropInto(folderNodeOf({ path: 'Archive' }), 'INBOX')).toBe(true)
  })

  it('refuses the folder the messages already sit in', () => {
    expect(canDropInto(folderNodeOf({ path: 'INBOX' }), 'INBOX')).toBe(false)
  })

  it('refuses a non-selectable container', () => {
    expect(canDropInto(folderNodeOf({ path: 'Parent', selectable: false }), 'INBOX')).toBe(false)
  })
})
