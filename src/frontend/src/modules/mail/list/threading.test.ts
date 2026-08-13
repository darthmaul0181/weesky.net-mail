import { describe, expect, it } from 'vitest'
import type { MailFolderPage, MailMessageSummary } from '../api/mailTypes'
import { dedupeThreads, flatMessages, groupsOf, memberUids, threadKeyOf } from './threading'

const msg = (uid: number): MailMessageSummary => ({
  uid, subject: `s${uid}`, fromName: '', fromAddress: 'a@b.c', to: [], date: '2026-08-13T10:00:00Z',
  seen: true, flagged: false, answered: false, hasAttachments: false, size: 1, preview: '', priority: 'normal',
})

const page = (over: Partial<MailFolderPage>): MailFolderPage => ({
  folderPath: 'INBOX', uidValidity: 1, total: 0, page: 0, pageSize: 100, messages: [], ...over,
})

describe('threadKeyOf', () => {
  it('is the oldest member — the last of the newest-first list', () => {
    expect(threadKeyOf([msg(30), msg(10)])).toBe(10)
  })
})

describe('groupsOf', () => {
  it('maps a grouped page one group per thread', () => {
    const p = page({ threads: [{ messages: [msg(30), msg(10)] }, { messages: [msg(20)] }] })
    expect(groupsOf(p)).toEqual([
      { key: 10, messages: [msg(30), msg(10)] },
      { key: 20, messages: [msg(20)] },
    ])
  })

  it('maps a flat page to singleton groups', () => {
    const p = page({ messages: [msg(3), msg(2)] })
    expect(groupsOf(p)).toEqual([
      { key: 3, messages: [msg(3)] },
      { key: 2, messages: [msg(2)] },
    ])
  })
})

describe('dedupeThreads', () => {
  it('keeps the first version of a thread seen twice across blocks', () => {
    const block0 = page({ threads: [{ messages: [msg(30), msg(10)] }] })
    const block1 = page({ threads: [{ messages: [msg(10)] }] })
    expect(dedupeThreads([block0, block1])).toEqual([{ key: 10, messages: [msg(30), msg(10)] }])
  })

  it('drops a member already shown under another thread, and an emptied thread whole', () => {
    const block0 = page({ threads: [{ messages: [msg(30), msg(10)] }] })
    const block1 = page({ threads: [{ messages: [msg(30)] }] })
    expect(dedupeThreads([block0, block1])).toEqual([{ key: 10, messages: [msg(30), msg(10)] }])
  })
})

describe('flatMessages', () => {
  it('flattens the summaries in display order', () => {
    expect(flatMessages([{ key: 10, messages: [msg(30), msg(10)] }, { key: 20, messages: [msg(20)] }]))
      .toEqual([msg(30), msg(10), msg(20)])
  })
})

describe('memberUids', () => {
  it('flattens in display order', () => {
    expect(memberUids([{ key: 10, messages: [msg(30), msg(10)] }, { key: 20, messages: [msg(20)] }]))
      .toEqual([30, 10, 20])
  })
})
