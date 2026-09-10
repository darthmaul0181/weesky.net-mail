import type { CSSProperties } from 'react'
import DropdownMenu from '../../components/DropdownMenu'
import ChevronDownIcon from '../../icons/ChevronDownIcon'
import type { Calendar } from './calendarTypes'

export interface CalendarSelectProps {
  label: string
  calendars: Calendar[]
  value: string
  onChange(id: string): void
}

const swatch = (color: string | undefined) => (
  <span className="calendar-swatch" aria-hidden="true" style={{ '--cal': color } as CSSProperties} />
)

/** The calendar picker, drawn as a select whose rows carry their colour. A native <select>
    cannot paint an option, so the swatch used to sit beside the box, and the box then started
    8px to the right of every other control in the column. */
export default function CalendarSelect({ label, calendars, value, onChange }: CalendarSelectProps) {
  const chosen = calendars.find(one => one.id === value)
  return (
    <DropdownMenu ariaLabel={label} className="calendar-select" align="left"
      trigger={<>
        {swatch(chosen?.color)}
        <span className="calendar-select-name">{chosen?.displayName ?? ''}</span>
        <ChevronDownIcon size={14} />
      </>}
      items={calendars.map(one => ({
        label: one.displayName, icon: swatch(one.color), onSelect: () => onChange(one.id),
      }))} />
  )
}
