import type { RefObject } from 'react'
import { useTranslation } from 'react-i18next'
import { isHexColor } from './calendarColors'
import ColorSwatches from './ColorSwatches'

/** Neither an empty name nor a half-typed colour may reach the API. */
export function isSubmittableCalendar(name: string, color: string): boolean {
  return name.trim() !== '' && isHexColor(color)
}

export interface CalendarNameColourFieldsProps {
  nameId: string
  hexId: string
  nameLabel: string
  colourLabel: string
  name: string
  color: string
  onName: (value: string) => void
  onColor: (value: string) => void
  nameRef?: RefObject<HTMLInputElement>
  hexRef?: RefObject<HTMLInputElement>
}

export default function CalendarNameColourFields({
  nameId, hexId, nameLabel, colourLabel, name, color, onName, onColor, nameRef, hexRef,
}: CalendarNameColourFieldsProps) {
  const { t } = useTranslation('calendar')

  return (
    <>
      <div className="field-h">
        <label htmlFor={nameId}>{nameLabel}</label>
        <input id={nameId} type="text" maxLength={255} value={name} ref={nameRef}
          onChange={event => onName(event.target.value)} />
      </div>

      <div className="field-h is-swatches">
        <label htmlFor={hexId}>{colourLabel}</label>
        <div className="calendar-colour-field">
          <ColorSwatches value={color} onPick={onColor} />
          {/* The way out of the twelve: a calendar imported from a phone keeps its own hue. */}
          <input id={hexId} type="text" maxLength={7} value={color} ref={hexRef}
            className={isHexColor(color) ? undefined : 'is-error'}
            aria-label={t('dialogs.hex')}
            onChange={event => onColor(event.target.value)} />
        </div>
      </div>
    </>
  )
}
