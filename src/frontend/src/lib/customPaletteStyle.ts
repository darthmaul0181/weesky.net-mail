import { readStored, removeStored, writeStored } from './safeStorage'
import {
  customPaletteCss, formatCustomPalette, generateCustomPalette, type CustomPaletteDef,
} from './customPalette'

// Repeated by the pre-paint script in index.html, which cannot import them.
export const CUSTOM_PALETTE_MIRROR_KEY = 'appearance_custom_palette'
export const CUSTOM_PALETTE_CSS_KEY = 'appearance_custom_palette_css'
export const CUSTOM_PALETTE_STYLE_ID = 'custom-palette'

/** The pre-paint script's own test, so the two halves never disagree on one frame. */
export function hasCustomPaletteMirror(): boolean {
  return !!readStored(CUSTOM_PALETTE_CSS_KEY)
}

export function applyCustomPalette(def: CustomPaletteDef | null): void {
  let style = document.getElementById(CUSTOM_PALETTE_STYLE_ID)
  if (!def) {
    style?.remove()
    removeStored(CUSTOM_PALETTE_MIRROR_KEY)
    removeStored(CUSTOM_PALETTE_CSS_KEY)
    return
  }
  const css = customPaletteCss(generateCustomPalette(def))
  if (!style) {
    style = document.createElement('style')
    style.id = CUSTOM_PALETTE_STYLE_ID
    document.head.appendChild(style)
  }
  style.textContent = css
  writeStored(CUSTOM_PALETTE_MIRROR_KEY, formatCustomPalette(def))
  writeStored(CUSTOM_PALETTE_CSS_KEY, css)
}
