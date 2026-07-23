import { describe, it, expect } from 'vitest'
import { PALETTE_IDS } from '../contexts/ThemeContext'
import html from '../../index.html?raw'
import mainSource from '../main.tsx?raw'

const modules = import.meta.glob('./theme-*.css', { query: '?raw', import: 'default', eager: true }) as Record<string, string>
const files = Object.keys(modules).map(path => path.replace('./', ''))
const idOf = (file: string) => file.slice('theme-'.length, -'.css'.length)

/** The ` {` is what keeps the light selector from also matching the dark one, which starts with it. */
function tokensIn(css: string, selector: string): string[] {
  const at = css.indexOf(`${selector} {`)
  if (at < 0) return []
  const body = css.slice(at, css.indexOf('}', at)).replace(/\/\*[\s\S]*?\*\//g, '')

  return [...body.matchAll(/(--[\w-]+)\s*:/g)].map(m => m[1]).sort()
}

function blocks(file: string) {
  const css = modules[`./${file}`]
  const id = idOf(file)

  return {
    light: tokensIn(css, `[data-palette='${id}']`),
    dark: tokensIn(css, `[data-palette='${id}'][data-theme='dark']`),
  }
}

const reference = blocks('theme-night.css')

describe('the palette stylesheets', () => {
  // Vitest mocks CSS imports to '' unless vite.config's test.css.include matches; that empties
  // every token list and makes each parity check pass vacuously.
  it('reads the stylesheets, not empty mocks', () => {
    expect(reference.light).toHaveLength(35)
  })
  it('ships every palette the picker offers', () => {
    expect(files.map(idOf).sort()).toEqual(['classic', 'forest', 'ink', 'night', 'plum', 'slate'])
  })

  // A role missing from a palette falls back to whatever the cascade holds — a browser default
  // for --quote-text, the light value for --list-row-selected-bg in dark mode. Neither throws,
  // neither shows up in review, and both look like a rendering fault to the user.
  it.each(files)('%s declares every role in its light block', file => {
    expect(blocks(file).light).toEqual(reference.light)
  })

  // classic's dark block is the one deliberate gap: it omits --danger, --danger-hover and
  // --success, inheriting them from its own light block. Recorded rather than hidden.
  const CLASSIC_INHERITS = ['--danger', '--danger-hover', '--success']

  it.each(files)('%s declares every role in its dark block', file => {
    const expected = idOf(file) === 'classic'
      ? reference.dark.filter(t => !CLASSIC_INHERITS.includes(t))
      : reference.dark

    expect(blocks(file).dark).toEqual(expected)
  })
})

describe('the pre-paint script in index.html', () => {
  it('accepts exactly the palettes the module knows', () => {
    const list = html.match(/\[([^\]]*)\]\.indexOf\(p\)/)

    expect(list, 'no palette list found in the pre-paint script').not.toBeNull()
    const names = [...list![1].matchAll(/'([^']+)'/g)].map(m => m[1])
    expect(names.sort()).toEqual([...PALETTE_IDS].sort())
  })
})

describe('the palette imports in main.tsx', () => {
  it('reads the file, not an empty stub', () => {
    expect(mainSource).toContain('createRoot')
  })
  it('imports one stylesheet per palette the module knows', () => {
    const ids = [...mainSource.matchAll(/\.\/styles\/theme-([\w-]+)\.css/g)].map(m => m[1])
    expect(ids.sort()).toEqual([...PALETTE_IDS].sort())
  })
})
