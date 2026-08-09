import { describe, it, expect } from 'vitest'

// ?raw on a .tsx source is the same mechanism palettes.test.ts uses on main.tsx.
const sources = import.meta.glob('../**/*.{jsx,tsx}', {
  query: '?raw', import: 'default', eager: true,
}) as Record<string, string>

/** A dialog's width is the contract's business (styles/modal.css), never a component's. */
function inlineWidths(): string[] {
  return Object.entries(sources)
    .filter(([path]) => !path.includes('.test.'))
    .flatMap(([path, src]) => src.split('\n')
      .map((line, i) => ({ path, at: i + 1, line }))
      .filter(({ line }) => /className="modal[\s"]/.test(line) && /[Ww]idth:/.test(line))
      .map(({ path, at }) => `${path}:${at}`))
}

describe('modal roots', () => {
  // Without this the glob could return nothing and the check below would pass vacuously.
  it('reads the components, not an empty glob', () => {
    expect(Object.keys(sources).length).toBeGreaterThan(50)
  })

  it('carry no inline width', () => {
    expect(inlineWidths()).toEqual([])
  })
})

const modalCss = (import.meta.glob('./modal.css', {
  query: '?raw', import: 'default', eager: true,
}) as Record<string, string>)['./modal.css']

// The same brace-depth extractor responsive.test.ts uses, for the same reason: a plain
// indexOf-to-next-'}' stops at the first rule inside the block, not at the block's own end.
function braceBlock(css: string, opensAt: number): string {
  let depth = 0
  let i = css.indexOf('{', opensAt)
  const start = i
  for (; i < css.length; i++) {
    if (css[i] === '{') depth++
    else if (css[i] === '}' && --depth === 0) break
  }
  return css.slice(start, i + 1)
}

describe('dialogs on a narrow screen', () => {
  // min-width always beats max-width: a 384px floor inside a 312px slot overflows the page.
  // Scoped to .modal's own rule inside the phone block, not a slice-to-end-of-file: the latter
  // would still pass the day a second, unrelated phone block lands after this one and declares
  // its own --modal-w. The regex requires the value to be exactly 0, so --modal-w: 0.5rem could
  // never satisfy it by accident either.
  it('drops the 24rem floor below 640px', () => {
    const phoneBlock = braceBlock(modalCss, modalCss.indexOf('@media (max-width: 639px)'))
    const modalRuleAt = phoneBlock.search(/\.modal\s*\{/)
    expect(modalRuleAt).toBeGreaterThan(-1)
    const modalRule = braceBlock(phoneBlock, modalRuleAt)
    expect(modalRule).toMatch(/--modal-w:\s*0\s*[;}]/)
  })

  it('keeps the content-sized contract above 640px', () => {
    expect(modalCss).toMatch(/min-width:\s*var\(--modal-w,\s*24rem\)/)
  })
})
