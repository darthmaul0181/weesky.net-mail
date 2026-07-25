import { describe, expect, it } from 'vitest'
import { forwardQuote, replyQuote } from './quote'

describe('replyQuote', () => {
  it('puts a cursor line, the attribution, then the blockquote', () => {
    const html = replyQuote('<p>original</p>', { dateText: '25 Jul 2026', name: 'Alice', address: 'a@x' })
    expect(html).toBe(
      '<div><br></div><div>On 25 Jul 2026, Alice &lt;a@x&gt; wrote:</div><blockquote><p>original</p></blockquote>')
  })

  it('escapes the attribution and drops an empty name', () => {
    const html = replyQuote('<p>o</p>', { dateText: 'd', name: '', address: 'a<b@x' })
    expect(html).toContain('On d, a&lt;b@x wrote:')
  })
})

describe('forwardQuote', () => {
  it('builds the banner and headers before the original', () => {
    const html = forwardQuote('<p>original</p>', {
      fromName: 'Alice', fromAddress: 'a@x', dateText: '25 Jul 2026', subject: 'Hi <you>', to: ['b@x', 'c@x'],
    })
    expect(html).toContain('---------- Forwarded message ----------')
    expect(html).toContain('From: Alice &lt;a@x&gt;')
    expect(html).toContain('Date: 25 Jul 2026')
    expect(html).toContain('Subject: Hi &lt;you&gt;')
    expect(html).toContain('To: b@x, c@x')
    expect(html.startsWith('<div><br></div>')).toBe(true)
    expect(html.endsWith('<p>original</p>')).toBe(true)
  })
})
