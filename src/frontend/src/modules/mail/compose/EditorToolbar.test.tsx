import { afterEach, beforeEach, describe, it, expect, vi } from 'vitest'
import { fireEvent, render, screen, within } from '@testing-library/react'
import { mockViewport, resetViewport } from '../../../test-utils'
import EditorToolbar from './EditorToolbar'
import type { EditorHandle } from './SquireEditor'

function fakeEditor(): EditorHandle {
  return {
    getHTML: vi.fn(() => ''), isEmpty: vi.fn(() => true), focus: vi.fn(),
    command: vi.fn(), setTextColour: vi.fn(), setHighlightColour: vi.fn(),
    setFontFace: vi.fn(), setFontSize: vi.fn(), setAlignment: vi.fn(), makeLink: vi.fn(),
    insertImage: vi.fn(),
  }
}

const noop = () => {}

function pick(trigger: string, option: string) {
  fireEvent.click(screen.getByRole('button', { name: trigger }))
  fireEvent.click(screen.getByRole('menuitem', { name: option }))
}

describe('EditorToolbar', () => {
  it('relays a format button to the editor', () => {
    const editor = fakeEditor()
    render(<EditorToolbar editor={editor} plainText={false} onPickImages={noop} onTogglePlainText={noop} />)
    fireEvent.click(screen.getByRole('button', { name: 'Bold' }))
    expect(editor.command).toHaveBeenCalledWith('bold')
  })

  it('lights the buttons whose format is active at the caret', () => {
    render(<EditorToolbar editor={fakeEditor()} plainText={false} onPickImages={noop} onTogglePlainText={noop} active={{
      bold: true, italic: false, underline: false, strikethrough: false,
      unorderedList: false, orderedList: false,
    }} />)
    expect(screen.getByRole('button', { name: 'Bold' }).className).toContain('is-active')
    expect(screen.getByRole('button', { name: 'Italic' }).className).not.toContain('is-active')
  })

  it('applies a text colour from the swatch grid', () => {
    const editor = fakeEditor()
    render(<EditorToolbar editor={editor} plainText={false} onPickImages={noop} onTogglePlainText={noop} />)
    fireEvent.click(screen.getByRole('button', { name: 'Text colour' }))
    fireEvent.click(screen.getByRole('button', { name: '#d0021b' }))
    expect(editor.setTextColour).toHaveBeenCalledWith('#d0021b')
  })

  it('applies a highlight colour from its own swatch grid', () => {
    const editor = fakeEditor()
    render(<EditorToolbar editor={editor} plainText={false} onPickImages={noop} onTogglePlainText={noop} />)
    fireEvent.click(screen.getByRole('button', { name: 'Highlight colour' }))
    fireEvent.click(screen.getByRole('button', { name: '#f8e71c' }))
    expect(editor.setHighlightColour).toHaveBeenCalledWith('#f8e71c')
    expect(editor.setTextColour).not.toHaveBeenCalled()
  })

  it('shows the last applied colour under its button', () => {
    render(<EditorToolbar editor={fakeEditor()} plainText={false} onPickImages={noop} onTogglePlainText={noop} />)
    fireEvent.click(screen.getByRole('button', { name: 'Text colour' }))
    fireEvent.click(screen.getByRole('button', { name: '#d0021b' }))
    expect(screen.getByRole('button', { name: 'Text colour' })
      .querySelector('.compose-tool-ink')).toHaveStyle({ background: '#d0021b' })
  })

  it('closes a popover on an outside mousedown', () => {
    const editor = fakeEditor()
    render(<EditorToolbar editor={editor} plainText={false} onPickImages={noop} onTogglePlainText={noop} />)
    fireEvent.click(screen.getByRole('button', { name: 'Text colour' }))
    expect(screen.getByRole('button', { name: '#d0021b' })).toBeInTheDocument()
    fireEvent.mouseDown(document.body)
    expect(screen.queryByRole('button', { name: '#d0021b' })).not.toBeInTheDocument()
  })

  it('applies font, size and alignment from their menus', () => {
    const editor = fakeEditor()
    render(<EditorToolbar editor={editor} plainText={false} onPickImages={noop} onTogglePlainText={noop} />)
    pick('Font', 'Georgia')
    pick('Size', 'Large')
    pick('Alignment', 'Center')
    expect(editor.setFontFace).toHaveBeenCalledWith('Georgia')
    expect(editor.setFontSize).toHaveBeenCalledWith('18px')
    expect(editor.setAlignment).toHaveBeenCalledWith('center')
  })

  // The choice used to be spelled on the trigger, which made the bar re-flow when a long font
  // name was picked. It lives in the menu now, so that is where it has to be readable.
  // Scoped to the trigger's own dropdown: a click does not close the menu opened before it —
  // DropdownMenu dismisses on mousedown — so an unscoped query reads two menus at once.
  const ticked = (menu: string) => {
    const trigger = screen.getByRole('button', { name: menu })
    fireEvent.click(trigger)
    const own = trigger.closest('.dropdown-root') as HTMLElement
    const marked = within(own).getAllByRole('menuitem')
      .filter(item => item.querySelector('svg'))
      .map(item => item.textContent)
    // Closed again: the trigger toggles, so a menu left open turns the next open into a close.
    fireEvent.click(trigger)
    return marked
  }

  it('marks the chosen font and size in their menus', () => {
    render(<EditorToolbar editor={fakeEditor()} plainText={false} onPickImages={noop} onTogglePlainText={noop} />)
    expect(ticked('Font')).toEqual(['Arial'])
    expect(ticked('Size')).toEqual(['Normal'])

    pick('Font', 'Verdana')
    pick('Size', 'Huge')

    expect(ticked('Font')).toEqual(['Verdana'])
    expect(ticked('Size')).toEqual(['Huge'])
  })

  // The whole point of the change: no value on the trigger means no width that moves with it.
  it('keeps the font and size triggers free of their value', () => {
    render(<EditorToolbar editor={fakeEditor()} plainText={false} onPickImages={noop} onTogglePlainText={noop} />)
    pick('Font', 'Times New Roman')

    expect(screen.getByRole('button', { name: 'Font' })).not.toHaveTextContent('Times New Roman')
    expect(screen.getByRole('button', { name: 'Size' })).not.toHaveTextContent('Normal')
  })

  it('offers the four sizes as Small/Normal/Large/Huge', () => {
    render(<EditorToolbar editor={fakeEditor()} plainText={false} onPickImages={noop} onTogglePlainText={noop} />)
    fireEvent.click(screen.getByRole('button', { name: 'Size' }))
    expect(screen.getAllByRole('menuitem').map(item => item.textContent)).toEqual([
      'Small', 'Normal', 'Large', 'Huge',
    ])
  })

  it('inserts a link through the URL popover', () => {
    const editor = fakeEditor()
    render(<EditorToolbar editor={editor} plainText={false} onPickImages={noop} onTogglePlainText={noop} />)
    fireEvent.click(screen.getByRole('button', { name: 'Link' }))
    fireEvent.change(screen.getByLabelText('Link URL'), { target: { value: 'https://weesky.net' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply' }))
    expect(editor.makeLink).toHaveBeenCalledWith('https://weesky.net')
  })

  it('closes the link popover and clears the URL after applying', () => {
    const editor = fakeEditor()
    render(<EditorToolbar editor={editor} plainText={false} onPickImages={noop} onTogglePlainText={noop} />)
    fireEvent.click(screen.getByRole('button', { name: 'Link' }))
    fireEvent.change(screen.getByLabelText('Link URL'), { target: { value: 'https://weesky.net' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply' }))
    expect(screen.queryByLabelText('Link URL')).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Link' }))
    expect(screen.getByLabelText('Link URL')).toHaveValue('')
  })

  it('ships one pair of quote buttons, no separate indent pair', () => {
    const editor = fakeEditor()
    render(<EditorToolbar editor={editor} plainText={false} onPickImages={noop} onTogglePlainText={noop} />)
    fireEvent.click(screen.getByRole('button', { name: 'Increase quote' }))
    fireEvent.click(screen.getByRole('button', { name: 'Decrease quote' }))
    expect(editor.command).toHaveBeenNthCalledWith(1, 'increaseQuote')
    expect(editor.command).toHaveBeenNthCalledWith(2, 'decreaseQuote')
    expect(screen.queryByRole('button', { name: /indent/i })).not.toBeInTheDocument()
  })

  it('does nothing without an editor', () => {
    render(<EditorToolbar editor={null} plainText={false} onPickImages={noop} onTogglePlainText={noop} />)
    fireEvent.click(screen.getByRole('button', { name: 'Bold' }))
    pick('Font', 'Georgia')
    // no throw is the assertion
  })

  it('folds down to the toggle in plain-text mode', () => {
    render(<EditorToolbar editor={null} plainText onPickImages={noop} onTogglePlainText={noop} />)

    expect(screen.getByRole('button', { name: 'Plain text' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.queryByRole('button', { name: 'Bold' })).toBeNull()
  })

  // The composer locks the switch while an inline upload is in flight: adopting before the id
  // exists strands it in no tray row and no payload.
  it('locks the toggle when the caller says the switch is not safe yet', () => {
    const onToggle = vi.fn()
    render(<EditorToolbar editor={null} plainText={false} switchLocked onPickImages={noop} onTogglePlainText={onToggle} />)

    const toggle = screen.getByRole('button', { name: 'Plain text' })
    expect(toggle).toBeDisabled()
    fireEvent.click(toggle)
    expect(onToggle).not.toHaveBeenCalled()
  })

  // Paste and drop serve neither a keyboard nor a touch screen, which is what this button is for.
  it('hands a picked image to the composer', () => {
    const onPick = vi.fn()
    render(<EditorToolbar editor={null} plainText={false} onPickImages={onPick} onTogglePlainText={noop} />)
    const file = new File(['x'], 'shot.png', { type: 'image/png' })
    const input = screen.getByTestId('inline-image-input') as HTMLInputElement

    fireEvent.change(input, { target: { files: [file] } })

    expect(onPick).toHaveBeenCalledWith([file])
    // An input holding its value fires no change for the same file picked twice.
    expect(input.value).toBe('')
  })

  it('withholds the image button in plain-text mode, which has no body to insert into', () => {
    render(<EditorToolbar editor={null} plainText onPickImages={noop} onTogglePlainText={noop} />)

    expect(screen.queryByRole('button', { name: 'Insert image' })).toBeNull()
  })

  // The tray's own button, moved: attaching a file and inserting an image both open a picker, and
  // one of the two was costing a 61px band of its own. Outside the plain-text branch, since a
  // plain-text message carries attachments exactly like an HTML one.
  it('hands picked files to the composer, in both body formats', () => {
    const onAddFiles = vi.fn()
    const file = new File(['x'], 'facture.pdf', { type: 'application/pdf' })

    const { rerender } = render(<EditorToolbar editor={null} plainText={false}
      onPickImages={noop} onAddFiles={onAddFiles} onTogglePlainText={noop} />)
    const input = screen.getByTestId('attachment-input') as HTMLInputElement
    fireEvent.change(input, { target: { files: [file] } })
    expect(onAddFiles).toHaveBeenCalledWith([file])
    expect(input.value).toBe('')

    rerender(<EditorToolbar editor={null} plainText onPickImages={noop} onAddFiles={onAddFiles}
      onTogglePlainText={noop} />)
    expect(screen.getByRole('button', { name: 'Attach files' })).toBeInTheDocument()
  })

  // Three rows of tools left 102px of a 640px phone screen to the message. The rest is one tap
  // away rather than gone, and the groups keep their own markup: font, size, alignment and the
  // two colour swatches are menus and popovers, which cannot be nested inside another menu.
  describe('on a phone', () => {
    beforeEach(() => { mockViewport('phone') })
    afterEach(() => { resetViewport() })

    const toolbar = () => render(<EditorToolbar editor={fakeEditor()} plainText={false}
      onPickImages={noop} onAddFiles={noop} onTogglePlainText={noop} />)

    it('folds every group but the essentials behind one button', () => {
      const { container } = toolbar()

      expect(container.querySelector('.compose-toolbar')!.className).not.toContain('is-expanded')
      expect(screen.getByRole('button', { name: 'More formatting tools' }))
        .toHaveAttribute('aria-expanded', 'false')
      // Still rendered, and hidden by the stylesheet alone: unmounting them would drop the open
      // state of every popover among them, and the probe is what checks they are off screen.
      expect(container.querySelectorAll('.compose-tool-group.is-extra').length).toBeGreaterThan(0)
    })

    it('reveals them on the button and folds them back', () => {
      const { container } = toolbar()
      const more = screen.getByRole('button', { name: 'More formatting tools' })

      fireEvent.click(more)
      expect(container.querySelector('.compose-toolbar')!.className).toContain('is-expanded')
      expect(screen.getByRole('button', { name: 'Fewer formatting tools' }))
        .toHaveAttribute('aria-expanded', 'true')

      fireEvent.click(screen.getByRole('button', { name: 'Fewer formatting tools' }))
      expect(container.querySelector('.compose-toolbar')!.className).not.toContain('is-expanded')
    })

    it('offers no such button in plain-text mode, which draws no group to fold', () => {
      render(<EditorToolbar editor={null} plainText onPickImages={noop} onAddFiles={noop}
        onTogglePlainText={noop} />)

      expect(screen.queryByRole('button', { name: 'More formatting tools' })).toBeNull()
    })
  })

  it('draws every group and no fold button on a desktop', () => {
    const { container } = render(<EditorToolbar editor={fakeEditor()} plainText={false}
      onPickImages={noop} onAddFiles={noop} onTogglePlainText={noop} />)

    expect(screen.queryByRole('button', { name: 'More formatting tools' })).toBeNull()
    expect(container.querySelector('.compose-toolbar')!.className).not.toContain('is-expanded')
  })
})
