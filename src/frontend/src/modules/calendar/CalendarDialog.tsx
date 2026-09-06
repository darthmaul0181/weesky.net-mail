import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { isHexColor } from './calendarColors'
import ColorSwatches from './ColorSwatches'

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

/**
 * Create, rename and recolour, in one dialog: same fields, same validation, three titles. The ✕
 * is the only way out, as in the admin dialogs, and the `<form>` is what makes Enter submit.
 */
export default function CalendarDialog({
  title, initialName, initialColor, focus, saving, onSubmit, onClose,
}: CalendarDialogProps) {
  const { t } = useTranslation('calendar')
  const [name, setName] = useState(initialName)
  const [color, setColor] = useState(initialColor)

  const trimmedName = name.trim()
  const trimmedColor = color.trim()
  const submittable = trimmedName !== '' && isHexColor(trimmedColor) && !saving

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={event => event.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">{title}</span>
          <button className="modal-close" aria-label={t('actions.close', { ns: 'common' })}
            onClick={onClose}>✕</button>
        </div>

        {/* Replayed here: a disabled submit does not stop Enter in every browser, and neither an
            empty name nor a half-typed colour must reach the API. */}
        <form onSubmit={event => {
          event.preventDefault()
          if (!submittable) return
          onSubmit({ displayName: trimmedName, color: trimmedColor })
        }}>
          <div className="field-h">
            <label htmlFor="calendar-name">{t('dialogs.name')}</label>
            <input id="calendar-name" type="text" maxLength={255} value={name}
              autoFocus={focus === 'name'} onChange={event => setName(event.target.value)} />
          </div>

          <div className="field-h is-swatches">
            <label htmlFor="calendar-hex">{t('dialogs.colour')}</label>
            <div className="calendar-colour-field">
              <ColorSwatches value={color} onPick={setColor} />
              {/* The way out of the twelve: a calendar imported from a phone keeps its own hue. */}
              <input id="calendar-hex" type="text" maxLength={7} value={color}
                className={isHexColor(color) ? undefined : 'is-error'}
                autoFocus={focus === 'colour'} aria-label={t('dialogs.hex')}
                onChange={event => setColor(event.target.value)} />
            </div>
          </div>

          <div className="modal-actions">
            <button type="submit" className="btn btn-primary" disabled={!submittable}>
              {saving ? <span className="spinner" /> : t('dialogs.save')}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
