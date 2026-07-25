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
    expect(renderBodyDocument('<p>x</p>')).toMatch(/overflow-wrap:\s*break-word/)
  })

  // `anywhere` breaks the same words, but unlike `break-word` it also feeds those break points
  // into min-content sizing — so a table column could shrink to one letter, and a GitHub mail
  // rendered its "Status" heading vertically with the icons squashed to slivers.
  it('never uses the value that collapses table columns', () => {
    expect(renderBodyDocument('<p>x</p>')).not.toContain('overflow-wrap: anywhere')
  })

  it('constrains an oversized image to the reader width', () => {
    expect(renderBodyDocument('<p>x</p>')).toMatch(/img\s*\{[^}]*max-width:\s*100%/)
  })

  // height:auto recomputed every image from its intrinsic ratio: a 1x1 spacer gif stretched to
  // 154x10 by attributes became 154px tall, turning a newsletter button into a tower.
  it('leaves image height attributes alone', () => {
    expect(renderBodyDocument('<p>x</p>')).not.toContain('height: auto')
  })

  describe('dark mode', () => {
    // Colours are recoloured in the markup by darkenColours, not by a CSS filter: a filter
    // inverts in RGB, which drags hues across the wheel and takes photographs with it.
    it('applies no filter of its own', () => {
      expect(renderBodyDocument('<p>x</p>', { dark: true })).not.toContain('filter:')
    })

    // What the message does not declare has to come from somewhere, and in dark mode that
    // somewhere cannot be the white sheet.
    it("gives the sheet the app's dark surface and a light default text", () => {
      const document = renderBodyDocument('<p>x</p>', { dark: true })

      expect(document).toMatch(/html\s*\{[^}]*background:\s*#212429/)
      expect(document).toMatch(/color:\s*#e0e0e0/)
    })

    it('tells the browser the sheet is dark', () => {
      expect(renderBodyDocument('<p>x</p>', { dark: true })).toContain('color-scheme: dark')
    })

    it('leaves the document light when dark is off', () => {
      const document = renderBodyDocument('<p>x</p>')

      expect(document).toContain('color-scheme: light')
      expect(document).toMatch(/background:\s*#ffffff/)
    })
  })

  it('grants the body no capability it did not already have', () => {
    const document = renderBodyDocument(sanitizeBody('<script>alert(1)</script><p>hi</p>'))

    expect(document).not.toContain('<script')
    expect(document).toContain('hi')
  })
})
