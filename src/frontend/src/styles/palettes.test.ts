import { describe, it, expect } from 'vitest'
import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'

const STYLES = join(process.cwd(), 'src/styles')

const files = readdirSync(STYLES).filter(f => /^theme-.+\.css$/.test(f))
const idOf = (file: string) => file.slice('theme-'.length, -'.css'.length)

/** The ` {` is what keeps the light selector from also matching the dark one, which starts with it. */
function tokensIn(css: string, selector: string): string[] {
  const at = css.indexOf(`${selector} {`)
  if (at < 0) return []
  const body = css.slice(at, css.indexOf('}', at)).replace(/\/\*[\s\S]*?\*\//g, '')

  return [...body.matchAll(/(--[\w-]+)\s*:/g)].map(m => m[1]).sort()
}

function blocks(file: string) {
  const css = readFileSync(join(STYLES, file), 'utf8')
  const id = idOf(file)

  return {
    light: tokensIn(css, `[data-palette='${id}']`),
    dark: tokensIn(css, `[data-palette='${id}'][data-theme='dark']`),
  }
}

const reference = blocks('theme-night.css')

describe('the palette stylesheets', () => {
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
