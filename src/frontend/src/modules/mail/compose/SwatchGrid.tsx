import { useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { SWATCH_ROWS, swatchNameKey } from './composeColors'
import { useGridNav } from '../../../hooks/useGridNav'

interface Props {
  /** The colour in force: it carries `aria-pressed`, which is what lands Tab on it. */
  value: string
  /** The trigger that opened the popover, which is what names the grid. */
  labelledBy: string
  onPick: (colour: string) => void
}

/** Eighteen swatches, six by three. A grid rather than a menu: ←/→ are a submenu's there and
    Home/End name no corner. One widget per cell, so no `cellEntry` and Escape stays the
    popover's — a swatch under focus still closes the surface it sits in. */
/** Structurally a twin of calendar/ColorSwatches: kept apart on purpose, since sharing it would
    take six parameters for a twenty-line body. The one thing that can drift silently is `picked`,
    which both normalise the same way — hence SwatchGrid.test.tsx. */
export default function SwatchGrid({ value, labelledBy, onPick }: Props) {
  const { t } = useTranslation('compose')
  const grid = useRef<HTMLDivElement>(null)
  useGridNav({ ref: grid })
  const picked = value.trim().toLowerCase()

  return (
    <div className="compose-swatches" role="grid" aria-labelledby={labelledBy} ref={grid}>
      {SWATCH_ROWS.map(row => (
        <div className="compose-swatch-row" role="row" key={row[0]}>
          {row.map(colour => (
            <div className="compose-swatch-cell" role="gridcell" key={colour}>
              <button type="button" style={{ background: colour }}
                aria-pressed={colour === picked}
                aria-label={t(swatchNameKey(colour))}
                onClick={() => onPick(colour)} />
            </div>
          ))}
        </div>
      ))}
    </div>
  )
}
