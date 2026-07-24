import { describe, it, expect, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import EditorToolbar from './EditorToolbar'
import type { EditorHandle } from './SquireEditor'

function fakeEditor(): EditorHandle {
  return {
    getHTML: vi.fn(() => ''), isEmpty: vi.fn(() => true), focus: vi.fn(),
    command: vi.fn(), setTextColour: vi.fn(), setHighlightColour: vi.fn(),
    setFontFace: vi.fn(), setFontSize: vi.fn(), setAlignment: vi.fn(), makeLink: vi.fn(),
  }
}

describe('EditorToolbar', () => {
  it('relays a format button to the editor', () => {
    const editor = fakeEditor()
    render(<EditorToolbar editor={editor} />)
    fireEvent.click(screen.getByRole('button', { name: 'Bold' }))
    expect(editor.command).toHaveBeenCalledWith('bold')
  })

  it('applies a text colour from the swatch grid', () => {
    const editor = fakeEditor()
    render(<EditorToolbar editor={editor} />)
    fireEvent.click(screen.getByRole('button', { name: 'Text colour' }))
    fireEvent.click(screen.getByRole('button', { name: '#d0021b' }))
    expect(editor.setTextColour).toHaveBeenCalledWith('#d0021b')
  })

  it('applies a highlight colour from its own swatch grid', () => {
    const editor = fakeEditor()
    render(<EditorToolbar editor={editor} />)
    fireEvent.click(screen.getByRole('button', { name: 'Highlight colour' }))
    fireEvent.click(screen.getByRole('button', { name: '#f8e71c' }))
    expect(editor.setHighlightColour).toHaveBeenCalledWith('#f8e71c')
    expect(editor.setTextColour).not.toHaveBeenCalled()
  })

  it('closes a popover on an outside mousedown', () => {
    const editor = fakeEditor()
    render(<EditorToolbar editor={editor} />)
    fireEvent.click(screen.getByRole('button', { name: 'Text colour' }))
    expect(screen.getByRole('button', { name: '#d0021b' })).toBeInTheDocument()
    fireEvent.mouseDown(document.body)
    expect(screen.queryByRole('button', { name: '#d0021b' })).not.toBeInTheDocument()
  })

  it('applies font, size and alignment from the selects', () => {
    const editor = fakeEditor()
    render(<EditorToolbar editor={editor} />)
    fireEvent.change(screen.getByLabelText('Font'), { target: { value: 'Georgia' } })
    fireEvent.change(screen.getByLabelText('Size'), { target: { value: '18px' } })
    fireEvent.change(screen.getByLabelText('Alignment'), { target: { value: 'center' } })
    expect(editor.setFontFace).toHaveBeenCalledWith('Georgia')
    expect(editor.setFontSize).toHaveBeenCalledWith('18px')
    expect(editor.setAlignment).toHaveBeenCalledWith('center')
  })

  it('offers the four sizes as Small/Normal/Large/Huge', () => {
    render(<EditorToolbar editor={fakeEditor()} />)
    const sizes = screen.getByLabelText('Size') as HTMLSelectElement
    expect([...sizes.options].map(o => [o.text, o.value])).toEqual([
      ['Small', '12px'], ['Normal', '14px'], ['Large', '18px'], ['Huge', '24px'],
    ])
  })

  it('inserts a link through the URL popover', () => {
    const editor = fakeEditor()
    render(<EditorToolbar editor={editor} />)
    fireEvent.click(screen.getByRole('button', { name: 'Link' }))
    fireEvent.change(screen.getByLabelText('Link URL'), { target: { value: 'https://weesky.net' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply' }))
    expect(editor.makeLink).toHaveBeenCalledWith('https://weesky.net')
  })

  it('closes the link popover and clears the URL after applying', () => {
    const editor = fakeEditor()
    render(<EditorToolbar editor={editor} />)
    fireEvent.click(screen.getByRole('button', { name: 'Link' }))
    fireEvent.change(screen.getByLabelText('Link URL'), { target: { value: 'https://weesky.net' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply' }))
    expect(screen.queryByLabelText('Link URL')).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Link' }))
    expect(screen.getByLabelText('Link URL')).toHaveValue('')
  })

  it('ships one pair of quote buttons, no separate indent pair', () => {
    const editor = fakeEditor()
    render(<EditorToolbar editor={editor} />)
    fireEvent.click(screen.getByRole('button', { name: 'Increase quote' }))
    fireEvent.click(screen.getByRole('button', { name: 'Decrease quote' }))
    expect(editor.command).toHaveBeenNthCalledWith(1, 'increaseQuote')
    expect(editor.command).toHaveBeenNthCalledWith(2, 'decreaseQuote')
    expect(screen.queryByRole('button', { name: /indent/i })).not.toBeInTheDocument()
  })

  it('does nothing without an editor', () => {
    render(<EditorToolbar editor={null} />)
    fireEvent.click(screen.getByRole('button', { name: 'Bold' }))
    fireEvent.change(screen.getByLabelText('Font'), { target: { value: 'Georgia' } })
    // no throw is the assertion
  })
})
