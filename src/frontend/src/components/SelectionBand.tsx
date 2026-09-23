import { type ReactNode, useEffect, useRef } from 'react'

export interface SelectionBandProps {
  /** Checked when the whole screen is selected. */
  allSelected: boolean
  indeterminate: boolean
  onToggleAll: () => void
  selectionDisabled?: boolean
  selectAllLabel: string
  /** How many rows are checked. Above zero, `countLabel` replaces `center`. */
  count: number
  countLabel: string
  /** What the band carries AT REST: a title, a search field, whatever the caller wants. */
  center: ReactNode
  /** Before the checkbox: the drawer hamburger, or nothing. */
  leading?: ReactNode
  /** After the centre and inside the title: what filters the view rather than acting on it. It
      survives the count, because a filter stays true while a selection is in progress. */
  trailing?: ReactNode
  /** The actions, on the right. */
  children: ReactNode
}

/** The band both list columns wear: the master checkbox, and the centre giving way to the count
 * while a selection stands. The actions, and the centre at rest (the folder name, the search
 * field), are the caller's. */
export default function SelectionBand({
  allSelected, indeterminate, onToggleAll, selectionDisabled, selectAllLabel,
  count, countLabel, center, leading, trailing, children,
}: SelectionBandProps) {
  const master = useRef<HTMLInputElement>(null)
  // A DOM property, not an attribute: React writes no such attribute, so it has to be set here.
  useEffect(() => { if (master.current) master.current.indeterminate = indeterminate }, [indeterminate])

  return (
    <div className={`selection-toolbar${count > 0 ? ' is-selecting' : ''}`}>
      {leading}
      {/* The finger-sized target on a phone is this label, not the box: a native checkbox paints
          its whole border box, so sizing it to 44px draws a slab twice its neighbours' weight. */}
      <label className="selection-master-hit">
        <input ref={master} type="checkbox" className="selection-master" aria-label={selectAllLabel}
          checked={allSelected} onChange={onToggleAll} disabled={selectionDisabled} />
      </label>
      <span className="selection-heading">
        {count > 0 ? <span className="selection-title">{countLabel}</span> : center}
        {trailing}
      </span>
      <div className="selection-actions">{children}</div>
    </div>
  )
}
