import { describe, it, expect } from 'vitest'

// ?raw on the stylesheets, the mechanism modals.test.ts uses on the components.
const sheets = import.meta.glob('./*.css', {
  query: '?raw', import: 'default', eager: true,
}) as Record<string, string>
const root = import.meta.glob('../index.css', {
  query: '?raw', import: 'default', eager: true,
}) as Record<string, string>
const all = { ...sheets, ...root }

function widthsUsedBy(query: RegExp): string[] {
  return Object.entries(all).flatMap(([path, css]) =>
    [...css.matchAll(query)].map(match => `${path}: ${match[0]}`))
}

// Slices out one @media block's body by brace depth, the way palettes.test.ts's tokensIn slices
// a selector block — a plain indexOf-to-next-'}' would stop at the first rule inside the query,
// not at the query's own end.
function mediaBlock(css: string, query: string): string {
  const at = css.indexOf(query)
  if (at < 0) return ''
  let depth = 0
  let i = css.indexOf('{', at)
  const start = i
  for (; i < css.length; i++) {
    if (css[i] === '{') depth++
    else if (css[i] === '}' && --depth === 0) break
  }
  return css.slice(start, i + 1)
}

describe('responsive contract', () => {
  // A key count alone passes on 14 files all holding '' — exactly what an under-inclusive
  // vite.config.js test.css.include mock produces. Real content is what proves the glob read.
  it('reads the stylesheets, not an empty glob', () => {
    const lengths = Object.values(all).map(css => css.length)
    expect(lengths.length).toBeGreaterThan(5)
    expect(Math.min(...lengths)).toBeGreaterThan(0)
  })

  it('holds no desktop floor', () => {
    expect(all['./shell.css']).not.toMatch(/min-width:\s*1024px/)
  })

  // Desktop stays the unqualified base rule. A min-width query means somebody inverted the
  // cascade, and every desktop rule now has to be read through a filter.
  it('uses no min-width media query', () => {
    expect(widthsUsedBy(/@media[^{]*min-width[^{]*/g)).toEqual([])
  })

  // Exactly two breakpoints, spelled one way each. Scoped to @media on purpose: @container
  // queries carry their own widths and answer to the column they measure, not to the window.
  it('uses only the two agreed breakpoint widths', () => {
    const widths = widthsUsedBy(/@media[^{]*max-width:\s*\d+px/g)
      .map(entry => entry.replace(/.*max-width:\s*/, ''))
    expect([...new Set(widths)].sort()).toEqual(['1023px', '639px'])
  })

  it('sizes the full-height roots in dvh', () => {
    expect(all['./shell.css']).toMatch(/height:\s*100dvh/)
    expect(all['../index.css']).toMatch(/min-height:\s*100dvh/)
  })

  it('declares the touch floor once, in the phone block', () => {
    const shell = all['./shell.css']
    expect([...shell.matchAll(/--touch:/g)]).toHaveLength(1)
    expect(mediaBlock(shell, '@media (max-width: 639px)')).toMatch(/--touch:/)
  })
})
