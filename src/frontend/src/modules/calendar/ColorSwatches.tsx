import { type CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import { CALENDAR_COLORS } from './calendarColors'

interface Props {
  value: string
  onPick: (color: string) => void
}

/** The twelve, six by two. Shared by the calendar dialog and the import one so a colour is
    chosen the same way whichever door the calendar is created from. */
export default function ColorSwatches({ value, onPick }: Props) {
  const { t } = useTranslation('calendar')
  const picked = value.trim().toLowerCase()

  return (
    <div className="color-swatches">
      {CALENDAR_COLORS.map(color => (
        <button key={color} type="button"
          className={`color-swatch${color === picked ? ' is-picked' : ''}`}
          style={{ '--cal': color } as CSSProperties}
          aria-pressed={color === picked}
          aria-label={t('dialogs.colourOption', { hex: color })}
          onClick={() => onPick(color)} />
      ))}
    </div>
  )
}
