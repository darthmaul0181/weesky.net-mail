import { describe, it, expect } from 'vitest'
import { htmlToText, losesFormatting, textToHtml } from './bodyFormat'

describe('htmlToText', () => {
  it('breaks blocks and <br> into lines', () => {
    expect(htmlToText('<div>one</div><div>two</div>')).toBe('one\ntwo')
    expect(htmlToText('<div>one<br>two</div>')).toBe('one\ntwo')
  })

  it('collapses the whitespace markup carries', () => {
    expect(htmlToText('<div>  one   two  </div>')).toBe('one two')
  })

  it('keeps an empty line a double <br> asked for', () => {
    expect(htmlToText('<div>one<br><br>two</div>')).toBe('one\n\ntwo')
  })

  it('prefixes a quote, one level per blockquote', () => {
    expect(htmlToText('<blockquote><div>said</div></blockquote>')).toBe('> said')
    expect(htmlToText('<blockquote><blockquote><div>older</div></blockquote></blockquote>'))
      .toBe('>> older')
  })

  it('quotes only what is inside the blockquote', () => {
    expect(htmlToText('<div>mine</div><blockquote><div>theirs</div></blockquote>'))
      .toBe('mine\n> theirs')
  })

  it('unescapes entities the markup carried', () => {
    expect(htmlToText('<div>a &amp; b &lt;c&gt;</div>')).toBe('a & b <c>')
  })

  it('drops the empty lines a wrapper adds at either end', () => {
    expect(htmlToText('<div><br></div><div><br></div><div>text</div>')).toBe('text')
  })

  it('answers an empty string on an empty body', () => {
    expect(htmlToText('')).toBe('')
  })

  // Deeper than any hand-written body, shallower than the parser's own cap: jsdom overflows
  // building the tree long before the walk could, so the stack guard cannot be exercised here.
  it('unwinds a deeply nested body', () => {
    const deep = '<div>'.repeat(400) + 'bottom' + '</div>'.repeat(400)
    expect(htmlToText(deep)).toBe('bottom')
  })
})

describe('textToHtml', () => {
  it('escapes and renders the line structure', () => {
    expect(textToHtml('a <b>\nc')).toBe('<div>a &lt;b&gt;<br>c</div>')
  })

  it('round-trips through htmlToText', () => {
    expect(htmlToText(textToHtml('one\ntwo\n\nthree'))).toBe('one\ntwo\n\nthree')
  })
})

describe('losesFormatting', () => {
  it('is false on plain paragraphs and on nothing at all', () => {
    expect(losesFormatting('')).toBe(false)
    expect(losesFormatting('<div>one</div><div>two<br>three</div>')).toBe(false)
  })

  // A quote survives as '>' lines, so a reply to a plain original must not be interrogated.
  it('is false on a quote carrying no formatting of its own', () => {
    expect(losesFormatting('<div>hi</div><blockquote><div>said</div></blockquote>')).toBe(false)
  })

  it.each([
    ['<div><b>bold</b></div>'],
    ['<div><ul><li>a</li></ul></div>'],
    ['<div><a href="https://x.test">link</a></div>'],
    ['<div><img src="cid:x"></div>'],
    ['<div style="color:#f00">red</div>'],
  ])('is true on %s', (html) => {
    expect(losesFormatting(html)).toBe(true)
  })
})
