import { describe, it, expect, vi, afterEach } from 'vitest'
import { forEachStyleRule, hasDarkDesign, resolveColourScheme, rewriteStyleElements } from './messageStyles'

describe('resolveColourScheme', () => {
  const html = '<style>@media (prefers-color-scheme: dark) { .x { color: #fff } } '
    + '@media screen and (prefers-color-scheme: light) { .y { color: #000 } }</style><p>x</p>'

  // We decide, not the browser: what the iframe inherits from the page is Blink's business.
  it('makes the dark blocks unconditional and the light ones inert for a dark render', () => {
    const out = resolveColourScheme(html, 'dark')
    expect(out).not.toContain('prefers-color-scheme')
    expect(out).toContain('@media (min-width: 0) { .x')
    expect(out).toContain('@media screen and (max-width: 0) and (min-width: 1px) { .y')
  })

  it('does the opposite for a light render', () => {
    const out = resolveColourScheme(html, 'light')
    expect(out).toContain('@media (max-width: 0) and (min-width: 1px) { .x')
    expect(out).toContain('@media screen and (min-width: 0) { .y')
  })

  it('leaves a body with no stylesheet untouched', () => {
    expect(resolveColourScheme('<p>prefers-color-scheme: dark</p>', 'light')).toBe('<p>prefers-color-scheme: dark</p>')
  })
})

describe('hasDarkDesign', () => {
  it('finds a dark media condition in a stylesheet', () => {
    expect(hasDarkDesign('<style>@media screen and (prefers-color-scheme:DARK) { .x { color: #fff } }</style>')).toBe(true)
  })

  // A boilerplate <meta name="color-scheme" content="light dark"> promises a dark design it may not have.
  it('finds none in a light-only sheet or a body with no sheet', () => {
    expect(hasDarkDesign('<style>@media (prefers-color-scheme: light) { .x { color: #000 } }</style>')).toBe(false)
    expect(hasDarkDesign('<p>x</p>')).toBe(false)
  })

  // A developer newsletter quoting the query in its prose or a code sample has no dark design.
  it('ignores the condition outside a stylesheet', () => {
    expect(hasDarkDesign('<p>Use (prefers-color-scheme: dark)</p><code>@media (prefers-color-scheme: dark)</code>')).toBe(false)
    expect(hasDarkDesign('<p>Use (prefers-color-scheme: dark)</p><style>.x { color: red }</style>')).toBe(false)
  })

  it('finds the condition only inside a stylesheet', () => {
    expect(hasDarkDesign('<p>x</p><style>@media (prefers-color-scheme: dark) { .x { color: #fff } }</style>')).toBe(true)
  })
})

describe('rewriteStyleElements', () => {
  it('rewrites a style at the very start of the fragment and keeps it there', () => {
    expect(rewriteStyleElements('<style>.a { color: red }</style><p>x</p>', css => css.replace('red', 'blue')))
      .toBe('<style>.a { color: blue }</style><p>x</p>')
  })
})

describe('forEachStyleRule', () => {
  afterEach(() => vi.restoreAllMocks())

  // Safari before 16.4 throws here; a throw would blank the reader in dark theme.
  it('hands the sheet back untouched when the browser cannot construct one', () => {
    vi.spyOn(CSSStyleSheet.prototype, 'replaceSync').mockImplementation(() => { throw new TypeError('unsupported') })
    const edit = vi.fn()
    expect(forEachStyleRule('.a { color: red }', edit)).toBe('.a { color: red }')
    expect(edit).not.toHaveBeenCalled()
  })

  it('reaches rules nested in a media block and keeps their priority', () => {
    const out = forEachStyleRule('@media (max-width: 499px) { .a { color: #000 !important } } .b { color: red }',
      style => style.setProperty('color', '#123456', style.getPropertyPriority('color')))
    expect(out).toMatch(/\.a \{ color: (#123456|rgb\(18, 52, 86\)) !important; \}/)
    expect(out).toMatch(/\.b \{ color: (#123456|rgb\(18, 52, 86\)); \}/)
  })

  it('reaches a rule nested in a style rule, after the rule itself', () => {
    const seen: string[] = []
    const out = forEachStyleRule('.a { color: red; .b { color: blue } }', style => {
      seen.push(style.getPropertyValue('color'))
      style.setProperty('color', '#123456')
    })
    expect(seen).toEqual(['red', 'blue'])
    expect(out).not.toMatch(/red|blue/)
  })
})
