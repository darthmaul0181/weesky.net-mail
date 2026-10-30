import { describe, it, expect } from 'vitest'
import css from '../../../../styles/mail.css?raw'
import { contrast, generateCustomPalette, INTENSITIES, type TokenSet } from '../../../../lib/customPalette'

const themes = import.meta.glob('../../../../styles/theme-*.css', { query: '?raw', import: 'default', eager: true })

function constantsOf(selector: string): Record<string, number> {
  const at = css.indexOf(`${selector} {`)
  const body = css.slice(at, css.indexOf('}', at))
  return Object.fromEntries([...body.matchAll(/(--avatar-[sl]|--avatar-bg-l|--avatar-fg-l)\s*:\s*([\d.]+)%/g)]
    .map(m => [m[1]!, Number(m[2]) / 100]))
}

function luminance(h: number, s: number, l: number): number {
  const a = s * Math.min(l, 1 - l)
  const channel = (n: number) => {
    const k = (n + h / 30) % 12
    const c = l - a * Math.max(-1, Math.min(k - 3, 9 - k, 1))
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4
  }
  return 0.2126 * channel(0) + 0.7152 * channel(8) + 0.0722 * channel(4)
}

function minContrast(selector: string): number {
  const { '--avatar-s': s, '--avatar-bg-l': bg, '--avatar-fg-l': fg } = constantsOf(selector)
  expect([s, bg, fg].every(v => v !== undefined)).toBe(true)
  let min = Infinity
  for (let hue = 0; hue < 360; hue++) {
    const [a, b] = [luminance(hue, s!, bg!), luminance(hue, s!, fg!)]
    min = Math.min(min, (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05))
  }
  return min
}

describe('sender avatar contrast', () => {
  it('reaches 4.5:1 for every hue in the light theme', () => {
    expect(minContrast('.message-row-avatar.is-sender')).toBeGreaterThanOrEqual(4.5)
  })

  it('reaches 4.5:1 for every hue in the dark theme', () => {
    expect(minContrast(`[data-theme='dark'] .message-row-avatar.is-sender`)).toBeGreaterThanOrEqual(4.5)
  })
})

const mixHex = (a: string, b: string, share: number) => '#' + [1, 3, 5]
  .map(i => Math.round(parseInt(a.slice(i, i + 2), 16) * share + parseInt(b.slice(i, i + 2), 16) * (1 - share))
    .toString(16).padStart(2, '0')).join('')

function monoContrast(tokens: Record<string, string>): number {
  const accent = tokens['--action-primary']!
  return contrast(mixHex(accent, tokens['--text']!, 0.55), mixHex(accent, tokens['--surface']!, 0.26))
}

function valuesIn(file: string, selector: string): Record<string, string> {
  const at = file.indexOf(`${selector} {`)
  const body = file.slice(at, file.indexOf('}', at)).replace(/\/\*[\s\S]*?\*\//g, '')
  return Object.fromEntries([...body.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)].map(m => [m[1]!, m[2]!.trim()]))
}

describe('monochrome avatar contrast', () => {
  it('uses the accent mixes this test measures', () => {
    expect(css).toMatch(/is-mono \{\s*background: color-mix\(in srgb, var\(--action-primary\) 26%, var\(--surface\)\);\s*color: color-mix\(in srgb, var\(--action-primary\) 55%, var\(--text\)\);/)
  })

  it('reaches 4.5:1 in the sixteen built-in palette/mode combinations', () => {
    const files = Object.entries(themes)
    expect(files).toHaveLength(8)
    for (const [path, file] of files) {
      const id = /theme-(\w+)\.css/.exec(path)![1]!
      const light = valuesIn(file, `[data-palette='${id}']`)
      const dark = { ...light, ...valuesIn(file, `[data-palette='${id}'][data-theme='dark']`) }
      expect(monoContrast(light), `${id} light`).toBeGreaterThanOrEqual(4.5)
      expect(monoContrast(dark), `${id} dark`).toBeGreaterThanOrEqual(4.5)
    }
  })

  it('reaches 4.5:1 in every generated custom palette', () => {
    for (const intensity of INTENSITIES) {
      for (let accent = 0; accent < 360; accent += 5) {
        const { light, dark }: { light: TokenSet; dark: TokenSet } = generateCustomPalette({ structure: (accent + 120) % 360, intensity, accent, buttons: 'accent' })
        expect(monoContrast(light), `${intensity} ${accent} light`).toBeGreaterThanOrEqual(4.5)
        expect(monoContrast(dark), `${intensity} ${accent} dark`).toBeGreaterThanOrEqual(4.5)
      }
    }
  })
})
