import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import GroupNameModal from './GroupNameModal'

function renderModal(props: Partial<Parameters<typeof GroupNameModal>[0]> = {}) {
  const onSubmit = vi.fn()
  const onClose = vi.fn()
  render(<GroupNameModal title="New group" initialName="" saving={false}
    onSubmit={onSubmit} onClose={onClose} {...props} />)
  return { onSubmit, onClose }
}

const field = () => screen.getByLabelText('Name')
const submit = () => screen.getByRole('button', { name: 'Save' })

describe('GroupNameModal', () => {
  // Un seul dialogue pour les deux gestes (décision 13) : c'est le titre qui les distingue.
  it('wears the title it is given', () => {
    renderModal({ title: 'Rename group' })

    expect(screen.getByText('Rename group')).toBeInTheDocument()
  })

  it('submits the typed name, trimmed', async () => {
    const { onSubmit } = renderModal()

    await userEvent.type(field(), '  Friends  ')
    await userEvent.click(submit())

    expect(onSubmit).toHaveBeenCalledWith('Friends')
  })

  // Un nom vide n'est pas un nom, et un espace non plus.
  it('refuses an empty name', async () => {
    renderModal()

    expect(submit()).toBeDisabled()
    await userEvent.type(field(), '   ')

    expect(submit()).toBeDisabled()
  })

  // Le bouton grisé n'est pas la seule route vers le submit : la garde se rejoue dans le
  // gestionnaire, sinon une soumission du formulaire enverrait un nom vide à l'API.
  it('refuses a form submit that bypasses the disabled button', () => {
    const { onSubmit } = renderModal({ initialName: 'Friends' })

    fireEvent.submit(document.querySelector('form')!)

    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('seeds a rename from the current name and refuses it unchanged', async () => {
    const { onSubmit } = renderModal({ initialName: 'Friends' })

    expect(field()).toHaveValue('Friends')
    expect(submit()).toBeDisabled()

    await userEvent.type(field(), ' & family')
    await userEvent.click(submit())

    expect(onSubmit).toHaveBeenCalledWith('Friends & family')
  })

  // La colonne du serveur s'arrête à 255 : la refuser au clavier vaut mieux que la refuser après
  // un aller-retour.
  it('stops the field at 255 characters', () => {
    renderModal()

    expect(field()).toHaveAttribute('maxLength', '255')
  })

  it('closes on the ✕ and never submits from it', async () => {
    const { onClose, onSubmit } = renderModal({ initialName: 'Friends' })

    await userEvent.click(screen.getByRole('button', { name: 'Close' }))

    expect(onClose).toHaveBeenCalled()
    expect(onSubmit).not.toHaveBeenCalled()
  })

  // Une requête en vol ne se relance pas d'un second clic. Le bouton porte alors un spinner,
  // donc plus de nom accessible : il se retrouve par son type.
  it('withholds the submit while a write is in flight', () => {
    renderModal({ initialName: 'Friends', saving: true })

    expect(document.querySelector('button[type="submit"]')).toBeDisabled()
  })
})
