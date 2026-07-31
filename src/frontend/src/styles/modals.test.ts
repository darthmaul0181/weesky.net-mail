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
