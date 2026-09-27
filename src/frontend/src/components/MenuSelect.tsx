import {
  type CSSProperties, type KeyboardEvent, type ReactNode, useEffect, useId, useLayoutEffect, useRef, useState,
} from 'react'
import { useDismiss } from '../hooks/useDismiss'
import { fold } from '../lib/fold'
import CheckIcon from '../icons/CheckIcon'
import ChevronDownIcon from '../icons/ChevronDownIcon'

export interface SelectOption<V extends string = string> {
  value: V
  /** What the row says, what typing a letter matches, and what sizes the box. */
  label: string
  /** Rich rendering for the row and the box; `label` stays the text. */
  node?: ReactNode
  icon?: ReactNode
  disabled?: boolean
  /** A level in a tree (a folder under its parent), drawn in the list only. */
  depth?: number
}

interface Props<V extends string> {
  value: V
  options: SelectOption<V>[]
  onChange: (value: V) => void
  /** For a `<label htmlFor>`: a combobox is named by its label, and announces its value. */
  id?: string
  ariaLabel?: string
  describedBy?: string
  disabled?: boolean
  /** On the root, which is the flex or grid item wherever the select stands. */
  className?: string
  /** What the box shows when it must name something the list does not offer (a stale identity). */
  display?: ReactNode
  /** The edge shared with the box when the list is wider than it. */
  align?: 'left' | 'right'
}

const TYPEAHEAD_MS = 600

/** The site's one select: a button drawn as a box, opening a listbox under it (APG select-only
 * combobox). Focus stays on the box and the active row is `aria-activedescendant`, so a dialog
 * around it keeps its Tab order. The box is as wide as its widest option, like a native select. */
export default function MenuSelect<V extends string>({
  value, options, onChange, id, ariaLabel, describedBy, disabled, className, display, align = 'left',
}: Props<V>) {
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(-1)
  const [up, setUp] = useState(false)
  const listId = useId()
  const rootRef = useRef<HTMLDivElement>(null)
  const boxRef = useRef<HTMLButtonElement>(null)
  const listRef = useRef<HTMLUListElement>(null)
  const typed = useRef({ text: '', at: 0 })
  const chosen = options.findIndex(o => o.value === value)
  const current = options[chosen]

  useDismiss({ open, rootRef, onDismiss: () => setOpen(false), refocusRef: boxRef })

  // Down unless the list spills below and fits whole above; a list too tall for both stays down,
  // where its own scroll reaches every row.
  useLayoutEffect(() => {
    if (!open || !boxRef.current || !listRef.current) return
    const box = boxRef.current.getBoundingClientRect(), height = listRef.current.offsetHeight
    setUp(window.innerHeight - box.bottom < height + 8 && box.top >= height + 8)
  }, [open])

  useEffect(() => {
    if (open && active >= 0) listRef.current?.children[active]?.scrollIntoView?.({ block: 'nearest' })
  }, [open, active])

  const enabled = (index: number) => !!options[index] && !options[index].disabled
  function step(from: number, by: 1 | -1) {
    for (let i = from + by; i >= 0 && i < options.length; i += by) if (enabled(i)) return i
    return from
  }
  const first = () => step(-1, 1)
  const last = () => step(options.length, -1)

  function show(at: number) {
    setActive(at)
    setOpen(true)
  }

  function choose(index: number) {
    const option = options[index]
    setOpen(false)
    if (option && !option.disabled && option.value !== value) onChange(option.value)
  }

  /** Typing jumps to the next row starting with what was typed; one letter pressed again cycles. */
  function typeahead(key: string, from: number) {
    const now = Date.now()
    const text = now - typed.current.at > TYPEAHEAD_MS ? key : typed.current.text + key
    typed.current = { text, at: now }
    const repeated = [...text].every(c => c === key)
    const needle = fold(repeated ? key : text)
    const start = repeated ? from + 1 : Math.max(from, 0)
    for (let n = 0; n < options.length; n++) {
      const i = (start + n) % options.length
      if (enabled(i) && fold(options[i]!.label).startsWith(needle)) return i
    }
    return -1
  }

  function onKeyDown(event: KeyboardEvent<HTMLButtonElement>) {
    const { key } = event
    if (!open) {
      if (key === 'ArrowDown' || key === 'ArrowUp' || key === 'Enter' || key === ' ') {
        event.preventDefault()
        show(chosen >= 0 ? chosen : first())
      } else if (key === 'Home' || key === 'End') {
        event.preventDefault()
        show(key === 'Home' ? first() : last())
      } else if (key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey) {
        const found = typeahead(key, chosen)
        if (found >= 0) show(found)
      }
      return
    }
    const moves: Record<string, () => number> = {
      ArrowDown: () => step(active, 1), ArrowUp: () => step(active, -1), Home: first, End: last,
      PageDown: () => Math.min(last(), step(active + 9, 1)), PageUp: () => Math.max(first(), step(active - 9, -1)),
    }
    if (moves[key]) {
      event.preventDefault()
      setActive(moves[key]())
    } else if (key === 'Enter' || key === ' ') {
      event.preventDefault()
      choose(active)
    } else if (key === 'Tab') {
      choose(active)
    } else if (key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey) {
      const found = typeahead(key, active)
      if (found >= 0) setActive(found)
    }
  }

  const optionId = (index: number) => `${listId}-${index}`
  return (
    <div className={`select-root${align === 'right' ? ' is-right' : ''}${className ? ` ${className}` : ''}`} ref={rootRef}>
      <button type="button" role="combobox" id={id} ref={boxRef} disabled={disabled}
        className="menu-select"
        aria-label={ariaLabel} aria-describedby={describedBy} aria-haspopup="listbox" aria-expanded={open} aria-controls={listId}
        aria-activedescendant={open && active >= 0 ? optionId(active) : undefined}
        onClick={() => (open ? setOpen(false) : show(chosen >= 0 ? chosen : first()))}
        onKeyDown={onKeyDown}
        // A button activates on Space's keyup, after the keydown has already opened or chosen.
        onKeyUp={event => { if (event.key === ' ') event.preventDefault() }}>
        {current?.icon}
        <span className="menu-select-value">
          <span className="menu-select-name">{display ?? current?.node ?? current?.label ?? ''}</span>
          {options.map(o => <span key={o.value} className="menu-select-sizer" data-label={o.label} aria-hidden="true" />)}
        </span>
        <ChevronDownIcon size={14} />
      </button>
      {open && (
        <ul role="listbox" id={listId} ref={listRef} tabIndex={-1} aria-label={ariaLabel}
          aria-labelledby={ariaLabel ? undefined : id}
          className={`dropdown-menu select-list${up ? ' is-up' : ''}`}>
          {options.map((o, index) => (
            /* eslint-disable-next-line jsx-a11y/click-events-have-key-events --
               the keys are the combobox's, which keeps focus and points here by aria-activedescendant. */
            <li key={o.value} id={optionId(index)} role="option" aria-selected={index === chosen}
              aria-disabled={o.disabled || undefined}
              className={`dropdown-item${index === active ? ' is-active' : ''}`}
              style={o.depth ? { '--depth': o.depth } as CSSProperties : undefined}
              onMouseDown={event => event.preventDefault()}
              onMouseMove={() => { if (o.disabled !== true && index !== active) setActive(index) }}
              onClick={() => choose(index)}>
              {o.icon}
              <span className="select-option-label">{o.node ?? o.label}</span>
              {index === chosen && <CheckIcon size={14} />}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
