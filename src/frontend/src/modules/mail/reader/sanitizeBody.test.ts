import { describe, it, expect } from 'vitest'
import { renderBodyDocument, revealBlockedImages, sanitizeBody } from './sanitizeBody'

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

describe('renderBodyDocument', () => {
  it('carries the sanitised body through unchanged', () => {
    expect(renderBodyDocument('<p>Bonjour</p>')).toContain('<p>Bonjour</p>')
  })

  // The bare fragment inherited the browser's 8px body margin, which left the message text
  // visibly out of line with the header above it. The padding here matches .reader-header.
  it('pads the body to line up with the header above it', () => {
    expect(renderBodyDocument('<p>x</p>')).toMatch(/padding:\s*18px 22px/)
  })

  it('keeps a long unbroken URL from scrolling the body sideways', () => {
    expect(renderBodyDocument('<p>x</p>')).toContain('overflow-wrap: anywhere')
  })

  it('constrains an oversized image to the reader width', () => {
    expect(renderBodyDocument('<p>x</p>')).toMatch(/img\s*\{[^}]*max-width:\s*100%/)
  })

  // Mail HTML is written against a white canvas; inverting it would make a body's own colours
  // unreadable. The sheet stays light whatever the app's theme.
  it('pins the body to a light sheet in every theme', () => {
    const document = renderBodyDocument('<p>x</p>')

    expect(document).toContain('color-scheme: light')
    expect(document).toMatch(/background:\s*#ffffff/)
  })

  it('grants the body no capability it did not already have', () => {
    const document = renderBodyDocument(sanitizeBody('<script>alert(1)</script><p>hi</p>'))

    expect(document).not.toContain('<script')
    expect(document).toContain('hi')
  })
})
