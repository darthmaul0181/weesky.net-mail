import { useState, type ChangeEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { CALENDAR_COLORS, isHexColor } from './calendarColors'
import type { Calendar } from './calendarTypes'
import ColorSwatches from './ColorSwatches'
import { calendarHeaderOf } from './icsHeader'

export type ImportChoice =
  | { mode: 'existing'; id: string; file: File }
  | { mode: 'new'; file: File; displayName: string; color: string }

export interface ImportDialogProps {
  calendars: Calendar[]
  /** The row the dialog was opened from, preselected as the destination. */
  targetId: string
  saving: boolean
  onImport: (choice: ImportChoice) => void
  onClose: () => void
}

/**
 * One file, two destinations: an existing calendar, or a new one the same gesture creates. The
 * file's own header pre-fills the second — an export says what it is, and asking the user to
 * retype it is asking them to get it wrong. Nothing past that header is read here: an export
 * runs to tens of megabytes and this runs on the pick.
 */
/** All a header can occupy: `calendarHeaderOf` stops at the first component in any case. */
const HEADER_BYTES = 65_536

export default function ImportDialog({
  calendars, targetId, saving, onImport, onClose,
}: ImportDialogProps) {
  const { t } = useTranslation('calendar')
  const [mode, setMode] = useState<'existing' | 'new'>('existing')
  const [file, setFile] = useState<File | null>(null)
  const [id, setId] = useState(targetId)
  const [name, setName] = useState('')
  const [color, setColor] = useState(CALENDAR_COLORS[0])

  async function pick(event: ChangeEvent<HTMLInputElement>) {
    const chosen = event.target.files?.[0] ?? null
    // Cleared before anything is awaited, ContactsTransfer's rule: an input keeping its value
    // fires no change event when the same file is chosen a second time.
    event.target.value = ''
    setFile(chosen)
    if (!chosen) return
    // The first 64 KB, never the file: an export runs to tens of megabytes, and the header this
    // reads is four lines of its head.
    const header = calendarHeaderOf(await chosen.slice(0, HEADER_BYTES).text())
    // The file's name, else its own file name without the extension: a nameless import would
    // otherwise leave Save inert with nothing on screen explaining why.
    setName(header.name || chosen.name.replace(/\.[^.]+$/, ''))
    if (header.color) setColor(header.color)
  }

  const trimmedName = name.trim()
  const submittable = file !== null && !saving
    && (mode === 'existing' ? id !== '' : trimmedName !== '' && isHexColor(color))

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={event => event.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">{t('import.title')}</span>
          <button className="modal-close" aria-label={t('actions.close', { ns: 'common' })}
            onClick={onClose}>✕</button>
        </div>

        <form onSubmit={event => {
          event.preventDefault()
          if (!submittable || !file) return
          onImport(mode === 'existing'
            ? { mode: 'existing', id, file }
            : { mode: 'new', file, displayName: trimmedName, color: color.trim() })
        }}>
          <div className="field-h">
            <label htmlFor="calendar-import-file">{t('import.file')}</label>
            <input id="calendar-import-file" type="file" accept=".ics,text/calendar"
              onChange={pick} />
          </div>

          <div className="field-h">
            {/* The row's own label, styled by `.field-h > label:first-child` — the group carries
                its accessible name itself, so this one associates with nothing. */}
            <label>{t('import.into')}</label>
            <div className="import-modes" role="radiogroup" aria-label={t('import.into')}>
              <label>
                <input type="radio" name="calendar-import-mode" checked={mode === 'existing'}
                  onChange={() => setMode('existing')} />
                {t('import.existing')}
              </label>
              <label>
                <input type="radio" name="calendar-import-mode" checked={mode === 'new'}
                  onChange={() => setMode('new')} />
                {t('import.new')}
              </label>
            </div>
          </div>

          {mode === 'existing' ? (
            <div className="field-h">
              <label htmlFor="calendar-import-into">{t('import.existing')}</label>
              <select id="calendar-import-into" value={id}
                onChange={event => setId(event.target.value)}>
                {calendars.map(one => (
                  <option key={one.id} value={one.id}>{one.displayName}</option>
                ))}
              </select>
            </div>
          ) : (
            <>
              <div className="field-h">
                <label htmlFor="calendar-import-name">{t('import.name')}</label>
                <input id="calendar-import-name" type="text" maxLength={255} value={name}
                  onChange={event => setName(event.target.value)} />
              </div>
              <div className="field-h is-swatches">
                <label htmlFor="calendar-import-hex">{t('import.colour')}</label>
                <div className="calendar-colour-field">
                  <ColorSwatches value={color} onPick={setColor} />
                  <input id="calendar-import-hex" type="text" maxLength={7} value={color}
                    className={isHexColor(color) ? undefined : 'is-error'}
                    aria-label={t('dialogs.hex')}
                    onChange={event => setColor(event.target.value)} />
                </div>
              </div>
            </>
          )}

          <div className="modal-actions">
            <button type="submit" className="btn btn-primary" disabled={!submittable}>
              {saving ? <span className="spinner" /> : t('import.submit')}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
