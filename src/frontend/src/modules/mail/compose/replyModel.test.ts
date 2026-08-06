import { describe, expect, it } from 'vitest'
import type { MailMessageDetail, SendingIdentity } from '../api/mailTypes'
import {
  editAsNewFrom, myAddresses, preselectIdentity, replyAllRecipients, replyRecipients, subjectFor,
} from './replyModel'

const detail = (overrides: Partial<MailMessageDetail> = {}): MailMessageDetail => ({
  uid: 1, folderPath: 'INBOX', uidValidity: 1, subject: 'Hello',
  fromName: 'Alice', fromAddress: 'alice@ext.example',
  to: [{ name: '', address: 'me@weesky.be' }], cc: [], date: '2026-07-25T10:00:00Z',
  authentication: null, spamScore: null, mailingList: null, sentBy: null, signedBy: null,
  unsubscribeUrl: null, tlsReceived: null, htmlBody: '', textBody: '', blockedImageCount: 0, truncated: false,
  attachments: [], messageId: 'm@x', references: [], inReplyTo: null, replyTo: [], bcc: [],
  priority: 'normal',
  ...overrides,
})

const identity = (address: string, overrides: Partial<SendingIdentity> = {}): SendingIdentity => ({
  address, displayName: address, isDefault: false, isPrimary: false, stale: false, labelIsCustom: false,
  ...overrides,
})

const mine = myAddresses(['me@weesky.be'], [{ name: 'sales', domain: 'weesky.be' }])

describe('myAddresses', () => {
  it('collects the primary and the aliases, lowercased', () => {
    expect(mine).toEqual(new Set(['me@weesky.be', 'sales@weesky.be']))
  })

  // The primary address is in no alias list — the backend tests it with its own `||` — so on a
  // connected account it is only in the set if it is handed over explicitly.
  it('collects every mailbox owned outright, skipping the ones there are none of', () => {
    expect(myAddresses(['Shared@Ext.example', null, 'mick@weesky.be'], []))
      .toEqual(new Set(['shared@ext.example', 'mick@weesky.be']))
  })

  // A connected mailbox has no alias list at all: its identities are the only source there.
  it('counts the usable identities, and only those', () => {
    const set = myAddresses(['shared@ext.example'], [], [
      identity('Shared@Ext.example', { isPrimary: true, isDefault: true }),
      identity('team@ext.example'),
      identity('gone@ext.example', { stale: true }),
    ])
    expect(set).toEqual(new Set(['shared@ext.example', 'team@ext.example']))
  })
})

describe('replyRecipients', () => {
  it('targets Reply-To over From', () => {
    const d = detail({ replyTo: [{ name: '', address: 'list@ext.example' }] })
    expect(replyRecipients(d, mine)).toEqual({ to: ['list@ext.example'], cc: [] })
  })

  it('targets From when there is no Reply-To', () => {
    expect(replyRecipients(detail(), mine)).toEqual({ to: ['alice@ext.example'], cc: [] })
  })

  it('replying to my own message targets the original To', () => {
    const d = detail({ fromAddress: 'me@weesky.be', to: [{ name: '', address: 'bob@ext.example' }] })
    expect(replyRecipients(d, mine)).toEqual({ to: ['bob@ext.example'], cc: [] })
  })
})

describe('replyAllRecipients', () => {
  it('keeps the To/Cc split and drops my addresses', () => {
    const d = detail({
      to: [{ name: '', address: 'ME@weesky.be' }, { name: '', address: 'bob@ext.example' }],
      cc: [{ name: '', address: 'carol@ext.example' }, { name: '', address: 'sales@weesky.be' }],
    })
    expect(replyAllRecipients(d, mine)).toEqual({
      to: ['alice@ext.example', 'bob@ext.example'],
      cc: ['carol@ext.example'],
    })
  })

  it('degenerates to a plain reply when everyone is me', () => {
    const d = detail({ fromAddress: 'sales@weesky.be', to: [{ name: '', address: 'me@weesky.be' }] })
    expect(replyAllRecipients(d, mine)).toEqual({ to: ['me@weesky.be'], cc: [] })
  })

  it('promotes Cc to To when To ends empty', () => {
    const d = detail({
      fromAddress: 'me@weesky.be', to: [{ name: '', address: 'sales@weesky.be' }],
      cc: [{ name: '', address: 'carol@ext.example' }],
    })
    expect(replyAllRecipients(d, mine)).toEqual({ to: ['carol@ext.example'], cc: [] })
  })
})

describe('subjectFor', () => {
  it('prefixes and never stacks', () => {
    expect(subjectFor('reply', 'Hello')).toBe('Re: Hello')
    expect(subjectFor('reply', 're: Hello')).toBe('re: Hello')
    expect(subjectFor('forward', 'FW: Hello')).toBe('FW: Hello')
    expect(subjectFor('forward', 'Fwd: Hello')).toBe('Fwd: Hello')
    expect(subjectFor('reply', 'Fwd: Hello')).toBe('Re: Fwd: Hello')
  })
})

describe('preselectIdentity', () => {
  const identities = [identity('me@weesky.be', { isDefault: true }), identity('sales@weesky.be')]

  it('picks the first of my identities found in To then Cc', () => {
    const d = detail({
      to: [{ name: '', address: 'other@ext.example' }],
      cc: [{ name: '', address: 'SALES@weesky.be' }],
    })
    expect(preselectIdentity(d, identities)).toBe('sales@weesky.be')
  })

  it('falls back to the default and never offers a stale identity', () => {
    const withStale = [identity('me@weesky.be', { isDefault: true }), identity('gone@weesky.be', { stale: true })]
    const d = detail({ to: [{ name: '', address: 'gone@weesky.be' }] })
    expect(preselectIdentity(d, withStale)).toBe('me@weesky.be')
  })
})

describe('editAsNewFrom', () => {
  it("uses the original's From when it is one of my identities, else the default", () => {
    const identities = [identity('me@weesky.be', { isDefault: true }), identity('sales@weesky.be')]
    expect(editAsNewFrom(detail({ fromAddress: 'Sales@weesky.be' }), identities)).toBe('sales@weesky.be')
    expect(editAsNewFrom(detail(), identities)).toBe('me@weesky.be')
  })
})
