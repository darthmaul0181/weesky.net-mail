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

  // height:auto recomputed every image from its intrinsic ratio: a 1x1 spacer gif stretched to
  // 154x10 by attributes became 154px tall, turning a newsletter button into a tower.
  it('leaves image height attributes alone', () => {
    expect(renderBodyDocument('<p>x</p>')).not.toContain('height: auto')
  })

  describe('dark mode', () => {
    // The inversion technique Apple Mail and Thunderbird use: invert the whole sheet, then
    // rotate the hue back so brand colours stay recognisable rather than becoming negatives.
    it('inverts the body when the reader asks for dark', () => {
      const document = renderBodyDocument('<p>x</p>', { dark: true })

      expect(document).toMatch(/filter:\s*invert\(1\)\s*hue-rotate\(180deg\)/)
    })

    // A filter on <body> leaves the canvas alone: the body's background propagates to it and is
    // painted outside the filter. Real mail came out light-grey text on white — the content
    // inverted, the sheet behind it did not. The filter has to sit on the root.
    it('inverts through the root so the canvas goes dark with the content', () => {
      const document = renderBodyDocument('<p>x</p>', { dark: true })

      expect(document).toMatch(/html\s*\{[^}]*filter:\s*invert\(1\)/)
      expect(document).not.toMatch(/body\s*\{[^}]*filter:\s*invert\(1\)/)
    })

    // Propagation only stops once html paints its own background.
    it('gives the root a background of its own', () => {
      expect(renderBodyDocument('<p>x</p>', { dark: true }))
        .toMatch(/html\s*\{[^}]*background:\s*#ffffff/)
    })

    // Inverting the sheet inverts the images with it; they need it applied twice to come back.
    it('re-inverts images so photographs stay themselves', () => {
      const document = renderBodyDocument('<p>x</p>', { dark: true })

      expect(document).toMatch(/img[^{]*\{[^}]*filter:\s*invert\(1\)\s*hue-rotate\(180deg\)/)
    })

    // The canvas must stay light for the inversion to land on white and produce black. Setting
    // it dark first would inverting to white — the bug this whole approach exists to avoid.
    it('keeps the canvas light so the inversion has something to invert', () => {
      const document = renderBodyDocument('<p>x</p>', { dark: true })

      expect(document).toContain('color-scheme: light')
      expect(document).toMatch(/background:\s*#ffffff/)
    })

    it('leaves the document untouched when dark is off', () => {
      expect(renderBodyDocument('<p>x</p>', { dark: false })).not.toContain('invert(1)')
      expect(renderBodyDocument('<p>x</p>')).not.toContain('invert(1)')
    })
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
