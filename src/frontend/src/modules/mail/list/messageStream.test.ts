import { describe, it, expect } from 'vitest'
import type { MailFolderPage, MailMessageSummary } from '../api/mailTypes'
import { PREFETCH_ROWS, dedupeByUid, nextBlockIndex, sentinelIndexOf } from './messageStream'

function message(uid: number): MailMessageSummary {
  return {
    uid, subject: `s${uid}`, fromName: '', fromAddress: 'a@b.c', date: '2026-07-21T00:00:00Z',
    seen: false, flagged: false, answered: false, hasAttachments: false, size: 0, preview: '',
  }
}

function page(uids: number[], total = 1000): MailFolderPage {
  return {
    folderPath: 'INBOX', uidValidity: 1, total, page: 0, pageSize: 100,
    messages: uids.map(message),
  }
}

describe('dedupeByUid', () => {
  it('flattens the blocks in order', () => {
    expect(dedupeByUid([page([3, 2]), page([1])]).map(m => m.uid)).toEqual([3, 2, 1])
  })

  // Paging is a numeric offset: one message arriving between two blocks shifts everything by
  // one and the last row of block 1 reappears at the head of block 2.
  it('keeps the first occurrence when a block repeats a uid', () => {
    expect(dedupeByUid([page([3, 2]), page([2, 1])]).map(m => m.uid)).toEqual([3, 2, 1])
  })

  it('answers an empty list for no blocks', () => {
    expect(dedupeByUid([])).toEqual([])
  })
})

describe('nextBlockIndex', () => {
  it('asks for the next block after a full one', () => {
    expect(nextBlockIndex(page([1, 2, 3]), 1, 3)).toBe(1)
  })

  it('stops on a partial block', () => {
    expect(nextBlockIndex(page([1, 2]), 1, 3)).toBeUndefined()
  })

  it('stops on an empty folder', () => {
    expect(nextBlockIndex(page([]), 1, 3)).toBeUndefined()
  })

  // 300 messages in blocks of 100: every block is full, so the stop can only come from the
  // empty block that follows. An implementation written by eye misses this.
  it('asks for one more block when the folder is an exact multiple, then stops', () => {
    expect(nextBlockIndex(page([1, 2, 3]), 3, 3)).toBe(3)
    expect(nextBlockIndex(page([]), 4, 3)).toBeUndefined()
  })

  // total moves when mail arrives; a short block is an observed fact.
  it('ignores a total that disagrees with the blocks', () => {
    expect(nextBlockIndex(page([1, 2], 9999), 1, 3)).toBeUndefined()
  })
})

describe('sentinelIndexOf', () => {
  it('sits PREFETCH_ROWS before the last row', () => {
    expect(sentinelIndexOf(100)).toBe(100 - PREFETCH_ROWS)
  })

  it('sits at the top while the list is shorter than the margin', () => {
    expect(sentinelIndexOf(5)).toBe(0)
    expect(sentinelIndexOf(0)).toBe(0)
  })
})
