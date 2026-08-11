import { useEffect, useRef, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import { useViewport } from '../../../hooks/useViewport'
import PaperclipIcon from '../../../icons/PaperclipIcon'
import EllipsisIcon from '../../../icons/EllipsisIcon'
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
import ImageIcon from '../../../icons/ImageIcon'
import CheckIcon from '../../../icons/CheckIcon'
import PlainTextIcon from '../../../icons/PlainTextIcon'

/* The bar's icon scale. Its other half — the button, the B/I/U/S letters and the dropdown text —
   lives on .compose-toolbar in mail.css; change the two together or the glyphs stop being centred
   in their buttons. */
const ICON = 19
/** The two colour buttons stack their glyph over an ink bar, so it runs smaller than the rest. */
const INK_ICON = 17
const CHEVRON = 15

const SWATCHES = [
  '#000000', '#444444', '#666666', '#999999', '#cccccc', '#ffffff',
  '#d0021b', '#e2674a', '#f5a623', '#f8e71c', '#7ed321', '#417505',
  '#4a90d9', '#182238', '#9013fe', '#bd10e0', '#8b572a', '#50e3c2',
]
const FONTS = ['Arial', 'Georgia', 'Tahoma', 'Times New Roman', 'Verdana', 'Courier New']
const SIZES: { key: string; value: string }[] = [
  { key: 'small', value: '12px' }, { key: 'normal', value: '14px' },
  { key: 'large', value: '18px' }, { key: 'huge', value: '24px' },
]
type Alignment = 'left' | 'center' | 'right' | 'justify'
const ALIGNMENTS: { value: Alignment; Icon: typeof AlignLeftIcon }[] = [
  { value: 'left', Icon: AlignLeftIcon },
  { value: 'center', Icon: AlignCentreIcon },
  { value: 'right', Icon: AlignRightIcon },
  { value: 'justify', Icon: AlignJustifyIcon },
]

/** Both labels are written out rather than held on the rows: a key that reaches `t()` only as a
    variable is invisible to `src/locales/keys.test.ts`. */
function sizeLabel(key: string, t: TFunction<'compose'>): string {
  return key === 'small' ? t('toolbar.sizes.small')
    : key === 'large' ? t('toolbar.sizes.large')
      : key === 'huge' ? t('toolbar.sizes.huge') : t('toolbar.sizes.normal')
}
function alignLabel(value: Alignment, t: TFunction<'compose'>): string {
  return value === 'center' ? t('toolbar.align.center')
    : value === 'right' ? t('toolbar.align.right')
      : value === 'justify' ? t('toolbar.align.justify') : t('toolbar.align.left')
}

function Popover({ open, children }: { open: boolean; children: ReactNode }) {
  if (!open) return null
  return <div className="compose-popover">{children}</div>
}

interface Props {
  editor: EditorHandle | null
  active?: ActiveFormats
  plainText: boolean
  /** An inline upload is in flight: switching now strands the id it has not produced yet. */
  switchLocked?: boolean
  onTogglePlainText: () => void
  /** Picked images go through the composer's own routing, the one paste and drop already use. */
  onPickImages: (files: File[]) => void
  /** Attaching moved here from the tray, which is a list of files and not a place to add one. */
  onAddFiles?: (files: File[]) => void
}

export default function EditorToolbar(
  { editor, active, plainText, switchLocked, onTogglePlainText, onPickImages, onAddFiles }: Props) {
  const { t } = useTranslation('compose')
  // Three rows of tools left 102px of a 640px screen to the message. Only the phone folds: a
  // desktop has the width for all seven groups and would gain a button that hides its own tools.
  const narrow = useViewport() === 'phone'
  const [expanded, setExpanded] = useState(false)
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
  const picker = useRef<HTMLInputElement>(null)
  const attachPicker = useRef<HTMLInputElement>(null)

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

  const btn = (label: string, glyph: ReactNode, onClick: () => void, on = false, off = false) => (
    <button type="button" className={`compose-tool${on ? ' is-active' : ''}`} aria-pressed={on}
      aria-label={label} title={label} disabled={off} onClick={onClick}>
      {glyph}
    </button>
  )

  /** The menu row carries the tick, since the trigger no longer carries the value. */
  const chosen = (row: ReactNode, on: boolean) => (
    <span className="compose-menu-row">{row}{on && <CheckIcon size={14} />}</span>
  )

  /** An icon over the colour it applies, so the button says which colour it will lay down. */
  const inked = (icon: ReactNode, colour: string) => (
    <span className="compose-tool-stack">
      {icon}
      <span className="compose-tool-ink" style={{ background: colour }} />
    </span>
  )

  // `is-extra` is what the phone block folds away; the groups keep their own markup either way,
  // because font, size, alignment and the two swatch grids are menus and popovers, and a menu
  // cannot hold another. Hidden by the stylesheet rather than unmounted, so an open popover among
  // them is not destroyed by a fold — and so the tab order needs no second source of truth.
  return (
    <div className={`compose-toolbar${narrow && expanded ? ' is-expanded' : ''}`} ref={container}>
      {/* Stays when everything else folds away: it is the only way back to the editor. */}
      <div className="compose-tool-group">
        {btn(t('toolbar.plainText'), <PlainTextIcon size={ICON} />, onTogglePlainText, plainText, switchLocked)}
      </div>
      {/* Outside the plain-text branch below: a plain-text message carries attachments exactly
          like an HTML one, and this is the only button that adds one now. */}
      {onAddFiles && (
        <div className="compose-tool-group">
          <button type="button" className="compose-tool"
            aria-label={t('attachments.attach')} title={t('attachments.attach')}
            onClick={() => attachPicker.current?.click()}>
            <PaperclipIcon size={ICON} />
          </button>
          <input ref={attachPicker} type="file" multiple hidden data-testid="attachment-input"
            onChange={e => {
              const files = e.target.files
              // Cleared straight away: an input keeping its value fires no change for the same file twice.
              if (files?.length) { onAddFiles(Array.from(files)); e.target.value = '' }
            }} />
        </div>
      )}
      {plainText ? null : (
      <>
      <div className="compose-tool-group is-extra">
        {btn(t('toolbar.undo'), <UndoIcon size={ICON} />, () => editor?.command('undo'))}
        {btn(t('toolbar.redo'), <RedoIcon size={ICON} />, () => editor?.command('redo'))}
      </div>
      <div className="compose-tool-group">
        {btn(t('toolbar.bold'), <b>B</b>, () => editor?.command('bold'), active?.bold)}
        {btn(t('toolbar.italic'), <i>I</i>, () => editor?.command('italic'), active?.italic)}
        {btn(t('toolbar.underline'), <u>U</u>, () => editor?.command('underline'), active?.underline)}
        {btn(t('toolbar.strikethrough'), <s>S</s>, () => editor?.command('strikethrough'), active?.strikethrough)}
      </div>
      <div className="compose-tool-group is-extra">
        <span className="compose-popover-anchor">
          {btn(t('toolbar.textColour'), inked(<TextColourIcon size={INK_ICON} />, textColour),
            () => setOpenPopover(p => p === 'text' ? null : 'text'))}
          <Popover open={openPopover === 'text'}>
            {swatchGrid(c => { setTextColour(c); editor?.setTextColour(c) })}
          </Popover>
        </span>
        <span className="compose-popover-anchor">
          {btn(t('toolbar.highlightColour'), inked(<HighlighterIcon size={INK_ICON} />, highlight),
            () => setOpenPopover(p => p === 'highlight' ? null : 'highlight'))}
          <Popover open={openPopover === 'highlight'}>
            {swatchGrid(c => { setHighlight(c); editor?.setHighlightColour(c) })}
          </Popover>
        </span>
      </div>
      <div className="compose-tool-group is-extra">
        {/* Icon only: spelling the value on the trigger made the bar a different width per font,
            so it re-flowed under the user at the moment of choosing. The choice moved into the
            menu, which is the one place it was ever read from — the bar cannot see the format at
            the caret and only ever echoed the last pick. */}
        <DropdownMenu ariaLabel={t('toolbar.font')} align="left" className="compose-tool-select"
          trigger={<><FontIcon size={ICON} /><ChevronDownIcon size={CHEVRON} /></>}
          items={FONTS.map(name => ({
            label: name,
            node: chosen(<span style={{ fontFamily: name }}>{name}</span>, name === font),
            onSelect: () => { setFont(name); editor?.setFontFace(name) },
          }))} />
        <DropdownMenu ariaLabel={t('toolbar.size')} align="left" className="compose-tool-select"
          trigger={<><TextSizeIcon size={ICON} /><ChevronDownIcon size={CHEVRON} /></>}
          items={SIZES.map(entry => ({
            label: sizeLabel(entry.key, t),
            node: chosen(
              <span style={{ fontSize: entry.value }}>{sizeLabel(entry.key, t)}</span>,
              entry.key === size.key),
            onSelect: () => { setSize(entry); editor?.setFontSize(entry.value) },
          }))} />
        <DropdownMenu ariaLabel={t('toolbar.alignment')} align="left" className="compose-tool-select"
          trigger={<><alignment.Icon size={ICON} /><ChevronDownIcon size={CHEVRON} /></>}
          items={ALIGNMENTS.map(entry => ({
            label: alignLabel(entry.value, t),
            icon: <entry.Icon size={14} />,
            onSelect: () => { setAlignment(entry); editor?.setAlignment(entry.value) },
          }))} />
      </div>
      <div className="compose-tool-group is-extra">
        {btn(t('toolbar.bulletedList'), <ListBulletIcon size={ICON} />, () => editor?.command('unorderedList'), active?.unorderedList)}
        {btn(t('toolbar.numberedList'), <ListOrderedIcon size={ICON} />, () => editor?.command('orderedList'), active?.orderedList)}
        {/* Squire exposes quote level only, so indent and quote are one pair of buttons. */}
        {btn(t('toolbar.increaseQuote'), <IndentIcon size={ICON} />, () => editor?.command('increaseQuote'))}
        {btn(t('toolbar.decreaseQuote'), <OutdentIcon size={ICON} />, () => editor?.command('decreaseQuote'))}
      </div>
      <div className="compose-tool-group is-extra">
        <span className="compose-popover-anchor">
          {btn(t('toolbar.link'), <LinkIcon size={ICON} />, () => setOpenPopover(p => p === 'link' ? null : 'link'))}
          <Popover open={openPopover === 'link'}>
            <div className="compose-link-form">
              <label htmlFor="compose-link-url">{t('toolbar.linkUrl')}</label>
              <input id="compose-link-url" type="url" value={url} onChange={e => setUrl(e.target.value)} />
              <button type="button" className="btn btn-primary" disabled={!url}
                onClick={() => { editor?.makeLink(url); setUrl(''); setOpenPopover(null) }}>
                {t('toolbar.apply')}
              </button>
            </div>
          </Popover>
        </span>
        {btn(t('toolbar.removeLink'), <UnlinkIcon size={ICON} />, () => editor?.command('removeLink'))}
        {/* Not built from btn: that one stamps aria-pressed on everything, and this opens a picker
            rather than holding a state. */}
        <button type="button" className="compose-tool"
          aria-label={t('toolbar.insertImage')} title={t('toolbar.insertImage')}
          onClick={() => picker.current?.click()}>
          <ImageIcon size={ICON} />
        </button>
        {/* The keyboard and touch way in, next to the paste and the drop that cannot serve either. */}
        <input ref={picker} type="file" accept="image/*" multiple hidden data-testid="inline-image-input"
          onChange={e => {
            const files = e.target.files
            // Cleared straight away: an input keeping its value fires no change for the same file twice.
            if (files?.length) { onPickImages(Array.from(files)); e.target.value = '' }
          }} />
        {btn(t('toolbar.clearFormatting'), <ClearFormatIcon size={ICON} />, () => editor?.command('clearFormatting'))}
      </div>
      {/* Last, so the collapsed row reads as the tools it keeps followed by the ones it does not.
          Only on a phone: elsewhere every group is drawn and this would hide working tools. */}
      {narrow && (
        <div className="compose-tool-group">
          <button type="button" className="compose-tool"
            aria-expanded={expanded}
            aria-label={t(expanded ? 'toolbar.fewerTools' : 'toolbar.moreTools')}
            title={t(expanded ? 'toolbar.fewerTools' : 'toolbar.moreTools')}
            onClick={() => setExpanded(v => !v)}>
            <EllipsisIcon size={ICON} />
          </button>
        </div>
      )}
      </>
      )}
    </div>
  )
}
