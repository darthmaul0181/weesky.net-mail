import { describe, it, expect } from 'vitest'
import {
  BUTTONS, contrast, customPaletteCss, formatCustomPalette, generateCustomPalette, INTENSITIES,
  isNearDanger, parseCustomPalette, PALETTE_SEEDS, type CustomPaletteDef, type TokenSet,
} from './customPalette'
import nightCss from '../styles/theme-night.css?raw'

function rolesIn(selector: string): string[] {
  const at = nightCss.indexOf(`${selector} {`)
  const body = nightCss.slice(at, nightCss.indexOf('}', at)).replace(/\/\*[\s\S]*?\*\//g, '')
  return [...body.matchAll(/(--[\w-]+)\s*:/g)].map(m => m[1]!).sort()
}

describe('parseCustomPalette / formatCustomPalette', () => {
  it.each([
    ['265,muted,35,structure', { structure: 265, intensity: 'muted', accent: 35, buttons: 'structure' }],
    ['0,neutral,359,accent', { structure: 0, intensity: 'neutral', accent: 359, buttons: 'accent' }],
  ])('reads %s', (value, expected) => {
    expect(parseCustomPalette(value)).toEqual(expected)
    expect(formatCustomPalette(expected as CustomPaletteDef)).toBe(value)
  })

  // Saved before the buttons choice existed: it keeps what it always showed.
  it('reads a three-field palette as structure buttons', () => {
    expect(parseCustomPalette('265,muted,35')).toEqual({ structure: 265, intensity: 'muted', accent: 35, buttons: 'structure' })
  })

  it.each(['', null, undefined, '360,muted,35', '065,muted,35', '265,loud,35', '265,muted', ' 265,muted,35',
    '265,muted,35,both', '265,muted,35,accent,'])(
    'refuses %s', value => expect(parseCustomPalette(value)).toBeNull(),
  )
})

const toLinear = (v: number) => v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4
const toGamma = (v: number) => v <= 0.0031308 ? 12.92 * v : 1.055 * v ** (1 / 2.4) - 0.055

/** `color-mix(in oklab, a t, b)`: the shared status-text rule (index.css) mixes the tone 55% with --text. */
function mixOklab(a: string, b: string, t: number): string {
  const lab = (hex: string) => {
    const [r, g, bl] = [1, 3, 5].map(i => toLinear(parseInt(hex.slice(i, i + 2), 16) / 255)) as [number, number, number]
    const l = Math.cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * bl)
    const m = Math.cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * bl)
    const s = Math.cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * bl)
    return [0.2104542553 * l + 0.793617785 * m - 0.0040720468 * s, 1.9779984951 * l - 2.428592205 * m + 0.4505937099 * s,
      0.0259040371 * l + 0.7827717662 * m - 0.808675766 * s]
  }
  const A = lab(a), B = lab(b)
  const [L, A1, B1] = A.map((v, i) => v * t + B[i]! * (1 - t)) as [number, number, number]
  const l = (L + 0.3963377774 * A1 + 0.2158037573 * B1) ** 3
  const m = (L - 0.1055613458 * A1 - 0.0638541728 * B1) ** 3
  const s = (L - 0.0894841775 * A1 - 1.291485548 * B1) ** 3
  const rgb = [4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s, -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
    -0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s]
  return '#' + rgb.map(v => Math.round(toGamma(Math.min(1, Math.max(0, v))) * 255).toString(16).padStart(2, '0')).join('')
}

describe('generateCustomPalette', () => {
  const sample = generateCustomPalette({ structure: 265, intensity: 'muted', accent: 35, buttons: 'structure' })

  // A role missing here falls back to whatever the cascade holds, silently.
  it('declares exactly the roles night declares, in both modes', () => {
    expect(rolesIn("[data-palette='night']")).toHaveLength(43)
    expect(Object.keys(sample.light).sort()).toEqual(rolesIn("[data-palette='night']"))
    expect(Object.keys(sample.dark).sort()).toEqual(rolesIn("[data-palette='night'][data-theme='dark']"))
  })

  it('only writes six-digit hex colours, rgba for the scrim', () => {
    for (const set of [sample.light, sample.dark])
      for (const [role, value] of Object.entries(set))
        expect(value, role).toMatch(role === '--scrim' ? /^rgba\(/ : /^#[0-9a-f]{6}$/)
  })

  it('is deterministic', () => {
    expect(generateCustomPalette({ structure: 265, intensity: 'muted', accent: 35, buttons: 'structure' })).toEqual(sample)
  })

  // Every combination the sliders can reach, at 5° steps: the guarantee is the product.
  it('keeps every pair legible for every combination', () => {
    const failures: string[] = []
    const need = (ok: boolean, label: string) => { if (!ok) failures.push(label) }
    for (const buttons of BUTTONS)
    for (const intensity of INTENSITIES)
      for (let structure = 0; structure < 360; structure += 5)
        for (let accent = 0; accent < 360; accent += 5) {
          const { light, dark } = generateCustomPalette({ structure, intensity, accent, buttons })
          const id = `${structure},${intensity},${accent},${buttons}`
          for (const [mode, t] of [['light', light], ['dark', dark]] as [string, TokenSet][]) {
            const at = (a: `--${string}`, b: `--${string}`, min: number) =>
              need(contrast(t[a]!, t[b]!) >= min, `${id} ${mode} ${a}/${b}`)
            at('--text', '--surface', 4.5)
            at('--text', '--bg', 4.5)
            for (const ground of ['--surface', '--folders-bg', '--folders-item-hover', '--surface-sunken'] as const)
              at('--text-muted', ground, 4.5)
            at('--quote-text', '--surface', 4.5)
            at('--topbar-fg', '--topbar-bg', mode === 'light' ? 7 : 4.5)
            at('--rail-fg', '--rail-bg', 4.5)
            at('--rail-item-active-fg', '--rail-item-active', 4.5)
            at('--badge-count-fg', '--badge-count-bg', 4.5)
            at('--list-row-selected-fg', '--list-row-selected-bg', 4.5)
            at('--pane-item-active-fg', '--pane-item-active-bg', 4.5)
            at('--accent-unread', '--surface', 3)
            at('--action-primary-fg', '--action-primary', mode === 'light' ? 4.5 : 2.8)
            if (mode === 'dark')
              for (const fill of ['--danger', '--danger-hover', '--success'] as const) at('--status-fg', fill, 4.5)
            for (const tone of ['--danger', '--success', '--warning'] as const) {
              const text = mixOklab(t[tone]!, t['--text']!, 0.55)
              for (const ground of ['--surface', '--folders-bg', '--surface-sunken', '--list-row-hover', '--list-row-selected-bg'] as const)
                need(contrast(text, t[ground]!) >= 4.5, `${id} ${mode} status ${tone} on ${ground}`)
            }
          }
        }
    expect(failures.slice(0, 10)).toEqual([])
  }, 60_000)

  // The choice only moves the light buttons: dark ones wear the accent either way.
  it('dresses the light buttons in the colour asked for', () => {
    const accent = generateCustomPalette({ structure: 265, intensity: 'muted', accent: 35, buttons: 'accent' })
    expect(sample.light['--action-primary']).toBe(sample.light['--topbar-bg'])
    expect(accent.light['--action-primary']).toBe(accent.light['--accent-unread'])
    expect(accent.light['--action-primary-hover']).not.toBe(sample.light['--action-primary-hover'])
    expect(accent.dark).toEqual(sample.dark)
  })

  it('keeps white on the primary action in both modes', () => {
    expect(sample.light['--action-primary-fg']).toBe('#ffffff')
    expect(sample.dark['--action-primary-fg']).toBe('#ffffff')
  })
})

describe('isNearDanger', () => {
  it.each([[27, true], [20, true], [34, true], [35, false], [19, false], [200, false]])(
    'accent %i → %s', (accent, expected) => expect(isNearDanger(accent)).toBe(expected),
  )
})

describe('customPaletteCss', () => {
  it('writes two unanchored blocks', () => {
    const css = customPaletteCss(generateCustomPalette({ structure: 10, intensity: 'vivid', accent: 200, buttons: 'accent' }))
    expect(css).toMatch(/^\[data-palette='custom'\] \{/)
    expect(css).toContain("[data-palette='custom'][data-theme='dark'] {")
    expect(css).not.toContain('html[')
  })
})

describe('PALETTE_SEEDS', () => {
  // Slate is the one built-in palette whose light buttons already wear its accent.
  it('seeds Slate with accent buttons, the others with structure', () => {
    expect(PALETTE_SEEDS.slate.buttons).toBe('accent')
    expect(Object.entries(PALETTE_SEEDS).filter(([, seed]) => seed.buttons === 'accent').map(([id]) => id)).toEqual(['slate'])
  })

  it('seeds the editor from every built-in palette', () => {
    expect(Object.keys(PALETTE_SEEDS).sort())
      .toEqual(['azure', 'classic', 'forest', 'indigo', 'ink', 'night', 'plum', 'slate'])
  })
})
