import { describe, it, expect } from 'vitest'
import { referencedCids, substituteInlineImages } from './inlineImages'

describe('referencedCids', () => {
  it('collects the cid a body references', () => {
    expect(referencedCids('<p>hi</p><img src="cid:logo@mail">')).toEqual(['logo@mail'])
  })

  it('dedupes a cid referenced more than once', () => {
    const html = '<img src="cid:a@x"><img src="cid:b@x"><img src="cid:a@x">'

    expect(referencedCids(html)).toEqual(['a@x', 'b@x'])
  })

  it('ignores remote, data and withheld srcs', () => {
    const html = '<img src="https://t.example/p.gif"><img src="data:image/png;base64,AA">'
      + '<img data-blocked-src="https://t.example/q.gif"><img>'

    expect(referencedCids(html)).toEqual([])
  })

  // The scheme is case-insensitive; a body writing CID: references the same part.
  it('accepts the scheme in any case', () => {
    expect(referencedCids('<img src="CID:Logo@Mail">')).toEqual(['Logo@Mail'])
  })

  it('ignores a bare cid: with nothing after it', () => {
    expect(referencedCids('<img src="cid:">')).toEqual([])
  })

  it('answers nothing for an empty body', () => {
    expect(referencedCids('')).toEqual([])
  })

  // The whole reason for the DOM round-trip: the html carries &amp; where the attribute value
  // holds &, so a string match on the raw markup would look for the wrong id.
  it('reads a cid containing an ampersand as the attribute value, not as markup', () => {
    expect(referencedCids('<img src="cid:a&amp;b@x">')).toEqual(['a&b@x'])
  })
})

describe('substituteInlineImages', () => {
  const uri = 'data:image/png;base64,AAAA'

  it('replaces a mapped cid with its data URI', () => {
    const html = substituteInlineImages('<img src="cid:logo@mail">', { 'logo@mail': uri })

    expect(html).toContain(`src="${uri}"`)
    expect(html).not.toContain('cid:')
  })

  it('leaves an unmapped cid untouched', () => {
    const html = substituteInlineImages(
      '<img src="cid:a@x"><img src="cid:b@x">', { 'a@x': uri })

    expect(html).toContain(`src="${uri}"`)
    expect(html).toContain('src="cid:b@x"')
  })

  it('leaves a remote src alone', () => {
    const html = '<img src="https://t.example/p.gif">'

    expect(substituteInlineImages(html, { 'logo@mail': uri })).toBe(html)
  })

  // Same reason as the collection: the map is keyed by the decoded value.
  it('substitutes a cid containing an ampersand', () => {
    const html = substituteInlineImages('<img src="cid:a&amp;b@x">', { 'a&b@x': uri })

    expect(html).toContain(`src="${uri}"`)
    expect(html).not.toContain('cid:')
  })

  it('returns the body unchanged when there is nothing to inline', () => {
    const html = '<p>Bonjour</p><img data-blocked-src="https://t.example/p.gif">'

    expect(substituteInlineImages(html, {})).toBe(html)
  })

  // A cid named after an Object.prototype member must not resolve to a function. The map is
  // deliberately non-empty, so the substitution really walks the document.
  it('ignores inherited properties of the map', () => {
    const html = substituteInlineImages(
      '<img src="cid:constructor"><img src="cid:a@x">', { 'a@x': uri })

    expect(html).toContain('src="cid:constructor"')
    expect(html).toContain(`src="${uri}"`)
  })

  it('answers an empty string for an empty body', () => {
    expect(substituteInlineImages('', { 'logo@mail': uri })).toBe('')
  })
})
