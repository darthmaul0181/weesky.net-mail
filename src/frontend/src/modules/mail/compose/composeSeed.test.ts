import { describe, expect, it } from 'vitest'
import type { MailMessageDetail, SendingIdentity } from '../api/mailTypes'
import { stagedAttachmentUrl } from '../../../api.js'
import { buildComposeSeed } from './composeSeed'

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
  })
})
