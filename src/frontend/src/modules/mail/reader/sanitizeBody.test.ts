import { describe, it, expect } from 'vitest'
import { revealBlockedImages, sanitizeBody } from './sanitizeBody'

describe('sanitizeBody', () => {
  // These prove the client barrier on its own. The backend already sanitised the body, but
  // that pass uses a different parser — the point is that a body must defeat both.

  it.each([
    ['<script>alert(1)</script><p>hi</p>', 'script'],
    ['<img src="x" onerror="alert(1)">', 'onerror'],
    ['<iframe src="https://evil.example"></iframe>', 'iframe'],
    ['<object data="evil"></object>', 'object'],
    ['<embed src="evil">', 'embed'],
    ['<form action="https://evil.example"></form>', 'form'],
    ['<base href="https://evil.example/">', 'base'],
    ['<a href="javascript:alert(1)">x</a>', 'javascript:'],
    ['<svg><script>alert(1)</script></svg>', 'script'],
    ['<math><annotation-xml encoding="text/html"><script>alert(1)</script></annotation-xml></math>', 'script'],
  ])('strips %s', (hostile, forbidden) => {
    expect(sanitizeBody(hostile)).not.toContain(forbidden)
  })

  it('keeps the harmless part of a hostile document', () => {
    expect(sanitizeBody('<script>alert(1)</script><p>hi</p>')).toContain('hi')
  })

  it('keeps the formatting a message legitimately carries', () => {
    const formatted =
      '<p><strong>bold</strong> <em>italic</em></p><ul><li>one</li></ul>' +
      '<blockquote>quoted</blockquote><table><tr><td>cell</td></tr></table>'

    const result = sanitizeBody(formatted)

    expect(result).toContain('<strong>')
    expect(result).toContain('<li>')
    expect(result).toContain('blockquote')
    expect(result).toContain('<td>')
  })

  it('preserves data-blocked-src so images can still be revealed', () => {
    const result = sanitizeBody('<img data-blocked-src="https://tracker.example/p.gif">')

    expect(result).toContain('data-blocked-src')
  })

  it('returns empty for an empty body', () => {
    expect(sanitizeBody('')).toBe('')
  })
})

describe('revealBlockedImages', () => {
  it('turns withheld sources back into real ones', () => {
    expect(revealBlockedImages('<img data-blocked-src="https://x.example/a.png">'))
      .toContain('src="https://x.example/a.png"')
  })

  it('leaves a body with no blocked images untouched', () => {
    expect(revealBlockedImages('<p>hi</p>')).toBe('<p>hi</p>')
  })

  it('output is still sanitised afterwards', () => {
    // Revealing runs before sanitising, so a hostile URL cannot slip in through consent.
    const revealed = revealBlockedImages('<img data-blocked-src="javascript:alert(1)">')

    expect(sanitizeBody(revealed)).not.toContain('javascript:')
  })
})
