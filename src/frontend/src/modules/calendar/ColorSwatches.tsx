import { useRef, type CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import { CALENDAR_COLORS, colourNameKey } from './calendarColors'
import { useGridNav } from '../../hooks/useGridNav'

interface Props {
  value: string
  onPick: (color: string) => void
}

/** Six per row, the count `.color-swatches` draws: the rows have to match what the eye sees. */
const ROWS = [CALENDAR_COLORS.slice(0, 6), CALENDAR_COLORS.slice(6)]

/** The twelve, six by two. Shared by the calendar dialog and the import one so a colour is
    chosen the same way whichever door the calendar is created from. */
export default function ColorSwatches({ value, onPick }: Props) {
  const { t } = useTranslation('calendar')
  const grid = useRef<HTMLDivElement>(null)
  useGridNav({ ref: grid })
  const picked = value.trim().toLowerCase()

  return (
    <div className="color-swatches" role="grid" aria-label={t('dialogs.colourGrid')} ref={grid}>
      {ROWS.map(row => (
        <div className="color-swatch-row" role="row" key={row[0]}>
          {row.map(color => (
            <div className="color-swatch-cell" role="gridcell" key={color}>
              <button type="button"
                className={`color-swatch${color === picked ? ' is-picked' : ''}`}
                style={{ '--cal': color } as CSSProperties}
                aria-pressed={color === picked}
                aria-label={t(colourNameKey(color))}
                onClick={() => onPick(color)} />
            </div>
          ))}
        </div>
      ))}
    </div>
  )
}
