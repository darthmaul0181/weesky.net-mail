import { useRef, useState, type ChangeEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { CALENDAR_COLORS } from './calendarColors'
import type { Calendar } from './calendarTypes'
import CalendarNameColourFields, { isSubmittableCalendar } from './CalendarNameColourFields'
import { calendarHeaderOf } from './icsHeader'
import Modal from '../../components/Modal'

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

// One file, to an existing calendar or a new one its header pre-fills. Nothing past the header is
// read: an export runs to tens of megabytes and this runs on the pick.
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
  // CALENDAR_COLORS is a fixed, non-empty literal list (see its own declaration).
  const [color, setColor] = useState<string>(CALENDAR_COLORS[0]!)
  const fileRef = useRef<HTMLInputElement>(null)

  async function pick(event: ChangeEvent<HTMLInputElement>) {
    const chosen = event.target.files?.[0] ?? null
    // Cleared before anything is awaited, ContactsTransfer's rule: an input keeping its value
    // fires no change event when the same file is chosen a second time.
    event.target.value = ''
    setFile(chosen)
    if (!chosen) return
    // The first 64 KB, never the file: an export runs to tens of megabytes, and the header this
    // reads is four lines of its head. A head the browser cannot read is a header saying nothing.
    const header: ReturnType<typeof calendarHeaderOf> = await chosen.slice(0, HEADER_BYTES).text()
      .then(calendarHeaderOf, () => ({}))
    // The file's name, else its own file name without the extension: a nameless import would
    // otherwise leave Save inert with nothing on screen explaining why.
    setName(header.name || chosen.name.replace(/\.[^.]+$/, ''))
    if (header.color) setColor(header.color)
  }

  const submittable = file !== null && !saving
    && (mode === 'existing' ? id !== '' : isSubmittableCalendar(name, color))

  return (
    <Modal title={t('import.title')} onClose={onClose} busy={saving} initialFocusRef={fileRef}>
      <form onSubmit={event => {
        event.preventDefault()
        if (!submittable || !file) return
        onImport(mode === 'existing'
          ? { mode: 'existing', id, file }
          : { mode: 'new', file, displayName: name.trim(), color: color.trim() })
      }}>
        <div className="field-h">
          <label htmlFor="calendar-import-file">{t('import.file')}</label>
          <input id="calendar-import-file" type="file" accept=".ics,text/calendar"
            ref={fileRef} onChange={event => void pick(event)} />
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
          <CalendarNameColourFields nameId="calendar-import-name" hexId="calendar-import-hex"
            nameLabel={t('import.name')} colourLabel={t('import.colour')}
            name={name} color={color} onName={setName} onColor={setColor} />
        )}

        <div className="modal-actions">
          <button type="submit" className="btn btn-primary" disabled={!submittable}>
            {saving ? <span className="spinner" /> : t('import.submit')}
          </button>
        </div>
      </form>
    </Modal>
  )
}
