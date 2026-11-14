import { createHash } from 'node:crypto'
import { describe, expect, it } from 'vitest'
import { scriptPolicy } from './scriptPolicyPlugin'

const sha = (body: string) => `'sha256-${createHash('sha256').update(body).digest('base64')}'`

describe('scriptPolicy', () => {
  it('pins every inline script by its hash and leaves external ones to self', () => {
    const html = '<script>a()</script><script src="/config.js"></script><script>\n  b()\n</script>'

    expect(scriptPolicy(html)).toBe(
      `script-src 'self' ${sha('a()')} ${sha('\n  b()\n')}; object-src 'none'; base-uri 'none'`)
  })

  it('hashes what the browser hashes: a CRLF checkout pins the same script as an LF one', () => {
    expect(scriptPolicy('<script>a()\r\nb()</script>')).toBe(scriptPolicy('<script>a()\nb()</script>'))
  })
})
