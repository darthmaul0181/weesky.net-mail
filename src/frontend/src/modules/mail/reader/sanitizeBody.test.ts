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

  it('keeps data-blocked-bg through the sanitising pass', () => {
    const html = '<div data-blocked-bg="https://cdn.example/l.png"></div>'

    expect(sanitizeBody(html)).toContain('data-blocked-bg')
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

describe('renderBodyDocument — images in dark mode', () => {
  // darkenColours cannot reach inside an image, so a pale banner blazes on the dark canvas.
  it('dims images in dark mode', () => {
    expect(renderBodyDocument('<p>x</p>', { dark: true })).toContain('brightness(0.85)')
  })

  it('leaves images alone in light mode', () => {
    expect(renderBodyDocument('<p>x</p>')).not.toContain('brightness')
  })
})

describe('revealBlockedImages — backgrounds', () => {
  // Serialised HTML spells the url()'s quotes &quot;, so the assertions read the attribute the
  // CSS parser actually sees rather than its escaped spelling.
  const revealedElement = (html: string) =>
    new DOMParser().parseFromString(revealBlockedImages(html), 'text/html')
      .body.firstElementChild as HTMLElement
  const revealedStyle = (html: string) => revealedElement(html).getAttribute('style') ?? ''

  it('appends the withheld background to the style', () => {
    const html = '<div data-blocked-bg="https://cdn.example/l.png" style="background-size: contain"></div>'

    const style = revealedStyle(html)

    expect(style).toContain('background-size: contain')
    expect(style).toContain('background-image: url("https://cdn.example/l.png")')
  })

  it('restores every layer in order', () => {
    const html = '<div data-blocked-bg="https://a.example/1.png https://b.example/2.png"></div>'

    expect(revealedStyle(html)).toContain(
      'background-image: url("https://a.example/1.png"), url("https://b.example/2.png")')
  })

  // The attribute carries no layer position, so a layer the cull left behind would be lost to
  // the appended declaration if it were not carried into it.
  it('keeps a gradient the cull left in the declaration', () => {
    const html = '<div data-blocked-bg="https://cdn.example/l.png" ' +
      'style="background-image: linear-gradient(to right, #000, #fff)"></div>'

    const style = revealedStyle(html)

    expect(style).toContain('linear-gradient')
    expect(style).toContain(', url("https://cdn.example/l.png")')
  })

  // Task 4 resolves cid backgrounds; this only has to keep the one already in the CSS in the
  // stack. Assigning through CSSOM reserialises the attribute, so the cid layer comes back
  // quoted rather than character for character — the layer survives, its spelling need not.
  it('leaves a cid layer in place and restores the remote one after it', () => {
    const html = '<div data-blocked-bg="https://evil.example/z.png" ' +
      'style="background-image: url(cid:logo@mail)"></div>'

    expect(revealedStyle(html)).toContain(
      'background-image: url("cid:logo@mail"), url("https://evil.example/z.png")')
  })

  // Blink answers the CSS-wide keyword `initial` — not '' and not 'none' — when a `background:`
  // shorthand declares no image, and a keyword cannot sit in a layer list: the engine rejects the
  // whole assignment, so consent restored nothing at all. Mail is commonly authored this way, and
  // the backend withholds only from the longhand, so the shape arrives intact. jsdom answers
  // `none` here, so the keyword itself is what the cases below pin down.
  it('restores under a background shorthand that declares no image', () => {
    const html = '<div data-blocked-bg="https://cdn.example/l.png" ' +
      'style="background: #ffffff;background-repeat: no-repeat"></div>'

    const style = revealedElement(html).style

    expect([...style]).toContain('background-image')
    expect(style.backgroundImage).toBe('url("https://cdn.example/l.png")')
  })

  it.each(['none', 'initial', 'inherit', 'unset', 'revert', 'revert-layer'])(
    'replaces a %s background-image instead of listing it as a layer', keyword => {
      const html =
        `<div data-blocked-bg="https://cdn.example/l.png" style="background-image: ${keyword}"></div>`

      expect(revealedElement(html).style.backgroundImage).toBe('url("https://cdn.example/l.png")')
    })

  // The backend already refuses these, and the client refuses them again: DOMPurify does not.
  it('restores nothing for a scheme that is not http(s)', () => {
    const html = '<div data-blocked-bg="javascript:alert(1)"></div>'

    expect(revealBlockedImages(html)).not.toContain('background-image')
  })

  // A forged URL must not be able to close the url() and append declarations of its own. The
  // forged tail is a legal path, so it survives inside the URL — what must not survive is its
  // reading as CSS. The assertion is the declaration list itself, not one property name: a
  // payload landing on content, behavior or transform would walk past a `position` check.
  it('encodes the quotes and backslashes of a forged url', () => {
    const html = '<div data-blocked-bg="https://x.example/a&quot;);position:fixed;a:url(&quot;"></div>'

    expect([...revealedElement(html).style]).toEqual(['background-image'])
    expect(revealedStyle(html)).toContain('%22')
  })

  // An open construct already in the style attribute captured the appended declaration, and a
  // `)` in the withheld URL closed it: per CSS Syntax §4.3.6 `url(` + `(` is a bad-url-token
  // consumed up to the first `)`, so everything after it parsed as fresh declarations. Neither
  // `)` nor `;` is percent-encoded by a URL parser, so only assigning through CSSOM closes it.
  it('cannot be escaped through an open construct in the style it lands on', () => {
    const html = '<div data-blocked-bg="https://x.example/a);position:fixed;z:url(" style="a: url("></div>'

    expect([...revealedElement(html).style]).toEqual(['background-image'])
  })

  it('leaves a body carrying no withheld background untouched', () => {
    const html = '<p>Bonjour</p>'

    expect(revealBlockedImages(html)).toBe(html)
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
    // Colours are recoloured in the markup by darkenColours, never by an inversion filter: that
    // inverts in RGB, which drags hues across the wheel and takes photographs with it. The image
    // dimming below is the opposite of that — it changes no hue and substitutes for nothing.
    it('inverts nothing and rotates no hue', () => {
      const document = renderBodyDocument('<p>x</p>', { dark: true })

      expect(document).not.toContain('invert(')
      expect(document).not.toContain('hue-rotate')
    })

    it('leaves everything but images unfiltered', () => {
      const document = renderBodyDocument('<p>x</p>', { dark: true })
      const filtered = [...document.matchAll(/([a-z]+)\s*\{[^}]*filter:/g)].map(match => match[1])

      expect(filtered).toEqual(['img'])
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
