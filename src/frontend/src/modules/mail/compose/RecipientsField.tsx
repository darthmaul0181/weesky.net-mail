import { useState, type ClipboardEvent, type KeyboardEvent } from 'react'

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
}

export default function RecipientsField({ id, label, tokens, onChange, autoFocus }: Props) {
  const [draft, setDraft] = useState('')

  function commit(raw: string) {
    const parts = raw.split(/[,;]/).map(p => p.trim()).filter(Boolean)
    if (parts.length > 0) onChange([...tokens, ...parts])
    setDraft('')
  }

  function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'Enter' || event.key === ',' || event.key === ';') {
      event.preventDefault()
      if (draft.trim()) commit(draft)
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
        {tokens.map((token, index) => (
          <span key={`${token}-${index}`} className={`recipient-token${isValidAddress(token) ? '' : ' is-invalid'}`}>
            {token}
            <button type="button" aria-label={`Remove ${token}`}
              onClick={() => onChange(tokens.filter((_, i) => i !== index))}>✕</button>
          </span>
        ))}
        <input id={id} type="text" value={draft} autoFocus={autoFocus}
          onChange={e => setDraft(e.target.value)}
          onKeyDown={onKeyDown} onPaste={onPaste}
          onBlur={() => { if (draft.trim()) commit(draft) }} />
      </div>
    </div>
  )
}
