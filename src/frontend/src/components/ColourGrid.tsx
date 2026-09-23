import { useRef, type CSSProperties } from 'react'
import { useGridNav } from '../hooks/useGridNav'

export interface ColourGridProps<T extends string> {
  /** One array per drawn row: the rows ARIA owns have to be the rows the eye sees. */
  rows: readonly (readonly T[])[]
  /** The colour in force, however the surface that holds it spells it. */
  value: string
  /** The caller keeps its own `t(…)` here, so `keys.test.ts` reads each key in the file that
      owns its namespace — a `t()` moved in here would be one key the guard cannot place. */
  nameOf: (colour: T) => string
  onPick: (colour: T) => void
  /** The surface's own class: its stylesheet paints the swatches through it, and declares the
      `--swatch-size`/`--swatch-gap` the shared row reads. */
  className: string
  label?: string
  /** A trigger that already names the grid, which is what spares eighteen swatches a key. */
  labelledBy?: string
}

/** Six colours to a row, one tab stop, the colour in force carrying `aria-pressed` so Tab lands
 * on it. No `cellEntry`: nothing to enter, which leaves Escape to the popover or dialog beneath. */
export default function ColourGrid<T extends string>({
  rows, value, nameOf, onPick, className, label, labelledBy,
}: ColourGridProps<T>) {
  const grid = useRef<HTMLDivElement>(null)
  useGridNav({ ref: grid })
  const picked = value.trim().toLowerCase()

  return (
    <div className={className} role="grid" ref={grid}
      aria-label={label} aria-labelledby={labelledBy}>
      {rows.map(row => (
        <div className="colour-grid-row" role="row" key={row[0]}>
          {row.map(colour => (
            <div className="colour-grid-cell" role="gridcell" key={colour}>
              <button type="button"
                className={`colour-swatch${colour === picked ? ' is-picked' : ''}`}
                style={{ '--swatch': colour } as CSSProperties}
                aria-pressed={colour === picked}
                aria-label={nameOf(colour)}
                onClick={() => onPick(colour)} />
            </div>
          ))}
        </div>
      ))}
    </div>
  )
}
