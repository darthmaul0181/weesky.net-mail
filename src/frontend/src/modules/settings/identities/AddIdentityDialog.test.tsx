import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import AddIdentityDialog from './AddIdentityDialog'
import { useAliases } from '../../mail/queries'

vi.mock('../../mail/queries', () => ({ useAliases: vi.fn() }))

const aliases = [
  { name: 'michel', domain: 'weesky.be' },
  { name: 'support', domain: 'weesky.be' },
  { name: 'taken', domain: 'weesky.be' },
]

describe('AddIdentityDialog', () => {
  beforeEach(() => {
    vi.mocked(useAliases).mockReturnValue({ data: aliases, isLoading: false } as never)
  })

  function renderDialog(over: Partial<Parameters<typeof AddIdentityDialog>[0]> = {}) {
    const onAdd = vi.fn()
    const { container } = render(<AddIdentityDialog
      taken={['taken@weesky.be']} defaultName="Mick Dubois"
      onAdd={onAdd} onClose={vi.fn()} {...over} />)
    return { onAdd, container }
  }

  it('lists the aliases minus the ones already taken, with a count', () => {
    renderDialog()
    expect(screen.getByText('michel@weesky.be')).toBeInTheDocument()
    expect(screen.queryByText('taken@weesky.be')).toBeNull()
    expect(screen.getByText('2 of 2 aliases')).toBeInTheDocument()
  })

  it('says the aliases could not be loaded rather than counting none', () => {
    vi.mocked(useAliases).mockReturnValue({ data: undefined, isLoading: false, isError: true } as never)
    renderDialog()
    expect(screen.getByText('Could not load your aliases.')).toBeInTheDocument()
    expect(screen.queryByText('0 of 0 aliases')).toBeNull()
  })

  // The backend's own limit: a longer name would cost a refused PUT rather than a keystroke.
  it('caps the display name at the stored column length', () => {
    renderDialog()
    expect(screen.getByLabelText('Display name')).toHaveAttribute('maxlength', '100')
  })

  it('filters as the user types', () => {
    renderDialog()
    fireEvent.change(screen.getByLabelText('Search your aliases'), { target: { value: 'mich' } })
    expect(screen.getByText('michel@weesky.be')).toBeInTheDocument()
    expect(screen.queryByText('support@weesky.be')).toBeNull()
    expect(screen.getByText('1 of 2 aliases')).toBeInTheDocument()
  })

  // The alias side is lowercased before the comparison; the stored side has to be too.
  it('excludes an already-taken address whatever case it is stored in', () => {
    renderDialog({ taken: ['TAKEN@Weesky.be'] })
    expect(screen.queryByText('taken@weesky.be')).toBeNull()
    expect(screen.getByText('2 of 2 aliases')).toBeInTheDocument()
  })

  it('marks the selected alias as pressed, not by styling alone', () => {
    renderDialog()
    const option = screen.getByRole('button', { name: 'michel@weesky.be' })
    expect(option).toHaveAttribute('aria-pressed', 'false')
    fireEvent.click(option)
    expect(option).toHaveAttribute('aria-pressed', 'true')
  })

  it('closes on the overlay but not on the panel', () => {
    const onClose = vi.fn()
    const { container } = renderDialog({ onClose })
    fireEvent.click(screen.getByText('Add identity'))
    expect(onClose).not.toHaveBeenCalled()
    fireEvent.click(container.firstChild as Element)
    expect(onClose).toHaveBeenCalled()
  })

  it('pre-fills the display name and adds the selected alias', () => {
    const { onAdd } = renderDialog()
    fireEvent.click(screen.getByText('michel@weesky.be'))
    expect(screen.getByLabelText('Display name')).toHaveValue('Mick Dubois')
    fireEvent.change(screen.getByLabelText('Display name'), { target: { value: 'Michel D.' } })
    fireEvent.click(screen.getByRole('button', { name: 'Add' }))
    expect(onAdd).toHaveBeenCalledWith('michel@weesky.be', 'Michel D.')
  })

  it('disables Add until an alias is selected and a name is present', () => {
    renderDialog()
    expect(screen.getByRole('button', { name: 'Add' })).toBeDisabled()
    fireEvent.click(screen.getByText('michel@weesky.be'))
    fireEvent.change(screen.getByLabelText('Display name'), { target: { value: '  ' } })
    expect(screen.getByRole('button', { name: 'Add' })).toBeDisabled()
  })
})
