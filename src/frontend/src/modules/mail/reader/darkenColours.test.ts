import { describe, it, expect } from 'vitest'
import { darkenColours, toDarkColour } from './darkenColours'

describe('toDarkColour', () => {
  // Lightness is inverted; hue and saturation are left alone. That is what keeps a red button
  // red instead of turning it cyan, which is where filter-based inversion goes wrong.
  // White is asked for as a background here, and black as text, because only the half that needs
  // flipping is flipped: the other half is already suited to dark mode and stays put.
  it('turns white into the app\'s dark surface, not pure black', () => {
    expect(toDarkColour('#ffffff', 'background')).toBe('#212121')
  })

  it('turns black into a light grey, not pure white', () => {
    expect(toDarkColour('#000000')).toBe('#e0e0e0')
  })

  it('leaves a mid-lightness colour about where it was', () => {
    expect(toDarkColour('rgb(128, 128, 128)')).toBe('#808080')
  })

  // Comparing the hue in against the hue out, rather than a number written by hand: the point
  // is that the colour is still itself, whatever its hue happens to be.
  it.each([
    ['#e4003a', 'bpost red'],
    ['#ffd814', 'Amazon yellow'],
    ['#2e7d32', 'a green'],
    ['#d8d4f5', 'a pale purple'],
  ])('keeps the hue of %s (%s)', colour => {
    const before = hueOf(...rgbOf(colour))
    const after = hueOf(...rgbOf(toDarkColour(colour)!))

    expect(after).toBeCloseTo(before, 0)
  })

  it('keeps the alpha channel', () => {
    expect(toDarkColour('rgba(255, 255, 255, 0.5)', 'background')).toBe('rgba(33, 33, 33, 0.5)')
  })

  it.each(['#fff', 'rgb(255,255,255)', 'RGB(255, 255, 255)'])('parses %s', colour => {
    expect(toDarkColour(colour, 'background')).toBe('#212121')
  })

  it.each(['transparent', 'inherit', 'currentColor', 'not-a-colour', ''])(
    'leaves %s alone rather than guessing', value => {
      expect(toDarkColour(value)).toBeNull()
    })
})

describe('darkenColours', () => {
  it('rewrites a colour inside a style attribute', () => {
    const result = darkenColours('<div style="background-color: #ffffff; padding: 4px">x</div>')

    expect(result).toContain('#212121')
    expect(result).toContain('padding: 4px')
  })

  it('rewrites the presentational colour attributes', () => {
    const result = darkenColours('<table><tr><td bgcolor="#ffffff">x</td></tr></table>')

    expect(result).toMatch(/bgcolor="#212121"/)
  })

  // A photograph is content, and Dark Reader leaves images alone for the same reason: there is
  // no way to recolour one without lying about what it shows.
  it('never touches an image', () => {
    const html = '<img src="https://x.example/a.png" width="10">'

    expect(darkenColours(html)).toContain('https://x.example/a.png')
  })

  it('leaves a declaration carrying no colour alone', () => {
    expect(darkenColours('<div style="font-size: 14px">x</div>')).toContain('font-size: 14px')
  })

  it('carries the text through unchanged', () => {
    expect(darkenColours('<p>Bonjour</p>')).toContain('Bonjour')
  })

  // A brand colour on a slab of background is what glows on a dark canvas: an Amazon button came
  // back a vivid gold. Text keeps its punch — a link that loses its colour stops reading as one.
  it('damps a saturated background but not the same colour as text', () => {
    const html = '<div style="background-color: #ffd916; color: #ffd916">x</div>'

    const dark = darkenColours(html)
    const [background, text] = [...dark.matchAll(/#[0-9a-f]{6}/gi)].map(m => m[0])

    expect(slOf(background).s).toBeLessThan(slOf(text).s)
    expect(slOf(background).l).toBeLessThan(slOf(text).l)
    expect(hueOf(...rgbOf(background))).toBeCloseTo(hueOf(...rgbOf(text)), 0)
  })

  it('treats a bgcolor attribute as a background', () => {
    const damped = toDarkColour('#ffd916', 'background')!

    const html = '<table><tbody><tr><td bgcolor="#ffd916">x</td></tr></tbody></table>'

    expect(darkenColours(html)).toContain(damped)
  })

  it('darkens a gradient stop as a background too', () => {
    const html = '<div style="background-image: linear-gradient(#ffd916, #ffd916)">x</div>'

    expect(darkenColours(html)).toContain(toDarkColour('#ffd916', 'background')!)
  })

  // The damping is a function of saturation, so a grey is untouched by it.
  it('leaves a mid grey where it already landed, background or not', () => {
    expect(toDarkColour('#808080', 'background')).toBe('#808080')
    expect(toDarkColour('#808080')).toBe('#808080')
  })

  // A colour already suited to dark mode is left alone: a section the sender drew black was drawn
  // that way on purpose, and the white logo sitting on it does not follow when we lighten it.
  it('never lightens a background', () => {
    expect(toDarkColour('#000000', 'background')).toBe('#000000')
    expect(toDarkColour('#111111', 'background')).toBe('#111111')
  })

  it('never darkens a text colour', () => {
    expect(toDarkColour('#ffffff')).toBe('#ffffff')
    expect(toDarkColour('#f7d6c8')).toBe('#f7d6c8')
  })

  // The other half of each pair still flips, which is what keeps every combination readable:
  // whatever the sender wrote, the result is a dark background under light text.
  it('still flips the half that needs it', () => {
    expect(toDarkColour('#000000')).toBe('#e0e0e0')
    expect(toDarkColour('#ffffff', 'background')).toBe('#212121')
  })

  // Damping exists to stop a bright slab from glowing. A dark slab never glowed, so it keeps its
  // own colour rather than being greyed for no reason.
  it('leaves a dark saturated background completely alone', () => {
    expect(toDarkColour('#0a1f44', 'background')).toBe('#0a1f44')
  })

  it('keeps a black section black under its white text', () => {
    const html = '<table><tbody><tr><td style="background-color: #000000; color: #ffffff">x</td></tr></tbody></table>'

    const dark = darkenColours(html)

    expect(dark).toContain('background-color: #000000')
    expect(dark).toContain('color: #ffffff')
  })

  // A gradient is a colour wearing an image's clothes. Mail tints a background image by laying a
  // flat gradient over it, and one such message came back white in dark mode because nothing here
  // looked inside background-image.
  it('darkens the colour stops of a gradient', () => {
    const html = '<div style="background-image: linear-gradient(rgba(246, 246, 246, 1), rgba(246, 246, 246, 1))">x</div>'

    const dark = darkenColours(html)

    expect(dark).toContain('linear-gradient(#282828, #282828)')
    expect(dark).not.toContain('246')
  })

  it('darkens every stop of a multi-stop gradient', () => {
    const html = '<div style="background-image: linear-gradient(to right, #ffffff 0%, #000000 100%)">x</div>'

    const dark = darkenColours(html)

    // A stop is a background: white flips to the dark surface, black is already suited and stays.
    expect(dark).toContain('#212121 0%')
    expect(dark).toContain('#000000 100%')
    expect(dark).toContain('to right')
  })

  it.each([
    'repeating-linear-gradient',
    'radial-gradient',
    'conic-gradient',
  ])('darkens the stops of a %s too', name => {
    const html = `<div style="background-image: ${name}(#ffffff, #ffffff)">x</div>`

    expect(darkenColours(html)).toContain(`${name}(#212121, #212121)`)
  })

  // The one thing this must never touch: a URL is not a colour, and a path can spell one.
  it('leaves a url in the same value alone, hex in its path included', () => {
    const html = '<div style="background-image: linear-gradient(#ffffff, #ffffff), url(&quot;https://x.test/a-ffffff.png&quot;)">x</div>'

    const dark = darkenColours(html)

    expect(dark).toContain('https://x.test/a-ffffff.png')
    expect(dark).toContain('linear-gradient(#212121, #212121)')
  })

  it('leaves a photograph background alone', () => {
    const html = '<div style="background-image: url(&quot;https://x.test/photo.jpg&quot;)">x</div>'

    expect(darkenColours(html)).toBe(html)
  })

  it('leaves a stop it cannot read alone', () => {
    const html = '<div style="background-image: linear-gradient(transparent, currentColor)">x</div>'

    expect(darkenColours(html)).toBe(html)
  })

  it('returns empty for an empty body', () => {
    expect(darkenColours('')).toBe('')
  })
})

/** Saturation and lightness in HSL, for asserting how far a colour was damped. */
function slOf(colour: string): { s: number; l: number } {
  const [r, g, b] = rgbOf(colour).map(c => c / 255)
  const max = Math.max(r, g, b)
  const min = Math.min(r, g, b)
  const l = (max + min) / 2
  if (max === min) return { s: 0, l }

  const d = max - min
  return { s: l > 0.5 ? d / (2 - max - min) : d / (max + min), l }
}

/** Reads a hex or rgb() string back into channels, for the hue assertions. */
function rgbOf(colour: string): [number, number, number] {
  if (colour.startsWith('#')) {
    const d = colour.slice(1).match(/../g)!.map(h => parseInt(h, 16))
    return [d[0], d[1], d[2]]
  }
  const [r, g, b] = colour.match(/\d+/g)!.map(Number)
  return [r, g, b]
}

/** Hue in degrees, for asserting that a transformed colour is still the same colour. */
function hueOf(r: number, g: number, b: number): number {
  const [rd, gd, bd] = [r / 255, g / 255, b / 255]
  const max = Math.max(rd, gd, bd)
  const min = Math.min(rd, gd, bd)
  if (max === min) return 0

  const d = max - min
  const h = max === rd ? ((gd - bd) / d) % 6 : max === gd ? (bd - rd) / d + 2 : (rd - gd) / d + 4
  return (h * 60 + 360) % 360
}
