import { describe, expect, it } from 'vitest'
import html from '../../index.html?raw'
import { LANGUAGE_MIRROR_KEY, SUPPORTED_LOCALES } from './locale'

/** The pre-paint script cannot import this module — it runs before any bundle. It therefore
    repeats the locale list and the mirror key, exactly as the theme script repeats PALETTE_IDS,
    and this asserts the two halves agree. Forgetting the script half produces a bug no other test
    sees: on reload, <html lang> disagrees with the rendered language for one frame. */
describe('the index.html pre-paint language script', () => {
  // Asserts the literal the script actually reads, not just that each locale's quoted form
  // appears somewhere in the file: 'en' occurs three times in the script regardless of what
  // `langs` holds, so a looser match would not redden if a locale were dropped from that array.
  it('carries every supported locale', () => {
    const literal = `var langs=[${SUPPORTED_LOCALES.map(l => `'${l}'`).join(',')}]`
    expect(html).toContain(literal)
  })

  it('reads the same mirror key the module writes', () => {
    expect(html).toContain(`localStorage.getItem('${LANGUAGE_MIRROR_KEY}')`)
  })

  it('no longer hard-codes a language on the html element', () => {
    expect(html).not.toMatch(/<html\s+lang=/)
  })
})
