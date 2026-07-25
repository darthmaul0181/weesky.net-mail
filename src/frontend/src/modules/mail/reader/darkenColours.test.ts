import { describe, it, expect } from 'vitest'
import { darkenColours, toDarkColour } from './darkenColours'

describe('toDarkColour', () => {
  // Lightness is inverted; hue and saturation are left alone. That is what keeps a red button
  // red instead of turning it cyan, which is where filter-based inversion goes wrong.
  it('turns white into the app\'s dark surface, not pure black', () => {
    expect(toDarkColour('#ffffff')).toBe('#212121')
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
    expect(toDarkColour('rgba(255, 255, 255, 0.5)')).toBe('rgba(33, 33, 33, 0.5)')
  })

  it.each(['#fff', 'rgb(255,255,255)', 'RGB(255, 255, 255)'])('parses %s', colour => {
    expect(toDarkColour(colour)).toBe('#212121')
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

  it('returns empty for an empty body', () => {
    expect(darkenColours('')).toBe('')
  })
})

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
