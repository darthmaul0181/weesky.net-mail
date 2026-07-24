import { useEffect, useRef, useState, type ReactNode } from 'react'
import type { ActiveFormats, EditorHandle } from './SquireEditor'

const SWATCHES = [
  '#000000', '#444444', '#666666', '#999999', '#cccccc', '#ffffff',
  '#d0021b', '#e2674a', '#f5a623', '#f8e71c', '#7ed321', '#417505',
  '#4a90d9', '#182238', '#9013fe', '#bd10e0', '#8b572a', '#50e3c2',
]
const FONTS = ['Arial', 'Georgia', 'Tahoma', 'Times New Roman', 'Verdana', 'Courier New']
const SIZES = [
  { label: 'Small', value: '12px' }, { label: 'Normal', value: '14px' },
  { label: 'Large', value: '18px' }, { label: 'Huge', value: '24px' },
]

function Popover({ open, children }: { open: boolean; children: ReactNode }) {
  if (!open) return null
  return <div className="compose-popover">{children}</div>
}

interface Props { editor: EditorHandle | null; active?: ActiveFormats }

export default function EditorToolbar({ editor, active }: Props) {
  const [openPopover, setOpenPopover] = useState<'text' | 'highlight' | 'link' | null>(null)
  const [url, setUrl] = useState('')
  const container = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!openPopover) return
    function onDown(event: MouseEvent) {
      if (!container.current?.contains(event.target as Node)) setOpenPopover(null)
    }
    document.addEventListener('mousedown', onDown)
    return () => document.removeEventListener('mousedown', onDown)
  }, [openPopover])

  function swatchGrid(apply: (colour: string) => void) {
    return (
      <div className="compose-swatches">
        {SWATCHES.map(colour => (
          <button key={colour} type="button" aria-label={colour} style={{ background: colour }}
            onClick={() => { apply(colour); setOpenPopover(null) }} />
        ))}
      </div>
    )
  }

  const btn = (label: string, glyph: ReactNode, onClick: () => void, on = false) => (
    <button type="button" className={`compose-tool${on ? ' is-active' : ''}`} aria-pressed={on}
      aria-label={label} title={label} onClick={onClick}>
      {glyph}
    </button>
  )

  return (
    <div className="compose-toolbar" ref={container}>
      {btn('Undo', '↶', () => editor?.command('undo'))}
      {btn('Redo', '↷', () => editor?.command('redo'))}
      <span className="compose-toolbar-rule" />
      {btn('Bold', <b>B</b>, () => editor?.command('bold'), active?.bold)}
      {btn('Italic', <i>I</i>, () => editor?.command('italic'), active?.italic)}
      {btn('Underline', <u>U</u>, () => editor?.command('underline'), active?.underline)}
      {btn('Strikethrough', <s>S</s>, () => editor?.command('strikethrough'), active?.strikethrough)}
      <span className="compose-toolbar-rule" />
      <span className="compose-popover-anchor">
        {btn('Text colour', 'A', () => setOpenPopover(p => p === 'text' ? null : 'text'))}
        <Popover open={openPopover === 'text'}>{swatchGrid(c => editor?.setTextColour(c))}</Popover>
      </span>
      <span className="compose-popover-anchor">
        {btn('Highlight colour', '▩', () => setOpenPopover(p => p === 'highlight' ? null : 'highlight'))}
        <Popover open={openPopover === 'highlight'}>{swatchGrid(c => editor?.setHighlightColour(c))}</Popover>
      </span>
      <span className="compose-select">
        <select aria-label="Font" title="Font" defaultValue="Arial"
          onChange={e => editor?.setFontFace(e.target.value)}>
          {FONTS.map(font => <option key={font} value={font}>{font}</option>)}
        </select>
      </span>
      <span className="compose-select">
        <select aria-label="Size" title="Size" defaultValue="14px"
          onChange={e => editor?.setFontSize(e.target.value)}>
          {SIZES.map(size => <option key={size.value} value={size.value}>{size.label}</option>)}
        </select>
      </span>
      <span className="compose-select">
        <select aria-label="Alignment" title="Alignment" defaultValue="left"
          onChange={e => editor?.setAlignment(e.target.value as 'left' | 'center' | 'right' | 'justify')}>
          <option value="left">Left</option><option value="center">Center</option>
          <option value="right">Right</option><option value="justify">Justify</option>
        </select>
      </span>
      <span className="compose-toolbar-rule" />
      {btn('Bulleted list', '•', () => editor?.command('unorderedList'), active?.unorderedList)}
      {btn('Numbered list', '1.', () => editor?.command('orderedList'), active?.orderedList)}
      {/* Squire exposes quote level only, so indent and quote are one pair of buttons. */}
      {btn('Increase quote', '❯❯', () => editor?.command('increaseQuote'))}
      {btn('Decrease quote', '❮❮', () => editor?.command('decreaseQuote'))}
      <span className="compose-toolbar-rule" />
      <span className="compose-popover-anchor">
        {btn('Link', '🔗', () => setOpenPopover(p => p === 'link' ? null : 'link'))}
        <Popover open={openPopover === 'link'}>
          <div className="compose-link-form">
            <label htmlFor="compose-link-url">Link URL</label>
            <input id="compose-link-url" type="url" value={url} onChange={e => setUrl(e.target.value)} />
            <button type="button" className="btn btn-primary" disabled={!url}
              onClick={() => { editor?.makeLink(url); setUrl(''); setOpenPopover(null) }}>
              Apply
            </button>
          </div>
        </Popover>
      </span>
      {btn('Remove link', '⛓', () => editor?.command('removeLink'))}
      {btn('Clear formatting', '⌫', () => editor?.command('clearFormatting'))}
    </div>
  )
}
