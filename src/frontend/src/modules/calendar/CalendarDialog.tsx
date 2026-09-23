import { useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { isHexColor } from './calendarColors'
import ColorSwatches from './ColorSwatches'
import Modal from '../../components/Modal'

export interface CalendarValues {
  displayName: string
  color: string
}

export interface CalendarDialogProps {
  title: string
  initialName: string
  initialColor: string
  /** Rename… and Colour… are two doors onto one dialog; this is which field opens focused. */
  focus: 'name' | 'colour'
  saving: boolean
  onSubmit: (values: CalendarValues) => void
  onClose: () => void
}

/** Create, rename and recolour in one dialog: same fields, same validation, three titles. */
export default function CalendarDialog({
  title, initialName, initialColor, focus, saving, onSubmit, onClose,
}: CalendarDialogProps) {
  const { t } = useTranslation('calendar')
  const [name, setName] = useState(initialName)
  const [color, setColor] = useState(initialColor)
  const nameRef = useRef<HTMLInputElement>(null)
  const hexRef = useRef<HTMLInputElement>(null)

  const trimmedName = name.trim()
  const trimmedColor = color.trim()
  const submittable = trimmedName !== '' && isHexColor(trimmedColor) && !saving

  return (
    <Modal title={title} onClose={onClose} busy={saving}
      initialFocusRef={focus === 'colour' ? hexRef : nameRef}>
      {/* Replayed here: a disabled submit does not stop Enter in every browser, and neither an
          empty name nor a half-typed colour must reach the API. */}
      <form onSubmit={event => {
        event.preventDefault()
        if (!submittable) return
        onSubmit({ displayName: trimmedName, color: trimmedColor })
      }}>
        <div className="field-h">
          <label htmlFor="calendar-name">{t('dialogs.name')}</label>
          <input id="calendar-name" type="text" maxLength={255} value={name} ref={nameRef}
            onChange={event => setName(event.target.value)} />
        </div>

        <div className="field-h is-swatches">
          <label htmlFor="calendar-hex">{t('dialogs.colour')}</label>
          <div className="calendar-colour-field">
            <ColorSwatches value={color} onPick={setColor} />
            {/* The way out of the twelve: a calendar imported from a phone keeps its own hue. */}
            <input id="calendar-hex" type="text" maxLength={7} value={color} ref={hexRef}
              className={isHexColor(color) ? undefined : 'is-error'}
              aria-label={t('dialogs.hex')}
              onChange={event => setColor(event.target.value)} />
          </div>
        </div>

        <div className="modal-actions">
          <button type="submit" className="btn btn-primary" disabled={!submittable}>
            {saving ? <span className="spinner" /> : t('dialogs.save')}
          </button>
        </div>
      </form>
    </Modal>
  )
}
