import { describe, it, expect } from 'vitest'

// ?raw on a .tsx source is the same mechanism palettes.test.ts uses on main.tsx.
const sources = import.meta.glob('../**/*.{jsx,tsx}', {
  query: '?raw', import: 'default', eager: true,
}) as Record<string, string>

/** The line each `<Modal …>` opening tag carrying a width starts on. A caller's tag spans
    several lines, so the line-by-line scan below cannot see a style prop sitting on one of
    its own. The tag ends at the first `>` that is neither inside a quoted attribute value
    (`title="a > b"`) nor inside braces — which is also what skips the one closing an element
    passed as a prop (`icon={<X />}`). */
function modalTagWidths(src: string): number[] {
  const found: number[] = []
  for (let start = src.indexOf('<Modal'); start !== -1; start = src.indexOf('<Modal', start + 1)) {
    if (!/[\s/>]/.test(src.slice(start + 6, start + 7))) continue // <ModalOverlay is another tag
    let depth = 0
    let quote = ''
    for (let i = start; i < src.length; i++) {
      if (quote) { if (src[i] === quote) quote = '' }
      else if (src[i] === '"' || src[i] === "'") quote = src[i]
      else if (src[i] === '{') depth++
      else if (src[i] === '}') depth--
      else if (src[i] === '>' && depth === 0) {
        if (/[Ww]idth:/.test(src.slice(start, i + 1))) found.push(src.slice(0, start).split('\n').length)
        break
      }
    }
  }
  return found
}

/** A dialog's width is the contract's business (styles/modal.css), never a component's. */
function inlineWidths(): string[] {
  return Object.entries(sources)
    .filter(([path]) => !path.includes('.test.'))
    .flatMap(([path, src]) => [
      ...src.split('\n')
        .map((line, i) => ({ at: i + 1, line }))
        .filter(({ line }) => /className="modal[\s"]/.test(line) && /[Ww]idth:/.test(line))
        .map(({ at }) => `${path}:${at}`),
      ...modalTagWidths(src).map(at => `${path}:${at}`),
    ])
}

describe('modal roots', () => {
  // Without this the glob could return nothing and the check below would pass vacuously.
  it('reads the components, not an empty glob', () => {
    expect(Object.keys(sources).length).toBeGreaterThan(50)
  })

  // The <Modal> half of the guard has no caller until Task 5, so its mechanism is proved here
  // rather than by the repo-wide scan below, which would pass vacuously in the meantime.
  it('sees a width anywhere in a <Modal> tag, and only in one', () => {
    expect(modalTagWidths('<Modal\n  title="x"\n  style={{ width: 400 }}\n>')).toEqual([1])
    expect(modalTagWidths('<ModalOverlay style={{ width: 400 }}>')).toEqual([])
    expect(modalTagWidths('<Modal title="x">\n<p style={{ width: 400 }} />')).toEqual([])
    expect(modalTagWidths('<Modal title="a > b" style={{ width: 400 }}>')).toEqual([1])
    expect(modalTagWidths('<Modal icon={<X />} title="x">\n<p style={{ width: 4 }} />')).toEqual([])
  })

  it('carry no inline width', () => {
    expect(inlineWidths()).toEqual([])
  })

  // Enabled in Task 5 by dropping the `.todo`, once the last caller draws its backdrop through
  // <Modal>: until then ScopeModal, ComposeView and the calendar editor write the class out.
  it.todo('draw the backdrop from the Modal shell alone', () => {
    const shell = ['../components/Modal.tsx', '../components/ModalOverlay.tsx']
    const writers = Object.entries(sources)
      .filter(([path, src]) => !path.includes('.test.') && src.includes('modal-overlay'))
      .map(([path]) => path)
    expect(writers.filter(path => !shell.includes(path))).toEqual([])
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

  // Stacked under its label, a 34ch text box ended 71px short of the select below it at 390.
  it('gives every field row\'s control the whole dialog width below 640px', () => {
    const phoneBlock = braceBlock(modalCss, modalCss.indexOf('@media (max-width: 639px)'))
    const rowRuleAt = phoneBlock.search(/\.modal \.field-h\s*\{/)
    expect(rowRuleAt).toBeGreaterThan(-1)
    expect(braceBlock(phoneBlock, rowRuleAt)).toMatch(/--field-w:\s*100%\s*[;}]/)
  })

  it('keeps the content-sized contract above 640px', () => {
    expect(modalCss).toMatch(/min-width:\s*var\(--modal-w,\s*24rem\)/)
  })
})
