import { describe, it, expect, beforeEach } from 'vitest'
import html from '../../index.html?raw'
import {
  applyCustomPalette, CUSTOM_PALETTE_CSS_KEY, CUSTOM_PALETTE_MIRROR_KEY, CUSTOM_PALETTE_STYLE_ID,
  hasCustomPaletteMirror,
} from './customPaletteStyle'

const styleEl = () => document.getElementById(CUSTOM_PALETTE_STYLE_ID)

describe('applyCustomPalette', () => {
  beforeEach(() => { localStorage.clear(); styleEl()?.remove() })

  it('writes one style element and the mirror', () => {
    applyCustomPalette({ structure: 265, intensity: 'muted', accent: 35 })
    applyCustomPalette({ structure: 100, intensity: 'vivid', accent: 200 })

    expect(document.querySelectorAll(`#${CUSTOM_PALETTE_STYLE_ID}`)).toHaveLength(1)
    expect(styleEl()!.textContent).toContain("[data-palette='custom']")
    expect(localStorage.getItem(CUSTOM_PALETTE_MIRROR_KEY)).toBe('100,vivid,200')
    expect(localStorage.getItem(CUSTOM_PALETTE_CSS_KEY)).toBe(styleEl()!.textContent)
    expect(hasCustomPaletteMirror()).toBe(true)
  })

  it('removes both on null', () => {
    applyCustomPalette({ structure: 265, intensity: 'muted', accent: 35 })
    applyCustomPalette(null)

    expect(styleEl()).toBeNull()
    expect(localStorage.getItem(CUSTOM_PALETTE_MIRROR_KEY)).toBeNull()
    expect(localStorage.getItem(CUSTOM_PALETTE_CSS_KEY)).toBeNull()
    expect(hasCustomPaletteMirror()).toBe(false)
  })
})

/** Runs the real pre-paint script, since it cannot import this module and repeats its keys. */
describe('the pre-paint script and the custom palette', () => {
  const script = html.match(/<script>\s*\(function\(\)\{[\s\S]*?\}\)\(\);\s*<\/script>/)![0]
    .replace(/^<script>/, '').replace(/<\/script>$/, '')
  // Executing the shipped script is the point: it is the only copy of this logic before React.
  // eslint-disable-next-line @typescript-eslint/no-implied-eval
  const run = new Function(script) as () => void

  // 'light' keeps the script off window.matchMedia.
  beforeEach(() => { localStorage.clear(); localStorage.setItem('appearance_theme', 'light'); styleEl()?.remove() })

  it('paints the mirrored palette before React', () => {
    localStorage.setItem('appearance_palette', 'custom')
    localStorage.setItem(CUSTOM_PALETTE_CSS_KEY, "[data-palette='custom'] { --bg: #123456; }")
    run()

    expect(document.documentElement.getAttribute('data-palette')).toBe('custom')
    expect(styleEl()!.textContent).toContain('#123456')
  })

  it('falls back to night without a mirror', () => {
    localStorage.setItem('appearance_palette', 'custom')
    run()

    expect(document.documentElement.getAttribute('data-palette')).toBe('night')
    expect(styleEl()).toBeNull()
  })
})
