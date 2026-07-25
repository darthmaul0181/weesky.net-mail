import { describe, it, expect, vi, beforeEach } from 'vitest'
import { createRef } from 'react'
import { render } from '@testing-library/react'
import Squire from 'squire-rte'
import SquireEditor, { type EditorHandle } from './SquireEditor'

// Squire owns a real contenteditable; jsdom's Range/Selection are too partial to run it,
// so the engine is mocked and these tests cover our glue only, never Squire itself.
const instance = {
  getHTML: vi.fn(() => '<div>hi</div>'),
  setHTML: vi.fn(),
  moveCursorToStart: vi.fn(),
  addEventListener: vi.fn(),
  destroy: vi.fn(),
  focus: vi.fn(),
  undo: vi.fn(), redo: vi.fn(),
  bold: vi.fn(), removeBold: vi.fn(),
  italic: vi.fn(), removeItalic: vi.fn(),
  underline: vi.fn(), removeUnderline: vi.fn(),
  strikethrough: vi.fn(), removeStrikethrough: vi.fn(),
  hasFormat: vi.fn<(tag: string) => boolean>(() => false),
  makeUnorderedList: vi.fn(), makeOrderedList: vi.fn(), removeList: vi.fn(),
  increaseQuoteLevel: vi.fn(), decreaseQuoteLevel: vi.fn(),
  makeLink: vi.fn(), removeLink: vi.fn(),
  setTextColor: vi.fn(), setHighlightColor: vi.fn(),
  setFontFace: vi.fn(), setFontSize: vi.fn(), setTextAlignment: vi.fn(),
  removeAllFormatting: vi.fn(),
}
// A plain function, not an arrow: the wrapper calls it with `new`, and an arrow is not constructible.
vi.mock('squire-rte', () => ({ default: vi.fn(function () { return instance }) }))

function setup() {
  const ref = createRef<EditorHandle>()
  const onChange = vi.fn()
  const view = render(<SquireEditor ref={ref} onChange={onChange} />)
  return { ref, onChange, view }
}

describe('SquireEditor', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    instance.hasFormat.mockReturnValue(false)
    instance.getHTML.mockReturnValue('<div>hi</div>')
  })

  it('relays commands and reads HTML through the handle', () => {
    const { ref } = setup()
    ref.current!.command('bold')
    expect(instance.bold).toHaveBeenCalled()
    expect(ref.current!.getHTML()).toBe('<div>hi</div>')
  })

  it('toggles a format off when it is already applied', () => {
    const { ref } = setup()
    instance.hasFormat.mockReturnValue(true)
    ref.current!.command('bold')
    expect(instance.removeBold).toHaveBeenCalled()
    expect(instance.bold).not.toHaveBeenCalled()
  })

  it('reports active formats on pathChange so the toolbar can light its buttons', () => {
    const onFormatChange = vi.fn()
    const ref = createRef<EditorHandle>()
    render(<SquireEditor ref={ref} onChange={() => {}} onFormatChange={onFormatChange} />)

    // Reported once at mount (nothing on yet).
    expect(onFormatChange).toHaveBeenLastCalledWith(expect.objectContaining({ bold: false, italic: false }))

    instance.hasFormat.mockImplementation((tag: string) => tag === 'b')
    const pathChange = instance.addEventListener.mock.calls.find(call => call[0] === 'pathChange')!
    ;(pathChange[1] as () => void)()

    expect(onFormatChange).toHaveBeenLastCalledWith(expect.objectContaining({ bold: true, italic: false }))
  })

  it('relays the non-toggle commands', () => {
    const { ref } = setup()
    ref.current!.command('undo')
    ref.current!.command('redo')
    ref.current!.command('increaseQuote')
    ref.current!.command('decreaseQuote')
    ref.current!.command('removeLink')
    ref.current!.command('clearFormatting')
    expect(instance.undo).toHaveBeenCalled()
    expect(instance.redo).toHaveBeenCalled()
    expect(instance.increaseQuoteLevel).toHaveBeenCalled()
    expect(instance.decreaseQuoteLevel).toHaveBeenCalled()
    expect(instance.removeLink).toHaveBeenCalled()
    expect(instance.removeAllFormatting).toHaveBeenCalled()
  })

  it('relays the styling calls, mapping the British handle names onto Squire spelling', () => {
    const { ref } = setup()
    ref.current!.setTextColour('#d0021b')
    ref.current!.setHighlightColour('#f8e71c')
    ref.current!.setFontFace('Georgia')
    ref.current!.setFontSize('18px')
    ref.current!.setAlignment('center')
    ref.current!.makeLink('https://weesky.net')
    ref.current!.focus()
    expect(instance.setTextColor).toHaveBeenCalledWith('#d0021b')
    expect(instance.setHighlightColor).toHaveBeenCalledWith('#f8e71c')
    expect(instance.setFontFace).toHaveBeenCalledWith('Georgia')
    expect(instance.setFontSize).toHaveBeenCalledWith('18px')
    expect(instance.setTextAlignment).toHaveBeenCalledWith('center')
    expect(instance.makeLink).toHaveBeenCalledWith('https://weesky.net')
    expect(instance.focus).toHaveBeenCalled()
  })

  it('reports an empty body when the markup carries no text', () => {
    const { ref } = setup()
    instance.getHTML.mockReturnValue('<div><br></div>')
    expect(ref.current!.isEmpty()).toBe(true)
    instance.getHTML.mockReturnValue('<div>hi</div>')
    expect(ref.current!.isEmpty()).toBe(false)
  })

  it('reports non-empty for a body that is only an image, an hr, or a table', () => {
    const { ref } = setup()
    instance.getHTML.mockReturnValue('<img src="x.png">')
    expect(ref.current!.isEmpty()).toBe(false)
    instance.getHTML.mockReturnValue('<hr>')
    expect(ref.current!.isEmpty()).toBe(false)
    instance.getHTML.mockReturnValue('<table><tr><td><img src="x.png"></td></tr></table>')
    expect(ref.current!.isEmpty()).toBe(false)
  })

  it('is not fooled by a > inside an attribute value into reporting non-empty', () => {
    const { ref } = setup()
    instance.getHTML.mockReturnValue('<div><br title="a>b"></div>')
    expect(ref.current!.isEmpty()).toBe(true)
  })

  it('hands Squire a sanitiser, since it would otherwise reach for a global DOMPurify', () => {
    setup()
    const config = vi.mocked(Squire).mock.calls[0][1]!
    const fragment = config.sanitizeToDOMFragment!('<p>hi</p><script>alert(1)</script>', {} as Squire)
    const holder = document.createElement('div')
    holder.appendChild(fragment)
    expect(holder.innerHTML).toBe('<p>hi</p>')
  })

  it('strips a style element, since a surviving one would apply document-wide to the chrome', () => {
    setup()
    const config = vi.mocked(Squire).mock.calls[0][1]!
    const fragment = config.sanitizeToDOMFragment!('<p>hi</p><style>.mail-list{display:none}</style>', {} as Squire)
    const holder = document.createElement('div')
    holder.appendChild(fragment)
    expect(holder.innerHTML).toBe('<p>hi</p>')
  })

  it('strips a form element, the same policy the reader applies', () => {
    setup()
    const config = vi.mocked(Squire).mock.calls[0][1]!
    const fragment = config.sanitizeToDOMFragment!(
      '<form action="https://evil.example"><input type="password" name="p"></form>', {} as Squire,
    )
    const holder = document.createElement('div')
    holder.appendChild(fragment)
    expect(holder.innerHTML).not.toContain('<form')
  })

  it('fires onChange on the input event', () => {
    const { onChange } = setup()
    const inputHandler = instance.addEventListener.mock.calls.find(c => c[0] === 'input')![1]
    inputHandler()
    expect(onChange).toHaveBeenCalled()
  })

  // A seeded body opens with the caret on the empty line above the quote, not after it, and
  // takes the focus: a prefilled composer is there to be written in, not addressed.
  it('loads an initial body at mount, parks the caret at the top and takes focus', () => {
    render(<SquireEditor onChange={() => {}} initialHtml="<div><br></div><blockquote>q</blockquote>" />)
    expect(instance.setHTML).toHaveBeenCalledWith('<div><br></div><blockquote>q</blockquote>')
    expect(instance.moveCursorToStart).toHaveBeenCalled()
    expect(instance.focus).toHaveBeenCalled()
  })

  // Without a seed the To field owns the focus, so the editor must not steal it at mount.
  it('leaves the engine untouched when no initial body is given', () => {
    setup()
    expect(instance.setHTML).not.toHaveBeenCalled()
    expect(instance.focus).not.toHaveBeenCalled()
  })

  it('destroys the engine on unmount', () => {
    const { view } = setup()
    view.unmount()
    expect(instance.destroy).toHaveBeenCalled()
  })
})
