import { afterEach, beforeEach, describe, it, expect, vi } from 'vitest'
import { fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { mockViewport, resetViewport } from '../../../test-utils'
import { expectNoAxeViolations } from '../../../a11y-test'
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

const NAMES = ['Black', 'Dark grey', 'Grey', 'Light grey', 'Silver', 'White',
  'Red', 'Coral', 'Amber', 'Yellow', 'Green', 'Olive',
  'Blue', 'Navy', 'Purple', 'Magenta', 'Brown', 'Turquoise']

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
    fireEvent.click(screen.getByRole('button', { name: 'Red' }))
    expect(editor.setTextColour).toHaveBeenCalledWith('#d0021b')
  })

  it('applies a highlight colour from its own swatch grid', () => {
    const editor = fakeEditor()
    render(<EditorToolbar editor={editor} plainText={false} onPickImages={noop} onTogglePlainText={noop} />)
    fireEvent.click(screen.getByRole('button', { name: 'Highlight colour' }))
    fireEvent.click(screen.getByRole('button', { name: 'Yellow' }))
    expect(editor.setHighlightColour).toHaveBeenCalledWith('#f8e71c')
    expect(editor.setTextColour).not.toHaveBeenCalled()
  })

  it('shows the last applied colour under its button', () => {
    render(<EditorToolbar editor={fakeEditor()} plainText={false} onPickImages={noop} onTogglePlainText={noop} />)
    fireEvent.click(screen.getByRole('button', { name: 'Text colour' }))
    fireEvent.click(screen.getByRole('button', { name: 'Red' }))
    expect(screen.getByRole('button', { name: 'Text colour' })
      .querySelector('.compose-tool-ink')).toHaveStyle({ background: '#d0021b' })
  })

  it('closes a popover on an outside mousedown', () => {
    const editor = fakeEditor()
    render(<EditorToolbar editor={editor} plainText={false} onPickImages={noop} onTogglePlainText={noop} />)
    fireEvent.click(screen.getByRole('button', { name: 'Text colour' }))
    expect(screen.getByRole('button', { name: 'Red' })).toBeInTheDocument()
    fireEvent.mouseDown(document.body)
    expect(screen.queryByRole('button', { name: 'Red' })).not.toBeInTheDocument()
  })

  it('closes a popover on Escape and hands the focus back to its button', async () => {
    render(<EditorToolbar editor={fakeEditor()} plainText={false} onPickImages={noop} onTogglePlainText={noop} />)
    const trigger = screen.getByRole('button', { name: 'Highlight colour' })
    await userEvent.click(trigger)
    screen.getByRole('button', { name: 'Yellow' }).focus()

    fireEvent.keyDown(document, { key: 'Escape' })

    expect(screen.queryByRole('button', { name: 'Yellow' })).not.toBeInTheDocument()
    expect(trigger).toHaveFocus()
  })

  // The URL box holds the focus, and the popover it lives in is gone the moment it is applied.
  it('hands the focus back to the Link button once a URL is applied', async () => {
    render(<EditorToolbar editor={fakeEditor()} plainText={false} onPickImages={noop} onTogglePlainText={noop} />)
    const trigger = screen.getByRole('button', { name: 'Link' })
    await userEvent.click(trigger)
    fireEvent.change(screen.getByLabelText('Link URL'), { target: { value: 'https://weesky.net' } })

    await userEvent.click(screen.getByRole('button', { name: 'Apply' }))

    expect(trigger).toHaveFocus()
  })

  // `fireEvent.click` (unlike userEvent.click) does not move focus in jsdom, which stands in for
  // Safari not focusing a button on click — `document.activeElement` stays on `elsewhere` while
  // the popover opens, and the close must still land on the button that opened it.
  function closeBySwatch(colour: string) {
    const swatch = screen.getByRole('button', { name: colour })
    swatch.focus()
    fireEvent.click(swatch)
  }

  it.each([
    { trigger: 'Text colour', atOpen: (elsewhere: HTMLElement) => elsewhere,
      close: () => closeBySwatch('Red') },
    { trigger: 'Highlight colour', atOpen: (elsewhere: HTMLElement) => elsewhere,
      close: () => closeBySwatch('Yellow') },
    { trigger: 'Link', atOpen: () => screen.getByLabelText('Link URL'), close: () => {
      fireEvent.change(screen.getByLabelText('Link URL'), { target: { value: 'https://weesky.net' } })
      const applyBtn = screen.getByRole('button', { name: 'Apply' })
      applyBtn.focus()
      fireEvent.click(applyBtn)
    } },
  ])('refocuses its own trigger, not whatever held focus when it opened ($trigger)', ({ trigger, atOpen, close }) => {
    const { container } = render(
      <EditorToolbar editor={fakeEditor()} plainText={false} onPickImages={noop} onTogglePlainText={noop} />)
    const elsewhere = document.createElement('button')
    container.appendChild(elsewhere)
    elsewhere.focus()

    const triggerBtn = screen.getByRole('button', { name: trigger })
    fireEvent.click(triggerBtn)
    // Sanity, per surface: a colour grid places no focus, so `elsewhere` still holds it, while the
    // link form takes it onto its URL box. Neither is the trigger, which is what the close finds.
    expect(atOpen(elsewhere)).toHaveFocus()

    close()

    expect(triggerBtn).toHaveFocus()
  })

  it('refocuses the highlight trigger, not the last text-colour trigger, when both popovers were used', () => {
    render(<EditorToolbar editor={fakeEditor()} plainText={false} onPickImages={noop} onTogglePlainText={noop} />)
    fireEvent.click(screen.getByRole('button', { name: 'Text colour' }))
    const firstSwatch = screen.getByRole('button', { name: 'Red' })
    firstSwatch.focus()
    fireEvent.click(firstSwatch)

    const highlightTrigger = screen.getByRole('button', { name: 'Highlight colour' })
    fireEvent.click(highlightTrigger)
    const secondSwatch = screen.getByRole('button', { name: 'Yellow' })
    secondSwatch.focus()
    fireEvent.click(secondSwatch)

    expect(highlightTrigger).toHaveFocus()
  })

  /* Eighteen swatches in six columns are a grid, which the menu pattern cannot express — ←/→ are
     a submenu's there and Home/End name no corner. The trigger still names it, which is also what
     stops that trigger announcing itself as an unpressed toggle. */
  describe('the swatch grid', () => {
    const toolbar = () => render(<EditorToolbar editor={fakeEditor()} plainText={false}
      onPickImages={noop} onTogglePlainText={noop} />)

    function open(trigger: string) {
      const rendered = toolbar()
      fireEvent.click(screen.getByRole('button', { name: trigger }))
      return rendered
    }

    const grid = (name: string) => screen.getByRole('grid', { name })
    const stops = (name: string) => within(grid(name)).getAllByRole('button')
      .map(button => button.getAttribute('tabindex'))

    it('is three rows of six cells, named by the trigger that opened it', () => {
      open('Text colour')

      expect(grid('Text colour').className).toContain('compose-swatches')
      const rows = within(grid('Text colour')).getAllByRole('row')
      expect(rows).toHaveLength(3)
      for (const row of rows) expect(within(row).getAllByRole('gridcell')).toHaveLength(6)
    })

    it('names each colour instead of reading its hex code', () => {
      open('Text colour')

      expect(within(grid('Text colour')).getAllByRole('button')
        .map(button => button.getAttribute('aria-label'))).toEqual(NAMES)
    })

    it('offers one tab stop for the eighteen swatches', () => {
      open('Text colour')

      expect(stops('Text colour')).toEqual(['0', ...Array<string>(17).fill('-1')])
    })

    // The stop opens where the state is: the highlight in force is yellow, not black.
    it('puts the tab stop on the colour already applied', () => {
      open('Highlight colour')

      expect(screen.getByRole('button', { name: 'Yellow' })).toHaveAttribute('aria-pressed', 'true')
      expect(stops('Highlight colour'))
        .toEqual([...Array<string>(9).fill('-1'), '0', ...Array<string>(8).fill('-1')])
    })

    it('walks the two dimensions with the arrow keys', async () => {
      open('Text colour')
      screen.getByRole('button', { name: 'Black' }).focus()

      await userEvent.keyboard('{ArrowRight}')
      expect(screen.getByRole('button', { name: 'Dark grey' })).toHaveFocus()

      await userEvent.keyboard('{ArrowDown}')
      expect(screen.getByRole('button', { name: 'Coral' })).toHaveFocus()

      await userEvent.keyboard('{End}')
      expect(screen.getByRole('button', { name: 'Olive' })).toHaveFocus()
    })

    /* The grid spends no Escape — it opts out of `cellEntry`, so the key passes it and reaches the
       layer the popover registered. Fired from the focused swatch, the way a real press arrives. */
    it('leaves Escape to the popover, which closes from a swatch holding the focus', async () => {
      open('Text colour')
      const trigger = screen.getByRole('button', { name: 'Text colour' })
      screen.getByRole('button', { name: 'Red' }).focus()

      await userEvent.keyboard('{Escape}')

      expect(screen.queryByRole('grid', { name: 'Text colour' })).toBeNull()
      expect(trigger).toHaveFocus()
    })

    // A trigger that opens a surface is not a toggle: aria-pressed would announce it unpressed.
    it('marks its trigger expanded rather than pressed', () => {
      toolbar()
      const trigger = screen.getByRole('button', { name: 'Highlight colour' })
      expect(trigger).not.toHaveAttribute('aria-pressed')
      expect(trigger).toHaveAttribute('aria-expanded', 'false')

      fireEvent.click(trigger)

      expect(trigger).toHaveAttribute('aria-expanded', 'true')
    })

    it('carries no accessibility violation', async () => {
      const { container } = open('Text colour')

      await expectNoAxeViolations(container)
    })
  })

  // A form is opened to be filled in, so it takes the focus onto its field — by a ref, never
  // `autoFocus`. The colour grids place no focus at all; Tab reaches them from their trigger.
  it('opens the link popover on its URL box', () => {
    render(<EditorToolbar editor={fakeEditor()} plainText={false} onPickImages={noop} onTogglePlainText={noop} />)

    fireEvent.click(screen.getByRole('button', { name: 'Link' }))

    expect(screen.getByLabelText('Link URL')).toHaveFocus()
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
