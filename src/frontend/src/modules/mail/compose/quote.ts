import i18next from 'i18next'
import { escapeHtml } from '../../../lib/escapeHtml'

const who = (name: string, address: string) =>
  name ? `${escapeHtml(name)} &lt;${escapeHtml(address)}&gt;` : escapeHtml(address)

export interface Attribution { dateText: string; name: string; address: string }

/** The caret lands on the first; the second keeps what gets typed off the attribution. */
const CURSOR_LINES = '<div><br></div><div><br></div>'

/**
 * Reply body: two empty lines, the attribution, then the original inside a visible blockquote.
 *
 * The attribution and the forward headers below are written into the user's own draft and read
 * back in the composer before anything is sent, so they follow the interface language. Both
 * operands are already HTML-escaped and `escapeValue` is off, so `t` inserts them verbatim.
 */
export function replyQuote(quotableHtml: string, attribution: Attribution): string {
  const { dateText, name, address } = attribution
  const line = i18next.t('compose:quote.attribution', {
    date: escapeHtml(dateText), who: who(name, address),
  })
  return `${CURSOR_LINES}<div>${line}</div><blockquote>${quotableHtml}</blockquote>`
}

export interface ForwardHeader {
  fromName: string; fromAddress: string; dateText: string; subject: string; to: string[]
}

/** Forward body: two cursor lines, the forwarded-message banner and headers, then the original. */
export function forwardQuote(quotableHtml: string, header: ForwardHeader): string {
  const lines = [
    i18next.t('compose:quote.forwardBanner'),
    i18next.t('compose:quote.from', { value: who(header.fromName, header.fromAddress) }),
    i18next.t('compose:quote.date', { value: escapeHtml(header.dateText) }),
    i18next.t('compose:quote.subject', { value: escapeHtml(header.subject) }),
    i18next.t('compose:quote.to', { value: header.to.map(escapeHtml).join(', ') }),
  ]
  return `${CURSOR_LINES}${lines.map(l => `<div>${l}</div>`).join('')}<div><br></div>${quotableHtml}`
}
