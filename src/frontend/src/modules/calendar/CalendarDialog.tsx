import { useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import CalendarNameColourFields, { isSubmittableCalendar } from './CalendarNameColourFields'
import MenuSelect from '../../components/MenuSelect'
import Modal from '../../components/Modal'
import { REMINDER_OPTIONS, reminderLabel } from './birthday'
import type { BirthdayReminder } from './calendarTypes'

export interface CalendarValues {
  displayName: string
  color: string
  birthdayReminder?: BirthdayReminder
}

export interface CalendarDialogProps {
  title: string
  initialName: string
  initialColor: string
  /** Rename… and Colour… are two doors onto one dialog; this is which field opens focused. */
  focus: 'name' | 'colour'
  /** Set for the birthdays calendar alone, which then shows its Reminder row. */
  initialReminder?: BirthdayReminder
  saving: boolean
  onSubmit: (values: CalendarValues) => void
  onClose: () => void
}

/** Create, rename and recolour in one dialog: same fields, same validation, three titles. */
export default function CalendarDialog({
  title, initialName, initialColor, focus, initialReminder, saving, onSubmit, onClose,
}: CalendarDialogProps) {
  const { t } = useTranslation('calendar')
  const [name, setName] = useState(initialName)
  const [color, setColor] = useState(initialColor)
  const [reminder, setReminder] = useState(initialReminder)
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
        onSubmit({
          displayName: name.trim(), color: color.trim(),
          ...(reminder !== undefined && { birthdayReminder: reminder }),
        })
      }}>
        <CalendarNameColourFields nameId="calendar-name" hexId="calendar-hex"
          nameLabel={t('dialogs.name')} colourLabel={t('dialogs.colour')}
          name={name} color={color} onName={setName} onColor={setColor}
          nameRef={nameRef} hexRef={hexRef} />

        {reminder !== undefined && (
          <>
            <div className="field-h">
              <label htmlFor="calendar-reminder">{t('dialogs.reminder')}</label>
              <MenuSelect id="calendar-reminder" value={reminder} onChange={setReminder}
                describedBy="calendar-reminder-hint"
                options={REMINDER_OPTIONS.map(value => ({ value, label: reminderLabel(value, t) }))} />
            </div>
            <p className="editor-hint" id="calendar-reminder-hint">
              {t('dialogs.reminderHint')}
            </p>
          </>
        )}

        <div className="modal-actions">
          <button type="submit" className="btn btn-primary" disabled={!submittable}>
            {saving ? <span className="spinner" /> : t('dialogs.save')}
          </button>
        </div>
      </form>
    </Modal>
  )
}
