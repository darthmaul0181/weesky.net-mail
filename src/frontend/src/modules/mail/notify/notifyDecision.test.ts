import { describe, it, expect } from 'vitest'
import type { MailMessageSummary } from '../api/mailTypes'
import { newSince, notifyBody, notifyDecision, silentBatch } from './notifyDecision'

const both = { sound: true, desktop: true }

function message(uid: number, fromName = '', subject = ''): MailMessageSummary {
  return {
    uid, subject, fromName, fromAddress: 'a@b.c', to: [], date: '2026-07-21T00:00:00Z',
    seen: false, flagged: false, answered: false, hasAttachments: false, size: 0, preview: '',
    priority: 'normal',
  }
}

describe('notifyDecision', () => {
  it('reports the arrivals and where they start', () => {
    expect(notifyDecision(10, 13, both)).toEqual({ count: 3, sinceUid: 10 })
  })

  // Opening the webmail would ring every time otherwise.
  it('stays silent on the baseline observation', () => {
    expect(notifyDecision(null, 13, both)).toBeNull()
  })

  it('stays silent when nothing arrived', () => {
    expect(notifyDecision(13, 13, both)).toBeNull()
  })

  // uidNext only ever rises; a fall means the folder was rebuilt, not that mail arrived.
  it('stays silent when uidNext went backwards', () => {
    expect(notifyDecision(20, 13, both)).toBeNull()
  })

  it('stays silent when the server stops reporting uidNext', () => {
    expect(notifyDecision(10, null, both)).toBeNull()
  })

  it.each([
    [{ sound: false, desktop: false }],
  ])('stays silent when both settings are off', settings => {
    expect(notifyDecision(10, 13, settings)).toBeNull()
  })

  it.each([
    [{ sound: true, desktop: false }],
    [{ sound: false, desktop: true }],
  ])('fires when either setting alone is on', settings => {
    expect(notifyDecision(10, 13, settings)).not.toBeNull()
  })
})

describe('newSince', () => {
  // The server sorts by Date header, so a late-delivered message lands mid-list: the new ones
  // are the ones whose uid the folder had not assigned yet, not the ones at the top.
  it('picks the messages the folder had not assigned yet', () => {
    const messages = [message(12), message(5), message(11), message(4)]

    expect(newSince(messages, 10).map(m => m.uid)).toEqual([12, 11])
  })

  it('answers empty when none qualify', () => {
    expect(newSince([message(5), message(4)], 10)).toEqual([])
  })

  // sinceUid is the previous UIDNEXT — the next uid the folder was going to assign — so a
  // single arrival lands exactly on it. A `>` here would miss every one-mail notification.
  it('includes the message sitting exactly on the boundary', () => {
    expect(newSince([message(10), message(9)], 10).map(m => m.uid)).toEqual([10])
  })
})

describe('notifyBody', () => {
  it('names the sender and subject of a single message', () => {
    expect(notifyBody([message(11, 'Alice Dupont', 'Lunch?')], 1)).toBe('Alice Dupont — Lunch?')
  })

  it('falls back to the address when there is no display name', () => {
    expect(notifyBody([message(11, '', 'Lunch?')], 1)).toBe('a@b.c — Lunch?')
  })

  it('says so when a message carries no subject', () => {
    expect(notifyBody([message(11, 'Alice Dupont', '')], 1)).toBe('Alice Dupont — (no subject)')
  })

  it('counts instead of naming when several arrived', () => {
    expect(notifyBody([message(12), message(11)], 2)).toBe('2 new messages')
  })

  // The count comes from uidNext, the messages from a fetch that may not have found them —
  // a late-delivered message sorts out of the first block. Counting is honest; naming the
  // wrong message is not.
  it('counts when the fetch did not find the arrival', () => {
    expect(notifyBody([], 1)).toBe('1 new message')
  })

  // The count comes from uidNext, the messages from a fetch: they can disagree. Naming a
  // message the count does not corroborate would be worse than counting.
  it('counts when the fetch found more messages than arrived', () => {
    expect(notifyBody([message(11, 'Alice Dupont', 'Lunch?'), message(10)], 1)).toBe('1 new message')
  })

  it('counts when the fetch found fewer messages than arrived', () => {
    expect(notifyBody([message(11, 'Alice Dupont', 'Lunch?')], 2)).toBe('2 new messages')
  })
})

/** Moving a message into the inbox appends it with a fresh uid, so uidNext advances just as it
    does for delivery. The read flags are the only thing that separates the two. */
describe('silentBatch', () => {
  const read = (uid: number): MailMessageSummary => ({ ...message(uid), seen: true })

  it('holds when the whole batch arrived already read', () => {
    expect(silentBatch([read(10)], 1, 0)).toBe(true)
    expect(silentBatch([read(11), read(10)], 2, 0)).toBe(true)
  })

  it('fails as soon as one arrival is unread', () => {
    expect(silentBatch([read(11), message(10)], 2, 0)).toBe(false)
  })

  // The flags are the direct witness: an arrival read is read whatever the counter did meanwhile.
  it('holds on read arrivals even when the unread count rose', () => {
    expect(silentBatch([read(10)], 1, 1)).toBe(true)
  })

  // A filed message keeps its own Date, and the page is sorted by it — so an old mail dragged in
  // takes a fresh uid at the top of the uid range and lands nowhere near the top of the page.
  // Its flags are then unreachable, and the folder's unread counter is the only witness left.
  it('holds when the page missed the batch and nothing new is unread', () => {
    expect(silentBatch([], 1, 0)).toBe(true)
    expect(silentBatch([read(10)], 3, 0)).toBe(true)
    expect(silentBatch([], 2, -1)).toBe(true)
  })

  it('announces when the page missed the batch and the unread count rose', () => {
    expect(silentBatch([], 1, 1)).toBe(false)
    expect(silentBatch([read(10)], 3, 2)).toBe(false)
  })

  // No counter to fall back on and no flags to read: announcing beats swallowing real mail.
  it('announces when the unread counter is unreadable', () => {
    expect(silentBatch([], 1, null)).toBe(false)
  })
})
