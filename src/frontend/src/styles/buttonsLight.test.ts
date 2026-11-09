import { describe, expect, it } from 'vitest'
import { PALETTE_IDS } from '../contexts/ThemeContext'
import { contrast } from '../lib/customPalette'
import css from './buttons-light.css?raw'

const rules = [...css.matchAll(/\[data-palette='(\w+)'\]\[data-buttons='(\w+)'\]([^{]*)\{([^}]*)\}/g)]
  .map(m => ({ id: m[1]!, style: m[2]!, guard: m[3]!.trim(), body: m[4]! }))

describe('the light-mode button variants', () => {
  it('give every built-in palette exactly the choice it does not wear by default', () => {
    expect(rules.map(r => r.id).sort()).toEqual([...PALETTE_IDS].sort())
    for (const r of rules) expect(r.style, r.id).toBe(r.id === 'slate' ? 'structure' : 'accent')
  })

  it('never reach dark mode, whose buttons already wear the accent', () => {
    for (const r of rules) expect(r.guard, r.id).toBe(":not([data-theme='dark'])")
  })

  it('keep white text readable on the button and on its hover', () => {
    for (const r of rules)
      for (const role of ['--action-primary', '--action-primary-hover']) {
        const value = new RegExp(role + `: (#[0-9a-f]{6})`).exec(r.body)?.[1]
        expect(contrast('#ffffff', value!), `${r.id} ${role}`).toBeGreaterThanOrEqual(4.5)
      }
  })
})
