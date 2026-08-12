import { type ReactNode, useEffect, useRef } from 'react'

export interface SelectionBandProps {
  /** Cochée quand tout l'écran est sélectionné. */
  allSelected: boolean
  indeterminate: boolean
  onToggleAll: () => void
  selectionDisabled?: boolean
  selectAllLabel: string
  /** Combien de lignes sont cochées. Au-dessus de zéro, `countLabel` remplace `center`. */
  count: number
  countLabel: string
  /** Ce que la bande porte AU REPOS : un titre, un champ de recherche, ce que l'appelant veut. */
  center: ReactNode
  /** Avant la case : le hamburger du tiroir, ou rien. */
  leading?: ReactNode
  /** Après le centre et à l'intérieur du titre : ce qui filtre la vue plutôt que d'agir sur elle.
      Il survit au décompte, parce qu'un filtre reste vrai pendant qu'une sélection est en cours. */
  trailing?: ReactNode
  /** Les actions, à droite. */
  children: ReactNode
}

/**
 * The band both list columns wear. It owns the master checkbox, the rule that the centre gives way
 * to the count while a selection stands, and nothing else: the actions are the caller's, and so is
 * whatever sits in the centre at rest — the mail puts its folder name there, the contacts their
 * search field.
 */
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
