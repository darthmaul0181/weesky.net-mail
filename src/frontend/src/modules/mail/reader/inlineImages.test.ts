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

  it('reports a cid referenced from a css background', () => {
    const html = '<div style="background-image: url(cid:logo@mail)"></div>'

    expect(referencedCids(html)).toEqual(['logo@mail'])
  })

  it('reports a quoted cid background', () => {
    const html = `<div style="background-image: url('cid:logo@mail')"></div>`

    expect(referencedCids(html)).toEqual(['logo@mail'])
  })

  it('deduplicates a cid referenced by both an img and a background', () => {
    const html = '<img src="cid:logo@mail"><div style="background-image: url(cid:logo@mail)"></div>'

    expect(referencedCids(html)).toEqual(['logo@mail'])
  })

  // Same reason as the img case: a body writing CID: (or Cid:) references the same part.
  it('accepts the scheme in any case from a css background', () => {
    const html = '<div style="background-image: url(CID:Logo@Mail)"></div>'

    expect(referencedCids(html)).toEqual(['Logo@Mail'])
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

  it('substitutes a cid background with the data uri', () => {
    const html = '<div style="background-size: contain; background-image: url(cid:logo@mail)"></div>'

    const result = substituteInlineImages(html, { 'logo@mail': 'data:image/png;base64,AAA' })
    // The serialised string legitimately carries &quot; (a literal " inside a double-quoted
    // HTML attribute must be entity-escaped); re-parse to assert on the decoded style instead.
    const div = new DOMParser().parseFromString(result, 'text/html').querySelector('div')!

    expect(div.style.backgroundImage).toBe('url("data:image/png;base64,AAA")')
    expect(div.style.backgroundSize).toBe('contain')
    expect(result).not.toContain('cid:')
  })

  it('leaves a background whose cid the map does not carry', () => {
    const html = '<div style="background-image: url(cid:missing@mail)"></div>'

    expect(substituteInlineImages(html, { 'other@mail': 'data:image/png;base64,AAA' })).toBe(html)
  })

  // Regression: the `style.includes('cid:')` fast-exit guard was case-sensitive while CSS_CID
  // (and referencedCids) are not, so a mixed-case scheme was collected but never substituted.
  it('substitutes a mixed-case cid scheme in a background', () => {
    const html = '<div style="background-image: url(CID:logo@mail)"></div>'

    const result = substituteInlineImages(html, { 'logo@mail': 'data:image/png;base64,AAA' })
    const div = new DOMParser().parseFromString(result, 'text/html').querySelector('div')!

    expect(div.style.backgroundImage).toBe('url("data:image/png;base64,AAA")')
    expect(result).not.toContain('cid:')
    expect(result).not.toContain('CID:')
  })

  // Same guard as the img path, now exercised on the background: neither an Object.prototype
  // member name nor the prototype pointer itself may resolve to a function through the map.
  it('ignores inherited properties of the map for a background', () => {
    const html = substituteInlineImages(
      '<div style="background-image: url(cid:constructor)"></div>'
      + '<div style="background-image: url(cid:__proto__)"></div>'
      + '<div style="background-image: url(cid:a@x)"></div>',
      { 'a@x': uri },
    )
    const divs = new DOMParser().parseFromString(html, 'text/html').querySelectorAll('div')

    expect(divs[0].style.backgroundImage).toBe('url("cid:constructor")')
    expect(divs[1].style.backgroundImage).toBe('url("cid:__proto__")')
    expect(divs[2].style.backgroundImage).toBe(`url("${uri}")`)
  })

  // A quote or backslash in the data URI must not break out of the CSS url("...") string —
  // the same encoding sanitizeBody.ts applies on its own write into a style attribute.
  it('escapes a quote in the data uri before writing it into css', () => {
    const hostile = 'data:image/png;base64,AA");background-image:url(https://evil.example/beacon.png);x:url("'
    const html = '<div style="background-image: url(cid:logo@mail)"></div>'

    const result = substituteInlineImages(html, { 'logo@mail': hostile })
    const div = new DOMParser().parseFromString(result, 'text/html').querySelector('div')!

    // Pins the exact escaped form: an unescaped quote would close the url("...") string early,
    // turning the rest into a second, overriding background-image declaration that points at
    // evil.example instead of the data URI — this exact-string check catches that regression.
    expect(div.style.backgroundImage).toBe(`url("${hostile.replace(/["\\]/g, encodeURIComponent)}")`)
  })
})
