import { useEffect, useRef, useState, type ReactNode } from 'react'
import type { ActiveFormats, EditorHandle } from './SquireEditor'
import DropdownMenu from '../../../components/DropdownMenu'
import ChevronDownIcon from '../../../icons/ChevronDownIcon'
import UndoIcon from '../../../icons/UndoIcon'
import RedoIcon from '../../../icons/RedoIcon'
import TextColourIcon from '../../../icons/TextColourIcon'
import HighlighterIcon from '../../../icons/HighlighterIcon'
import FontIcon from '../../../icons/FontIcon'
import TextSizeIcon from '../../../icons/TextSizeIcon'
import AlignLeftIcon from '../../../icons/AlignLeftIcon'
import AlignCentreIcon from '../../../icons/AlignCentreIcon'
import AlignRightIcon from '../../../icons/AlignRightIcon'
import AlignJustifyIcon from '../../../icons/AlignJustifyIcon'
import ListBulletIcon from '../../../icons/ListBulletIcon'
import ListOrderedIcon from '../../../icons/ListOrderedIcon'
import IndentIcon from '../../../icons/IndentIcon'
import OutdentIcon from '../../../icons/OutdentIcon'
import LinkIcon from '../../../icons/LinkIcon'
import UnlinkIcon from '../../../icons/UnlinkIcon'
import ClearFormatIcon from '../../../icons/ClearFormatIcon'

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
type Alignment = 'left' | 'center' | 'right' | 'justify'
const ALIGNMENTS: { label: string; value: Alignment; Icon: typeof AlignLeftIcon }[] = [
  { label: 'Left', value: 'left', Icon: AlignLeftIcon },
  { label: 'Center', value: 'center', Icon: AlignCentreIcon },
  { label: 'Right', value: 'right', Icon: AlignRightIcon },
  { label: 'Justify', value: 'justify', Icon: AlignJustifyIcon },
]

function Popover({ open, children }: { open: boolean; children: ReactNode }) {
  if (!open) return null
  return <div className="compose-popover">{children}</div>
}

interface Props { editor: EditorHandle | null; active?: ActiveFormats }

export default function EditorToolbar({ editor, active }: Props) {
  const [openPopover, setOpenPopover] = useState<'text' | 'highlight' | 'link' | null>(null)
  const [url, setUrl] = useState('')
  // The editor reports no font, size or colour at the caret, so these are the last choice made
  // here — the same thing the <select>s' defaultValue used to show.
  const [font, setFont] = useState(FONTS[0])
  const [size, setSize] = useState(SIZES[1])
  const [alignment, setAlignment] = useState(ALIGNMENTS[0])
  const [textColour, setTextColour] = useState('currentColor')
  const [highlight, setHighlight] = useState('#f8e71c')
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

  /** An icon over the colour it applies, so the button says which colour it will lay down. */
  const inked = (icon: ReactNode, colour: string) => (
    <span className="compose-tool-stack">
      {icon}
      <span className="compose-tool-ink" style={{ background: colour }} />
    </span>
  )

  return (
    <div className="compose-toolbar" ref={container}>
      <div className="compose-tool-group">
        {btn('Undo', <UndoIcon />, () => editor?.command('undo'))}
        {btn('Redo', <RedoIcon />, () => editor?.command('redo'))}
      </div>
      <div className="compose-tool-group">
        {btn('Bold', <b>B</b>, () => editor?.command('bold'), active?.bold)}
        {btn('Italic', <i>I</i>, () => editor?.command('italic'), active?.italic)}
        {btn('Underline', <u>U</u>, () => editor?.command('underline'), active?.underline)}
        {btn('Strikethrough', <s>S</s>, () => editor?.command('strikethrough'), active?.strikethrough)}
      </div>
      <div className="compose-tool-group">
        <span className="compose-popover-anchor">
          {btn('Text colour', inked(<TextColourIcon size={14} />, textColour),
            () => setOpenPopover(p => p === 'text' ? null : 'text'))}
          <Popover open={openPopover === 'text'}>
            {swatchGrid(c => { setTextColour(c); editor?.setTextColour(c) })}
          </Popover>
        </span>
        <span className="compose-popover-anchor">
          {btn('Highlight colour', inked(<HighlighterIcon size={14} />, highlight),
            () => setOpenPopover(p => p === 'highlight' ? null : 'highlight'))}
          <Popover open={openPopover === 'highlight'}>
            {swatchGrid(c => { setHighlight(c); editor?.setHighlightColour(c) })}
          </Popover>
        </span>
      </div>
      <div className="compose-tool-group">
        <DropdownMenu ariaLabel="Font" align="left" className="compose-tool-select"
          trigger={<><FontIcon size={14} />{font}<ChevronDownIcon size={12} /></>}
          items={FONTS.map(name => ({
            label: name,
            node: <span style={{ fontFamily: name }}>{name}</span>,
            onSelect: () => { setFont(name); editor?.setFontFace(name) },
          }))} />
        <DropdownMenu ariaLabel="Size" align="left" className="compose-tool-select"
          trigger={<><TextSizeIcon size={14} />{size.label}<ChevronDownIcon size={12} /></>}
          items={SIZES.map(entry => ({
            label: entry.label,
            node: <span style={{ fontSize: entry.value }}>{entry.label}</span>,
            onSelect: () => { setSize(entry); editor?.setFontSize(entry.value) },
          }))} />
        <DropdownMenu ariaLabel="Alignment" align="left" className="compose-tool-select"
          trigger={<><alignment.Icon size={14} /><ChevronDownIcon size={12} /></>}
          items={ALIGNMENTS.map(entry => ({
            label: entry.label,
            icon: <entry.Icon size={14} />,
            onSelect: () => { setAlignment(entry); editor?.setAlignment(entry.value) },
          }))} />
      </div>
      <div className="compose-tool-group">
        {btn('Bulleted list', <ListBulletIcon />, () => editor?.command('unorderedList'), active?.unorderedList)}
        {btn('Numbered list', <ListOrderedIcon />, () => editor?.command('orderedList'), active?.orderedList)}
        {/* Squire exposes quote level only, so indent and quote are one pair of buttons. */}
        {btn('Increase quote', <IndentIcon />, () => editor?.command('increaseQuote'))}
        {btn('Decrease quote', <OutdentIcon />, () => editor?.command('decreaseQuote'))}
      </div>
      <div className="compose-tool-group">
        <span className="compose-popover-anchor">
          {btn('Link', <LinkIcon />, () => setOpenPopover(p => p === 'link' ? null : 'link'))}
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
        {btn('Remove link', <UnlinkIcon />, () => editor?.command('removeLink'))}
        {btn('Clear formatting', <ClearFormatIcon />, () => editor?.command('clearFormatting'))}
      </div>
    </div>
  )
}
