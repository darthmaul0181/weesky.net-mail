import { useEffect, useMemo, useRef, useState, type ClipboardEvent, type KeyboardEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { canonicalAddress } from '../../../lib/canonicalAddress'
import { contactNameOf } from '../../contacts/contactName'
import { compareContacts, suggestionsFor } from '../../contacts/contactSearch'
import type { Contact } from '../../contacts/contactTypes'

/** Paint-and-gate check only; the backend's MimeKit parse is the authority. */
export function isValidAddress(value: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value)
}

interface Props {
  id: string
  label: string
  tokens: string[]
  onChange: (tokens: string[]) => void
  autoFocus?: boolean
  /** The user's book, handed in by ComposeView. Empty by default, so the field stays fully usable
      — and its existing behaviour unchanged — for an account with no contacts. */
  contacts?: Contact[]
}

/**
 * Canonical address to the name its contact carries, if any. A token stays the bare address — the
 * wire format and every reader downstream depend on it — so naming it is a rendering step, and it
 * is exported because the folded summary on a phone names the same recipients: two callers naming
 * one person two different ways is the drift `displayNameOf` exists to prevent in contacts.
 * Sorted, and first-wins, so a shared address keeps the name the dropdown offered it under.
 */
export function namesByAddressOf(contacts: Contact[]): Map<string, string> {
  const names = new Map<string, string>()
  for (const contact of [...contacts].sort(compareContacts)) {
    const name = contactNameOf(contact)
    if (name === null) continue
    for (const address of contact.addresses) {
      const key = canonicalAddress(address)
      if (!names.has(key)) names.set(key, name)
    }
  }
  return names
}

export default function RecipientsField({
  id, label, tokens, onChange, autoFocus, contacts = [],
}: Props) {
  const { t } = useTranslation('compose')
  const [draft, setDraft] = useState('')
  const [closed, setClosed] = useState(false)
  // -1 means "nothing highlighted", and it is the default on purpose: Enter must commit the
  // address the user typed, not substitute a suggestion they never looked at.
  const [active, setActive] = useState(-1)

  const suggestions = useMemo(
    () => suggestionsFor(contacts, draft, { exclude: new Set(tokens) }),
    [contacts, draft, tokens])
  const namesByAddress = useMemo(() => namesByAddressOf(contacts), [contacts])

  const open = !closed && suggestions.length > 0
  const listId = `${id}-suggestions`
  const listRef = useRef<HTMLUListElement>(null)

  useEffect(() => {
    if (active < 0) return
    const row = listRef.current?.children[active] as HTMLElement | undefined
    // Optional call, not a test-time stub: jsdom implements no scrollIntoView, and bringing a row
    // into view is decoration — it must never be the reason the field throws.
    row?.scrollIntoView?.({ block: 'nearest' })
  }, [active])

  function commit(raw: string) {
    const parts = raw.split(/[,;]/).map(p => p.trim()).filter(Boolean)
    if (parts.length > 0) onChange([...tokens, ...parts])
    reset()
  }

  function reset() {
    setDraft('')
    setActive(-1)
    setClosed(false)
  }

  function type(value: string) {
    setDraft(value)
    setActive(-1)
    setClosed(false)
  }

  function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    const down = event.key === 'ArrowDown'
    if ((down || event.key === 'ArrowUp') && suggestions.length > 0) {
      event.preventDefault()
      // Escape hides the list, it does not dismiss the query, so an arrow brings it back and
      // lands on the end it came from — what the combobox pattern prescribes.
      if (closed) {
        setClosed(false)
        setActive(down ? 0 : suggestions.length - 1)
      } else {
        setActive(previous => down
          ? Math.min(previous + 1, suggestions.length - 1)
          : Math.max(previous - 1, -1))
      }
    } else if (event.key === 'Escape') {
      if (open) { event.preventDefault(); setClosed(true) }
    } else if (event.key === 'Enter' || event.key === ',' || event.key === ';') {
      event.preventDefault()
      if (open && active >= 0) commit(suggestions[active].address)
      else if (draft.trim()) commit(draft)
    } else if (event.key === 'Backspace' && draft === '' && tokens.length > 0) {
      onChange(tokens.slice(0, -1))
    }
  }

  function onPaste(event: ClipboardEvent<HTMLInputElement>) {
    const text = event.clipboardData.getData('text')
    if (!/[,;]/.test(text)) return
    event.preventDefault()
    commit(text)
  }

  return (
    <div className="field-h recipients-field">
      <label htmlFor={id}>{label}</label>
      <div className="recipients-box">
        {tokens.map((token, index) => {
          const name = namesByAddress.get(canonicalAddress(token))
          const shown = name ?? token
          return (
            <span key={`${token}-${index}`}
              className={`recipient-token${isValidAddress(token) ? '' : ' is-invalid'}`}
              // Only when the chip is not already showing it: a bubble repeating the text under
              // the cursor is noise, the rule AddressLabel follows in the reader.
              title={name ? token : undefined}>
              {shown}
              <button type="button" aria-label={t('recipients.remove', { name: shown })}
                onClick={() => onChange(tokens.filter((_, i) => i !== index))}>✕</button>
            </span>
          )
        })}
        <input id={id} type="text" value={draft} autoFocus={autoFocus}
          role="combobox" aria-expanded={open} aria-autocomplete="list"
          aria-controls={open ? listId : undefined}
          // The highlight lives on a row the focus never moves to, so without this a screen
          // reader announces nothing as the arrows walk the list.
          aria-activedescendant={open && active >= 0 ? `${listId}-${active}` : undefined}
          onChange={e => type(e.target.value)}
          onKeyDown={onKeyDown} onPaste={onPaste}
          onBlur={() => { if (draft.trim()) commit(draft) }} />

        {open && (
          <ul ref={listRef} className="ownership-dropdown" id={listId} role="listbox"
            aria-label={t('recipients.suggestions', { label })}
            // On the container, so it also covers the scrollbar and the padding strip: any
            // mousedown here would blur the input, and the blur commits the half-typed draft as
            // an invalid token. Rows rely on this too — their own handler bubbles up to it.
            onMouseDown={event => event.preventDefault()}>
            {suggestions.map((suggestion, index) => (
              <li key={suggestion.address} id={`${listId}-${index}`} role="option"
                aria-selected={index === active}
                className={`ownership-dropdown-option${index === active ? ' is-active' : ''}`}
                onMouseDown={() => commit(suggestion.address)}>
                {suggestion.names.length > 0
                  && <span className="suggestion-names">{suggestion.names.join(', ')}</span>}
                <span className="suggestion-address">{suggestion.address}</span>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  )
}
