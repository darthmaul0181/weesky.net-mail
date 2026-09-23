import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import SelectionBand from './SelectionBand'

describe('SelectionBand', () => {
  // The one rule the skeleton brings: the centre gives way to the count as soon as a row is
  // checked. Written here rather than in each caller, or the two modules would reinvent it.
  it('shows the caller centre at rest and the count once rows are checked', () => {
    const { rerender } = render(
      <SelectionBand allSelected={false} indeterminate={false} onToggleAll={() => {}}
        selectAllLabel="Tout sélectionner" count={0} countLabel="0 sélectionné"
        center={<span>Boîte de réception</span>}>
        <button>Supprimer</button>
      </SelectionBand>)

    expect(screen.getByText('Boîte de réception')).toBeInTheDocument()

    rerender(
      <SelectionBand allSelected={false} indeterminate onToggleAll={() => {}}
        selectAllLabel="Tout sélectionner" count={3} countLabel="3 sélectionnés"
        center={<span>Boîte de réception</span>}>
        <button>Supprimer</button>
      </SelectionBand>)

    expect(screen.queryByText('Boîte de réception')).not.toBeInTheDocument()
    expect(screen.getByText('3 sélectionnés')).toBeInTheDocument()
  })

  // A filter stays true while a selection is in progress: the mail's star is in the title, and
  // the count must not carry it away along with the folder name.
  it('keeps the trailing slot through the swap', () => {
    render(
      <SelectionBand allSelected={false} indeterminate count={2} countLabel="2 sélectionnés"
        onToggleAll={() => {}} selectAllLabel="Tout sélectionner" center={<span>Reçus</span>}
        trailing={<button>Favoris seulement</button>}>
        <button>Supprimer</button>
      </SelectionBand>)

    expect(screen.getByRole('button', { name: 'Favoris seulement' })).toBeInTheDocument()
  })

  // indeterminate is a DOM property, not an attribute: JSX that writes it does not set it.
  it('sets the master box indeterminate as a DOM property', () => {
    render(
      <SelectionBand allSelected={false} indeterminate onToggleAll={() => {}}
        selectAllLabel="Tout sélectionner" count={2} countLabel="2 sélectionnés" center={null}>
        <button>Supprimer</button>
      </SelectionBand>)

    expect(screen.getByLabelText<HTMLInputElement>('Tout sélectionner').indeterminate).toBe(true)
  })
})
