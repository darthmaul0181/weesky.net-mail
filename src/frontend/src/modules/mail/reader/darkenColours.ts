/**
 * Recolours a message for dark mode by inverting each colour's *lightness* and leaving its hue
 * and saturation alone — the approach Dark Reader takes.
 *
 * A CSS `filter: invert()` was tried first and is the cheap approximation of this. It inverts
 * in RGB, which drags hues across the wheel: a red button came back cyan until a hue-rotate was
 * bolted on, images had to be inverted a second time to survive, and every white became pure
 * black. Working on the colours themselves keeps a red button red, an Amazon yellow yellow, and
 * never touches a photograph at all.
 *
 * Runs before sanitising, so everything it writes is subject to the same pass as the rest.
 */

/** Attributes that carry a colour rather than a URL or a measurement. */
const COLOUR_ATTRIBUTES = ['bgcolor', 'color', 'bordercolor']

/** Declarations whose value is a colour. `background` is excluded: it is a shorthand. */
const COLOUR_PROPERTIES = /(^|[\s;])(color|background-color|border(-[a-z]+)?-color|outline-color)\s*:\s*([^;]+)/gi

/**
 * A gradient is a colour wearing an image's clothes, and mail tints a background image by laying
 * a flat one over it — so a message can paint itself white through a property no colour rule
 * would ever look at.
 */
const IMAGE_PROPERTY = /(^|[\s;])(background-image)\s*:\s*([^;]+)/gi

/**
 * A url() or a colour, url() first: matching it consumes the whole function, so a path that
 * spells a colour can never be read as one.
 */
const URL_OR_COLOUR = /url\((?:"[^"]*"|'[^']*'|[^)]*)\)|#[\da-f]{3,8}\b|rgba?\([^)]*\)/gi

// White lands here rather than on #000, and black lands on the top rather than #fff: the app's
// own dark surface sits around 13% lightness, and a pure black slab beside it reads as a hole.
const DARKEST = 0.13
const LIGHTEST = 0.88

/**
 * A slab of brand colour is what glows on a dark canvas — an Amazon button came back a vivid
 * gold. A background therefore keeps only part of its saturation and lands under a lower ceiling,
 * both in proportion to how saturated it was: a grey is untouched, so ordinary mail does not move.
 * Text is left at full strength, since a link that loses its colour stops reading as one.
 */
const BACKGROUND_SATURATION = 0.6
const SATURATED_LIGHTEST = 0.55

export type ColourRole = 'text' | 'background'

/**
 * The dark-mode equivalent of a single colour, or null when the value is not one we can read —
 * `transparent`, `inherit`, a keyword. Null means "leave it alone", never "guess".
 */
export function toDarkColour(value: string, role: ColourRole = 'text'): string | null {
  const rgba = parse(value.trim())
  if (!rgba) return null

  const [r, g, b, a] = rgba
  const { h, s, l } = toHsl(r, g, b)
  const background = role === 'background'
  const ceiling = background ? LIGHTEST - s * (LIGHTEST - SATURATED_LIGHTEST) : LIGHTEST
  const { r: nr, g: ng, b: nb } = toRgb(
    h, background ? s * BACKGROUND_SATURATION : s, DARKEST + (1 - l) * (ceiling - DARKEST))

  // Hex, not rgb(): a presentational attribute like bgcolor does not understand rgb() and the
  // browser falls back to its legacy colour parsing, which keeps the hex digits out of
  // "rgb(84, 22, 0)" and invents a colour from them — a brown background came out green.
  return a === 1 ? `#${hex(nr)}${hex(ng)}${hex(nb)}` : `rgba(${nr}, ${ng}, ${nb}, ${a})`
}

/** Rewrites every colour a fragment declares, in style attributes and in colour attributes. */
export function darkenColours(html: string): string {
  if (!html) return ''

  const document = new DOMParser().parseFromString(html, 'text/html')

  for (const element of document.body.querySelectorAll<HTMLElement>('[style]')) {
    const style = element.getAttribute('style')!
    element.setAttribute('style', style
      .replace(COLOUR_PROPERTIES, (whole, lead, property, _side, value) => {
        const dark = toDarkColour(value, roleOf(property))
        return dark ? `${lead}${property}: ${dark}` : whole
      })
      .replace(IMAGE_PROPERTY, (_whole, lead, property, value) =>
        `${lead}${property}: ${darkenImageColours(value)}`))
  }

  for (const attribute of COLOUR_ATTRIBUTES) {
    for (const element of document.body.querySelectorAll(`[${attribute}]`)) {
      const dark = toDarkColour(element.getAttribute(attribute)!, roleOf(attribute))
      if (dark) element.setAttribute(attribute, dark)
    }
  }

  return document.body.innerHTML
}

/** `background-color` and `bgcolor` paint a slab; everything else here paints text or a hairline. */
function roleOf(name: string): ColourRole {
  return /^(background-color|bgcolor)$/i.test(name) ? 'background' : 'text'
}

/** Gradient stops darkened; a url() and anything unreadable are left exactly as they are. */
function darkenImageColours(value: string): string {
  return value.replace(URL_OR_COLOUR, token =>
    /^url\(/i.test(token) ? token : toDarkColour(token, 'background') ?? token)
}

function hex(channel: number): string {
  return channel.toString(16).padStart(2, '0')
}

function parse(value: string): [number, number, number, number] | null {
  const hex = /^#([\da-f]{3}|[\da-f]{6})$/i.exec(value)
  if (hex) {
    const digits = hex[1].length === 3 ? [...hex[1]].map(d => d + d) : hex[1].match(/../g)!
    return [...digits.map(d => parseInt(d, 16)), 1] as [number, number, number, number]
  }

  const fn = /^rgba?\(\s*([\d.]+)[\s,]+([\d.]+)[\s,]+([\d.]+)(?:[\s,/]+([\d.]+))?\s*\)$/i.exec(value)
  if (!fn) return null

  return [Number(fn[1]), Number(fn[2]), Number(fn[3]), fn[4] === undefined ? 1 : Number(fn[4])]
}

function toHsl(r: number, g: number, b: number): { h: number; s: number; l: number } {
  const [rd, gd, bd] = [r / 255, g / 255, b / 255]
  const max = Math.max(rd, gd, bd)
  const min = Math.min(rd, gd, bd)
  const l = (max + min) / 2

  if (max === min) return { h: 0, s: 0, l }

  const d = max - min
  const s = l > 0.5 ? d / (2 - max - min) : d / (max + min)
  const h = max === rd ? ((gd - bd) / d) % 6 : max === gd ? (bd - rd) / d + 2 : (rd - gd) / d + 4

  return { h: ((h * 60) + 360) % 360, s, l }
}

function toRgb(h: number, s: number, l: number): { r: number; g: number; b: number } {
  const c = (1 - Math.abs(2 * l - 1)) * s
  const x = c * (1 - Math.abs(((h / 60) % 2) - 1))
  const m = l - c / 2
  const [r, g, b] =
    h < 60 ? [c, x, 0] : h < 120 ? [x, c, 0] : h < 180 ? [0, c, x] :
    h < 240 ? [0, x, c] : h < 300 ? [x, 0, c] : [c, 0, x]

  return { r: Math.round((r + m) * 255), g: Math.round((g + m) * 255), b: Math.round((b + m) * 255) }
}
