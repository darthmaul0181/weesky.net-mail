import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import IdentityDialog from './IdentityDialog'
import { useAliases } from '../../mail/queries'

vi.mock('../../mail/queries', () => ({ useAliases: vi.fn() }))

const aliases = [
  { name: 'michel', domain: 'weesky.be' },
  { name: 'support', domain: 'weesky.be' },
  { name: 'taken', domain: 'weesky.be' },
]

describe('IdentityDialog', () => {
  beforeEach(() => {
    vi.mocked(useAliases).mockReturnValue({ data: aliases, isLoading: false } as never)
  })

  function renderAdd(over: Partial<Parameters<typeof IdentityDialog>[0]> = {}) {
    const onSubmit = vi.fn()
    const onClose = vi.fn()
    const { container } = render(
      <IdentityDialog mode="add" taken={['taken@weesky.be']} initialName="Mick Dubois"
        onSubmit={onSubmit} onClose={onClose} {...over} />)
    return { onSubmit, onClose, container }
  }

  it('filters the aliases as the user types, refining with each character', () => {
    renderAdd()
    fireEvent.change(screen.getByLabelText('Alias'), { target: { value: 'e' } })
    expect(screen.getByRole('button', { name: 'michel@weesky.be' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'support@weesky.be' })).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Alias'), { target: { value: 'mich' } })
    expect(screen.getByRole('button', { name: 'michel@weesky.be' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'support@weesky.be' })).toBeNull()
  })

  it('excludes an already-taken address whatever case it is stored in', () => {
    renderAdd({ taken: ['TAKEN@Weesky.be'] })
    fireEvent.change(screen.getByLabelText('Alias'), { target: { value: 'taken' } })
    expect(screen.queryByRole('button', { name: 'taken@weesky.be' })).toBeNull()
  })

  it('picking an alias fills the field and closes the dropdown', () => {
    renderAdd()
    fireEvent.change(screen.getByLabelText('Alias'), { target: { value: 'mich' } })
    fireEvent.mouseDown(screen.getByRole('button', { name: 'michel@weesky.be' }))
    expect(screen.getByLabelText('Alias')).toHaveValue('michel@weesky.be')
    expect(screen.queryByRole('button', { name: 'michel@weesky.be' })).toBeNull()
  })

  it('caps the display name at the stored column length', () => {
    renderAdd()
    expect(screen.getByLabelText('Display name')).toHaveAttribute('maxlength', '100')
  })

  it('pre-fills the name and submits the picked alias with it', () => {
    const { onSubmit } = renderAdd()
    fireEvent.change(screen.getByLabelText('Alias'), { target: { value: 'mich' } })
    fireEvent.mouseDown(screen.getByRole('button', { name: 'michel@weesky.be' }))
    expect(screen.getByLabelText('Display name')).toHaveValue('Mick Dubois')
    fireEvent.change(screen.getByLabelText('Display name'), { target: { value: 'Michel D.' } })
    fireEvent.click(screen.getByRole('button', { name: 'Add' }))
    expect(onSubmit).toHaveBeenCalledWith('michel@weesky.be', 'Michel D.')
  })

  it('disables Add until an alias is picked and a name is present', () => {
    renderAdd()
    expect(screen.getByRole('button', { name: 'Add' })).toBeDisabled()
    fireEvent.change(screen.getByLabelText('Alias'), { target: { value: 'mich' } })
    fireEvent.mouseDown(screen.getByRole('button', { name: 'michel@weesky.be' }))
    fireEvent.change(screen.getByLabelText('Display name'), { target: { value: '  ' } })
    expect(screen.getByRole('button', { name: 'Add' })).toBeDisabled()
  })

  it('says the aliases could not be loaded rather than counting none', () => {
    vi.mocked(useAliases).mockReturnValue({ data: undefined, isLoading: false, isError: true } as never)
    renderAdd()
    expect(screen.getByText('Could not load your aliases.')).toBeInTheDocument()
  })

  it('closes on the ✕ and on the overlay, never on the panel', () => {
    const { onClose, container } = renderAdd()
    fireEvent.click(screen.getByText('Add identity'))
    expect(onClose).not.toHaveBeenCalled()
    fireEvent.click(screen.getByLabelText('Close'))
    expect(onClose).toHaveBeenCalledTimes(1)
    fireEvent.click(container.firstChild as Element)
    expect(onClose).toHaveBeenCalledTimes(2)
  })

  // A connected mailbox has no alias list on our server: the remote server is the sole authority,
  // so the address is typed and only checked for shape and duplication.
  describe('freeAddress', () => {
    function renderFree(over: Partial<Parameters<typeof IdentityDialog>[0]> = {}) {
      const onSubmit = vi.fn()
      render(<IdentityDialog mode="add" freeAddress taken={['taken@ext.example']}
        initialName="Mick Dubois" onSubmit={onSubmit} onClose={vi.fn()} {...over} />)
      return { onSubmit }
    }

    it('offers a plain address field with its warning, and no alias picker', () => {
      renderFree()
      expect(screen.getByLabelText('Address')).toBeInTheDocument()
      expect(screen.queryByLabelText('Alias')).toBeNull()
      expect(screen.getByText(/The server has the final say/)).toBeInTheDocument()
    })

    it('submits a freely typed address, lowercased', () => {
      const { onSubmit } = renderFree()
      fireEvent.change(screen.getByLabelText('Address'), { target: { value: ' Sales@Ext.example ' } })
      fireEvent.click(screen.getByRole('button', { name: 'Add' }))
      expect(onSubmit).toHaveBeenCalledWith('sales@ext.example', 'Mick Dubois')
    })

    it('refuses a malformed address and one the list already holds', () => {
      renderFree()
      const address = screen.getByLabelText('Address')
      expect(screen.getByRole('button', { name: 'Add' })).toBeDisabled()
      fireEvent.change(address, { target: { value: 'not-an-address' } })
      expect(screen.getByRole('button', { name: 'Add' })).toBeDisabled()
      fireEvent.change(address, { target: { value: 'TAKEN@ext.example' } })
      expect(screen.getByRole('button', { name: 'Add' })).toBeDisabled()
      fireEvent.change(address, { target: { value: 'sales@ext.example' } })
      expect(screen.getByRole('button', { name: 'Add' })).toBeEnabled()
    })

    it('edit mode locks the address to the row being renamed', () => {
      const { onSubmit } = renderFree({
        mode: 'edit', editAddress: 'shared@ext.example', initialName: 'Shared',
        taken: ['shared@ext.example', 'taken@ext.example'],
      })
      const address = screen.getByLabelText('Address')
      expect(address).toHaveValue('shared@ext.example')
      expect(address).toBeDisabled()
      fireEvent.change(screen.getByLabelText('Display name'), { target: { value: 'Support' } })
      fireEvent.click(screen.getByRole('button', { name: 'Save' }))
      expect(onSubmit).toHaveBeenCalledWith('shared@ext.example', 'Support')
    })
  })

  it('edit mode fixes the alias, prefills the name, and keeps the alias on save', () => {
    const onSubmit = vi.fn()
    render(<IdentityDialog mode="edit" taken={[]} editAddress="michel@weesky.be"
      initialName="Michel" onSubmit={onSubmit} onClose={vi.fn()} />)
    const alias = screen.getByLabelText('Alias')
    expect(alias).toHaveValue('michel@weesky.be')
    expect(alias).toBeDisabled()
    fireEvent.change(screen.getByLabelText('Display name'), { target: { value: 'Michel D.' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(onSubmit).toHaveBeenCalledWith('michel@weesky.be', 'Michel D.')
  })
})
