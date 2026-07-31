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
  priority: 'normal',
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

// A quoted body's inline parts are staged files, namespaced by account on the backend. The
// composer shows them as <img> subresources, which cannot carry the X-Account-Id header.
describe('staged inline images in a seed', () => {
  it('names the active account in a quote seed', () => {
    const seed = buildComposeSeed(
      'forward', detail(), { ...prepared, quotableHtml: '<img src="/api/Mail/Attachments/i1/content">' },
      identities, aliases, ['me@weesky.be'], 'linked-1')

    expect(seed.html).toContain(stagedAttachmentUrl('i1', 'linked-1'))
  })

  it('names the active account in a draft seed', () => {
    const opened: OpenedDraft = {
      to: [], cc: [], bcc: [], subject: 's', fromAddress: null,
      htmlBody: '<img src="/api/Mail/Attachments/i1/content">',
      attachments: [{ id: 'i1', fileName: 'logo.png', size: 3, contentType: 'image/png', contentId: 'logo@x' }],
      inReplyTo: null, references: [], priority: 'normal', textBody: null,
    }

    const seed = buildDraftSeed(opened, [], { folderPath: 'Drafts', uid: 9 }, 'linked-1')

    expect(seed.html).toContain(stagedAttachmentUrl('i1', 'linked-1'))
  })
})

describe('nameHints', () => {
  it('carries the sender on a reply, keyed canonically', () => {
    const seed = buildComposeSeed(
      'reply', detail({ fromName: 'Alice Dupont', fromAddress: 'Alice@Ext.example' }),
      prepared, identities, aliases, ['me@weesky.be'], 'primary')

    expect(seed.nameHints['alice@ext.example']).toBe('Alice Dupont')
  })

  it('carries the other recipients on a reply-all', () => {
    const seed = buildComposeSeed(
      'replyAll', detail({ cc: [{ name: 'Bob Martin', address: 'Bob@Ext.example' }] }),
      prepared, identities, aliases, ['me@weesky.be'], 'primary')

    expect(seed.nameHints['bob@ext.example']).toBe('Bob Martin')
  })

  it('omits an address whose header carried no name', () => {
    const seed = buildComposeSeed(
      'reply', detail({ fromName: '', to: [{ name: '', address: 'me@weesky.be' }] }),
      prepared, identities, aliases, ['me@weesky.be'], 'primary')

    expect(seed.nameHints).toEqual({})
  })
})

describe('buildComposeSeed', () => {
  it('reply: recipients, Re: subject, quoted body, threading', () => {
    const seed = buildComposeSeed('reply', detail(), prepared, identities, aliases, ['me@weesky.be'], 'primary')
    expect(seed.action).toBe('reply')
    expect(seed.to).toEqual(['alice@ext.example'])
    expect(seed.subject).toBe('Re: Hello')
    expect(seed.html).toContain('<blockquote><p>original</p></blockquote>')
    expect(seed.inReplyTo).toBe('m@x')
    expect(seed.references).toEqual(['m@x'])
    expect(seed.fromAddress).toBe('me@weesky.be')
    expect(seed.attachments).toEqual(prepared.attachments)
    expect(buildComposeSeed('replyAll', detail(), prepared, identities, aliases, ['me@weesky.be'], 'primary').action)
      .toBe('replyAll')
  })

  // Three ways to be the user on a connected account: its own address, an identity on it, and the
  // primary mailbox — which no alias list carries. Keeping any of them mails the user themselves.
  it('reply-all drops the active account, its identities and the primary alike', () => {
    const own = [identity('shared@ext.example', { isPrimary: true, isDefault: true }), identity('team@ext.example')]
    const d = detail({
      to: [{ name: '', address: 'shared@ext.example' }, { name: '', address: 'Mick@weesky.be' },
        { name: '', address: 'bob@ext.example' }],
      cc: [{ name: '', address: 'Team@ext.example' }, { name: '', address: 'carol@ext.example' }],
    })

    const seed = buildComposeSeed('replyAll', d, prepared, own, [], ['shared@ext.example', 'mick@weesky.be'], 'linked-1')

    expect(seed.to).toEqual(['alice@ext.example', 'bob@ext.example'])
    expect(seed.cc).toEqual(['carol@ext.example'])
  })

  // The composer shows these as <img src>, which resolves against the SPA origin, not the API's.
  it('absolutizes the quote inline images, on every action', () => {
    const inline = {
      quotableHtml: '<p>original</p><img src="/api/Mail/Attachments/i1/content">',
      attachments: prepared.attachments,
    }
    for (const action of ['reply', 'replyAll', 'forward', 'editAsNew'] as const) {
      const seed = buildComposeSeed(action, detail(), inline, identities, aliases, ['me@weesky.be'], 'primary')
      expect(seed.html).toContain(`src="${stagedAttachmentUrl('i1')}"`)
      expect(seed.html).not.toContain('src="/api/Mail/Attachments/i1/content"')
    }
  })

  /** A reply to an urgent message is not itself urgent, and edit-as-new starts a message of its
      own — the priority is the sender's fresh choice on all four. */
  it.each(['reply', 'replyAll', 'forward', 'editAsNew'] as const)(
    'opens a %s at normal whatever the quoted message declared', (action) => {
      const seed = buildComposeSeed(
        action, detail({ priority: 'high' }), prepared, identities, aliases, ['me@weesky.be'], 'primary')

      expect(seed.priority).toBe('normal')
    })

  it('forward: empty recipients, Fwd: subject, banner body, threading', () => {
    const seed = buildComposeSeed('forward', detail(), prepared, identities, aliases, ['me@weesky.be'], 'primary')
    expect(seed.action).toBe('forward')
    expect(seed.to).toEqual([])
    expect(seed.subject).toBe('Fwd: Hello')
    expect(seed.html).toContain('---------- Forwarded message ----------')
    expect(seed.inReplyTo).toBe('m@x')
    expect(seed.nameHints['alice@ext.example']).toBe('Alice')
  })

  it('editAsNew: original recipients and subject, bare body, no threading', () => {
    const d = detail({ bcc: [{ name: '', address: 'hidden@ext.example' }] })
    const seed = buildComposeSeed('editAsNew', d, prepared, identities, aliases, ['me@weesky.be'], 'primary')
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
    priority: 'normal', textBody: null,
  }
  const ref = { folderPath: 'Drafts', uid: 41 }

  it('carries the envelope, threading and draftRef', () => {
    const seed = buildDraftSeed(opened, [identity('sales@weesky.be')], ref, 'primary')
    expect(seed.action).toBe('draft')
    expect(seed.to).toEqual(['bob@ext.example'])
    expect(seed.subject).toBe('WIP')
    expect(seed.inReplyTo).toBe('msg1@ext.example')
    expect(seed.references).toEqual(['msg0@ext.example', 'msg1@ext.example'])
    expect(seed.draftRef).toEqual(ref)
    expect(seed.attachments).toHaveLength(2)
  })

  it('absolutizes the staged URLs in the body', () => {
    const seed = buildDraftSeed(opened, [], ref, 'primary')
    expect(seed.html).toContain(stagedAttachmentUrl('a1'))
  })

  it('keeps the draft From only when a usable identity owns it', () => {
    expect(buildDraftSeed(opened, [identity('sales@weesky.be')], ref, 'primary').fromAddress).toBe('sales@weesky.be')
    expect(buildDraftSeed(opened, [identity('sales@weesky.be', { stale: true })], ref, 'primary').fromAddress).toBeNull()
    expect(buildDraftSeed(opened, [identity('other@weesky.be')], ref, 'primary').fromAddress).toBeNull()
    // Case differences must not lose the choice: an IMAP client may have stored it capitalised.
    expect(buildDraftSeed({ ...opened, fromAddress: 'Sales@weesky.be' }, [identity('sales@weesky.be')], ref, 'primary')
      .fromAddress).toBe('sales@weesky.be')
  })

  it('carries a saved draft priority into the seed', () => {
    expect(buildDraftSeed({ ...opened, priority: 'high' }, [], ref, 'primary').priority).toBe('high')
  })

  // A draft keeps no headers from whatever it was a reply to, so there is nothing to carry.
  it('carries no name hints', () => {
    expect(buildDraftSeed(opened, [], ref, 'primary').nameHints).toEqual({})
  })
})
