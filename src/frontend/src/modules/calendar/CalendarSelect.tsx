import type { CSSProperties } from 'react'
import MenuSelect from '../../components/MenuSelect'
import type { Calendar } from './calendarTypes'

export interface CalendarSelectProps {
  label: string
  calendars: Calendar[]
  value: string
  onChange: (id: string) => void
}

const swatch = (color: string | undefined) => (
  <span className="calendar-swatch" aria-hidden="true" style={{ '--cal': color } as CSSProperties} />
)

/** The calendar picker, drawn as a select whose rows carry their colour. A native <select>
    cannot paint an option, so the swatch used to sit beside the box, and the box then started
    8px to the right of every other control in the column. */
export default function CalendarSelect({ label, calendars, value, onChange }: CalendarSelectProps) {
  return (
    <MenuSelect ariaLabel={label} value={value} onChange={onChange}
      options={calendars.map(one => ({ value: one.id, label: one.displayName, icon: swatch(one.color) }))} />
  )
}
