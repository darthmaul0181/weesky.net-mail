import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import ReaderDetails from './ReaderDetails'
import type { MailMessageDetail } from '../api/mailTypes'

const message: MailMessageDetail = {
  uid: 1, folderPath: 'INBOX', uidValidity: 1,
  subject: 'News', fromName: 'Weesky News', fromAddress: 'news@weesky.net',
  to: [{ name: '', address: 'mick@weesky.be' }],
  cc: [{ name: 'Bob', address: 'bob@x.be' }],
  date: '2026-07-02T10:03:00Z',
  messageId: null, references: [], inReplyTo: null, replyTo: [], bcc: [],
  authentication: null, spamScore: null,
  mailingList: '<news.weesky.net>', sentBy: 'a547955.bnc3.mailjet.com', signedBy: 'weesky.net',
  unsubscribeUrl: 'https://news.weesky.net/unsub', tlsReceived: true,
  htmlBody: '', textBody: '', blockedImageCount: 0, attachments: [], priority: 'normal',
}

describe('ReaderDetails', () => {
  it('shows every row when the message carries every datum', () => {
    render(<ReaderDetails message={message} />)

    expect(screen.getByText('Weesky News')).toBeInTheDocument()
    expect(screen.getByText('<news@weesky.net>')).toBeInTheDocument()
    expect(screen.getByText('mick@weesky.be')).toBeInTheDocument()
    expect(screen.getByText('Bob')).toBeInTheDocument()
    expect(screen.getByText(/2026/)).toBeInTheDocument()
    expect(screen.getByText('<news.weesky.net>')).toBeInTheDocument()
    expect(screen.getByText('a547955.bnc3.mailjet.com')).toBeInTheDocument()
    expect(screen.getByText('weesky.net')).toBeInTheDocument()
    expect(screen.getByText(/standard encryption/i)).toBeInTheDocument()
  })

  it('drops the rows whose datum is absent, leaving no empty labels', () => {
    render(<ReaderDetails message={{
      ...message, cc: [], mailingList: null, sentBy: null, signedBy: null,
      unsubscribeUrl: null, tlsReceived: null,
    }} />)

    expect(screen.queryByText('Cc:')).not.toBeInTheDocument()
    expect(screen.queryByText('Mailing list:')).not.toBeInTheDocument()
    expect(screen.queryByText('Mailed by:')).not.toBeInTheDocument()
    expect(screen.queryByText('Signed by:')).not.toBeInTheDocument()
    expect(screen.queryByText('Unsubscribe:')).not.toBeInTheDocument()
    expect(screen.queryByText('Security:')).not.toBeInTheDocument()
  })

  it('shows the bare address alone when the sender has no display name', () => {
    render(<ReaderDetails message={{ ...message, fromName: 'news@weesky.net' }} />)

    expect(screen.getByText('news@weesky.net')).toBeInTheDocument()
    expect(screen.queryByText('<news@weesky.net>')).not.toBeInTheDocument()
  })

  it('opens an http unsubscribe link in a new tab, without a referrer', () => {
    render(<ReaderDetails message={message} />)

    const link = screen.getByRole('link', { name: /unsubscribe/i })
    expect(link).toHaveAttribute('href', 'https://news.weesky.net/unsub')
    expect(link).toHaveAttribute('target', '_blank')
    expect(link).toHaveAttribute('rel', 'noopener noreferrer')
  })

  it('links a mailto unsubscribe in place, not in a new tab', () => {
    render(<ReaderDetails message={{ ...message, unsubscribeUrl: 'mailto:unsub@x.be' }} />)

    const link = screen.getByRole('link', { name: /unsubscribe/i })
    expect(link).toHaveAttribute('href', 'mailto:unsub@x.be')
    expect(link).not.toHaveAttribute('target')
  })

  it('says so plainly when the last hop was unencrypted', () => {
    render(<ReaderDetails message={{ ...message, tlsReceived: false }} />)

    expect(screen.getByText(/no encryption/i)).toBeInTheDocument()
    expect(screen.queryByText(/standard encryption/i)).not.toBeInTheDocument()
  })

  it('drops the Security row when an older backend omits tlsReceived entirely', () => {
    const { tlsReceived, ...withoutTls } = message
    void tlsReceived

    render(<ReaderDetails message={withoutTls as MailMessageDetail} />)

    expect(screen.queryByText('Security:')).not.toBeInTheDocument()
    expect(screen.queryByText(/no encryption/i)).not.toBeInTheDocument()
  })

  // The scheme is case-insensitive on the wire; a new tab would open blank on a mailto.
  it('treats an uppercase mailto as a mailto', () => {
    render(<ReaderDetails message={{ ...message, unsubscribeUrl: 'MAILTO:unsub@x.be' }} />)

    expect(screen.getByRole('link', { name: /unsubscribe/i })).not.toHaveAttribute('target')
  })
})
