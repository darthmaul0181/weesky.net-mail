import { describe, expect, it } from 'vitest'
import type { MailMessageDetail, OpenedDraft, SendingIdentity } from '../api/mailTypes'
import { stagedAttachmentUrl } from '../../../api.js'
import { buildComposeSeed, buildDraftSeed } from './composeSeed'

const detail = (overrides: Partial<MailMessageDetail> = {}): MailMessageDetail => ({
  uid: 1, folderPath: 'INBOX', uidValidity: 1, subject: 'Hello',
  fromName: 'Alice', fromAddress: 'alice@ext.example',
  to: [{ name: '', address: 'me@weesky.be' }], cc: [], date: '2026-07-25T10:00:00Z',
  authentication: null, spamScore: null, mailingList: null, sentBy: null, signedBy: null,
  unsubscribeUrl: null, tlsReceived: null, htmlBody: '', textBody: '', blockedImageCount: 0,
  attachments: [], messageId: 'm@x', references: [], inReplyTo: null, replyTo: [], bcc: [],
  ...overrides,
})

const identity = (address: string, overrides: Partial<SendingIdentity> = {}): SendingIdentity => ({
  address, displayName: address, isDefault: false, isPrimary: false, stale: false, labelIsCustom: false,
  ...overrides,
})

const prepared = {
  quotableHtml: '<p>original</p>',
  attachments: [
    { id: 'i1', fileName: 'logo.png', size: 3, contentType: 'image/png', contentId: 'logo@x' },
    { id: 'a1', fileName: 'doc.pdf', size: 9, contentType: 'application/pdf', contentId: null },
  ],
}
const identities = [identity('me@weesky.be', { isDefault: true })]
const aliases = [{ name: 'sales', domain: 'weesky.be' }]

describe('nameHints', () => {
  it('carries the sender on a reply, keyed canonically', () => {
    const seed = buildComposeSeed(
      'reply', detail({ fromName: 'Alice Dupont', fromAddress: 'Alice@Ext.example' }),
      prepared, identities, aliases, 'me@weesky.be')

    expect(seed.nameHints['alice@ext.example']).toBe('Alice Dupont')
  })

  it('carries the other recipients on a reply-all', () => {
    const seed = buildComposeSeed(
      'replyAll', detail({ cc: [{ name: 'Bob Martin', address: 'Bob@Ext.example' }] }),
      prepared, identities, aliases, 'me@weesky.be')

    expect(seed.nameHints['bob@ext.example']).toBe('Bob Martin')
  })

  it('omits an address whose header carried no name', () => {
    const seed = buildComposeSeed(
      'reply', detail({ fromName: '', to: [{ name: '', address: 'me@weesky.be' }] }),
      prepared, identities, aliases, 'me@weesky.be')

    expect(seed.nameHints).toEqual({})
  })
})

describe('buildComposeSeed', () => {
  it('reply: recipients, Re: subject, quoted body, threading', () => {
    const seed = buildComposeSeed('reply', detail(), prepared, identities, aliases, 'me@weesky.be')
    expect(seed.action).toBe('reply')
    expect(seed.to).toEqual(['alice@ext.example'])
    expect(seed.subject).toBe('Re: Hello')
    expect(seed.html).toContain('<blockquote><p>original</p></blockquote>')
    expect(seed.inReplyTo).toBe('m@x')
    expect(seed.references).toEqual(['m@x'])
    expect(seed.fromAddress).toBe('me@weesky.be')
    expect(seed.attachments).toEqual(prepared.attachments)
    expect(buildComposeSeed('replyAll', detail(), prepared, identities, aliases, 'me@weesky.be').action)
      .toBe('replyAll')
  })

  // The composer shows these as <img src>, which resolves against the SPA origin, not the API's.
  it('absolutizes the quote inline images, on every action', () => {
    const inline = {
      quotableHtml: '<p>original</p><img src="/api/Mail/Attachments/i1/content">',
      attachments: prepared.attachments,
    }
    for (const action of ['reply', 'replyAll', 'forward', 'editAsNew'] as const) {
      const seed = buildComposeSeed(action, detail(), inline, identities, aliases, 'me@weesky.be')
      expect(seed.html).toContain(`src="${stagedAttachmentUrl('i1')}"`)
      expect(seed.html).not.toContain('src="/api/Mail/Attachments/i1/content"')
    }
  })

  it('forward: empty recipients, Fwd: subject, banner body, threading', () => {
    const seed = buildComposeSeed('forward', detail(), prepared, identities, aliases, 'me@weesky.be')
    expect(seed.action).toBe('forward')
    expect(seed.to).toEqual([])
    expect(seed.subject).toBe('Fwd: Hello')
    expect(seed.html).toContain('---------- Forwarded message ----------')
    expect(seed.inReplyTo).toBe('m@x')
    expect(seed.nameHints['alice@ext.example']).toBe('Alice')
  })

  it('editAsNew: original recipients and subject, bare body, no threading', () => {
    const d = detail({ bcc: [{ name: '', address: 'hidden@ext.example' }] })
    const seed = buildComposeSeed('editAsNew', d, prepared, identities, aliases, 'me@weesky.be')
    expect(seed.action).toBe('editAsNew')
    expect(seed.to).toEqual(['me@weesky.be'])
    expect(seed.bcc).toEqual(['hidden@ext.example'])
    expect(seed.subject).toBe('Hello')
    expect(seed.html).toBe('<p>original</p>')
    expect(seed.inReplyTo).toBeNull()
    expect(seed.references).toEqual([])
    expect(seed.nameHints['alice@ext.example']).toBe('Alice')
  })
})

describe('buildDraftSeed', () => {
  const opened: OpenedDraft = {
    to: ['bob@ext.example'], cc: ['carol@ext.example'], bcc: [],
    subject: 'WIP', fromAddress: 'sales@weesky.be',
    htmlBody: '<p>hello <img src="/api/Mail/Attachments/a1/content"></p>',
    attachments: [
      { id: 'a1', fileName: 'logo.png', size: 5, contentType: 'image/png', contentId: 'logo@mail' },
      { id: 'a2', fileName: 'doc.pdf', size: 9, contentType: 'application/pdf', contentId: null },
    ],
    inReplyTo: 'msg1@ext.example', references: ['msg0@ext.example', 'msg1@ext.example'],
  }
  const ref = { folderPath: 'Drafts', uid: 41 }

  it('carries the envelope, threading and draftRef', () => {
    const seed = buildDraftSeed(opened, [identity('sales@weesky.be')], ref)
    expect(seed.action).toBe('draft')
    expect(seed.to).toEqual(['bob@ext.example'])
    expect(seed.subject).toBe('WIP')
    expect(seed.inReplyTo).toBe('msg1@ext.example')
    expect(seed.references).toEqual(['msg0@ext.example', 'msg1@ext.example'])
    expect(seed.draftRef).toEqual(ref)
    expect(seed.attachments).toHaveLength(2)
  })

  it('absolutizes the staged URLs in the body', () => {
    const seed = buildDraftSeed(opened, [], ref)
    expect(seed.html).toContain(stagedAttachmentUrl('a1'))
  })

  it('keeps the draft From only when a usable identity owns it', () => {
    expect(buildDraftSeed(opened, [identity('sales@weesky.be')], ref).fromAddress).toBe('sales@weesky.be')
    expect(buildDraftSeed(opened, [identity('sales@weesky.be', { stale: true })], ref).fromAddress).toBeNull()
    expect(buildDraftSeed(opened, [identity('other@weesky.be')], ref).fromAddress).toBeNull()
    // Case differences must not lose the choice: an IMAP client may have stored it capitalised.
    expect(buildDraftSeed({ ...opened, fromAddress: 'Sales@weesky.be' }, [identity('sales@weesky.be')], ref)
      .fromAddress).toBe('sales@weesky.be')
  })

  // A draft keeps no headers from whatever it was a reply to, so there is nothing to carry.
  it('carries no name hints', () => {
    expect(buildDraftSeed(opened, [], ref).nameHints).toEqual({})
  })
})
