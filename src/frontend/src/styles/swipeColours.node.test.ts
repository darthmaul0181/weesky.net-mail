import { describe, expect, it } from 'vitest'

const modules = import.meta.glob('./theme-*.css', { query: '?raw', import: 'default', eager: true })
const files = Object.keys(modules)
const ROLES = ['--swipe-seen', '--swipe-flag', '--swipe-archive', '--swipe-delete']

function block(css: string, selector: string): string {
  const at = css.indexOf(`${selector} {`)
  return at < 0 ? '' : css.slice(at, css.indexOf('}', at))
}

function valueOf(body: string, role: string): string | undefined {
  return new RegExp(`${role}\\s*:\\s*(#[0-9a-fA-F]{6})`).exec(body)?.[1]
}

function luminance(hex: string): number {
  const [r, g, b] = [1, 3, 5].map(i => {
    const c = parseInt(hex.slice(i, i + 2), 16) / 255
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4
  })
  return 0.2126 * r! + 0.7152 * g! + 0.0722 * b!
}

const onWhite = (hex: string) => 1.05 / (luminance(hex) + 0.05)

describe('the swipe fills', () => {
  it('reads the stylesheets, not empty mocks', () => {
    expect(files).toHaveLength(8)
  })

  // The band's label is 14px: small text, 4.5:1. The dark block must not redeclare them, so one
  // check per palette covers both modes.
  it.each(files)('%s gives every swipe fill 4.5:1 under white text, in both modes', file => {
    const css = modules[file]!
    const id = file.slice('./theme-'.length, -'.css'.length)
    const light = block(css, `[data-palette='${id}']`)
    const dark = block(css, `[data-palette='${id}'][data-theme='dark']`)
    for (const role of ROLES) {
      const value = valueOf(light, role)
      expect(value, `${file} ${role}`).toBeDefined()
      expect(onWhite(value!), `${file} ${role}`).toBeGreaterThanOrEqual(4.5)
      expect(valueOf(dark, role), `${file} dark ${role}`).toBeUndefined()
    }
  })
})
