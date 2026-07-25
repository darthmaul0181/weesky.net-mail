const escapeHtml = (text: string) =>
  text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')

const who = (name: string, address: string) =>
  name ? `${escapeHtml(name)} &lt;${escapeHtml(address)}&gt;` : escapeHtml(address)

export interface Attribution { dateText: string; name: string; address: string }

/** The caret lands on the first; the second keeps what gets typed off the attribution. */
const CURSOR_LINES = '<div><br></div><div><br></div>'

/** Reply body: two empty lines, the attribution, then the original inside a visible blockquote. */
export function replyQuote(quotableHtml: string, attribution: Attribution): string {
  const { dateText, name, address } = attribution
  return `${CURSOR_LINES}<div>On ${escapeHtml(dateText)}, ${who(name, address)} wrote:</div>`
    + `<blockquote>${quotableHtml}</blockquote>`
}

export interface ForwardHeader {
  fromName: string; fromAddress: string; dateText: string; subject: string; to: string[]
}

/** Forward body: two cursor lines, the forwarded-message banner and headers, then the original. */
export function forwardQuote(quotableHtml: string, header: ForwardHeader): string {
  const lines = [
    '---------- Forwarded message ----------',
    `From: ${who(header.fromName, header.fromAddress)}`,
    `Date: ${escapeHtml(header.dateText)}`,
    `Subject: ${escapeHtml(header.subject)}`,
    `To: ${header.to.map(escapeHtml).join(', ')}`,
  ]
  return `${CURSOR_LINES}${lines.map(l => `<div>${l}</div>`).join('')}<div><br></div>${quotableHtml}`
}
