import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import SelectionBand from './SelectionBand'

describe('SelectionBand', () => {
  // La règle que le squelette apporte, et la seule : le centre cède au décompte dès qu'une ligne est
  // cochée. Écrite ici plutôt que dans chaque appelant, sinon les deux modules la réinventent.
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

  // Un filtre reste vrai pendant qu'une sélection est en cours : l'étoile du mail est dans le
  // titre, et le décompte ne doit pas l'emporter avec le nom du dossier.
  it('keeps the trailing slot through the swap', () => {
    render(
      <SelectionBand allSelected={false} indeterminate count={2} countLabel="2 sélectionnés"
        onToggleAll={() => {}} selectAllLabel="Tout sélectionner" center={<span>Reçus</span>}
        trailing={<button>Favoris seulement</button>}>
        <button>Supprimer</button>
      </SelectionBand>)

    expect(screen.getByRole('button', { name: 'Favoris seulement' })).toBeInTheDocument()
  })

  // indeterminate est une propriété DOM et non un attribut : un JSX qui l'écrit ne la pose pas.
  it('sets the master box indeterminate as a DOM property', () => {
    render(
      <SelectionBand allSelected={false} indeterminate onToggleAll={() => {}}
        selectAllLabel="Tout sélectionner" count={2} countLabel="2 sélectionnés" center={null}>
        <button>Supprimer</button>
      </SelectionBand>)

    expect((screen.getByLabelText('Tout sélectionner') as HTMLInputElement).indeterminate).toBe(true)
  })
})
