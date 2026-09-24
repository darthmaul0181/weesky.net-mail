import { useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import CalendarNameColourFields, { isSubmittableCalendar } from './CalendarNameColourFields'
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

  const submittable = isSubmittableCalendar(name, color) && !saving

  return (
    <Modal title={title} onClose={onClose} busy={saving}
      initialFocusRef={focus === 'colour' ? hexRef : nameRef}>
      {/* Replayed here: a disabled submit does not stop Enter in every browser. */}
      <form onSubmit={event => {
        event.preventDefault()
        if (!submittable) return
        onSubmit({ displayName: name.trim(), color: color.trim() })
      }}>
        <CalendarNameColourFields nameId="calendar-name" hexId="calendar-hex"
          nameLabel={t('dialogs.name')} colourLabel={t('dialogs.colour')}
          name={name} color={color} onName={setName} onColor={setColor}
          nameRef={nameRef} hexRef={hexRef} />

        <div className="modal-actions">
          <button type="submit" className="btn btn-primary" disabled={!submittable}>
            {saving ? <span className="spinner" /> : t('dialogs.save')}
          </button>
        </div>
      </form>
    </Modal>
  )
}
