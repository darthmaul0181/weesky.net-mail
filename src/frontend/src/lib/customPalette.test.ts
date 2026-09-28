import { describe, it, expect } from 'vitest'
import {
  contrast, customPaletteCss, formatCustomPalette, generateCustomPalette, INTENSITIES,
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
    ['265,muted,35', { structure: 265, intensity: 'muted', accent: 35 }],
    ['0,neutral,359', { structure: 0, intensity: 'neutral', accent: 359 }],
  ])('reads %s', (value, expected) => {
    expect(parseCustomPalette(value)).toEqual(expected)
    expect(formatCustomPalette(expected as CustomPaletteDef)).toBe(value)
  })

  it.each(['', null, undefined, '360,muted,35', '065,muted,35', '265,loud,35', '265,muted', ' 265,muted,35'])(
    'refuses %s', value => expect(parseCustomPalette(value)).toBeNull(),
  )
})

describe('generateCustomPalette', () => {
  const sample = generateCustomPalette({ structure: 265, intensity: 'muted', accent: 35 })

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
    expect(generateCustomPalette({ structure: 265, intensity: 'muted', accent: 35 })).toEqual(sample)
  })

  // Every combination the sliders can reach, at 5° steps: the guarantee is the product.
  it('keeps every pair legible for every combination', () => {
    const failures: string[] = []
    const need = (ok: boolean, label: string) => { if (!ok) failures.push(label) }
    for (const intensity of INTENSITIES)
      for (let structure = 0; structure < 360; structure += 5)
        for (let accent = 0; accent < 360; accent += 5) {
          const { light, dark } = generateCustomPalette({ structure, intensity, accent })
          const id = `${structure},${intensity},${accent}`
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
          }
        }
    expect(failures.slice(0, 10)).toEqual([])
  }, 60_000)

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
    const css = customPaletteCss(generateCustomPalette({ structure: 10, intensity: 'vivid', accent: 200 }))
    expect(css).toMatch(/^\[data-palette='custom'\] \{/)
    expect(css).toContain("[data-palette='custom'][data-theme='dark'] {")
    expect(css).not.toContain('html[')
  })
})

describe('PALETTE_SEEDS', () => {
  it('seeds the editor from every built-in palette', () => {
    expect(Object.keys(PALETTE_SEEDS).sort())
      .toEqual(['azure', 'classic', 'forest', 'indigo', 'ink', 'night', 'plum', 'slate'])
  })
})
