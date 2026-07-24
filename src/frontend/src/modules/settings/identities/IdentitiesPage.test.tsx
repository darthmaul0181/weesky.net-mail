import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import IdentitiesPage from './IdentitiesPage'
import { useIdentities, useReplaceIdentities, useAliases } from '../../mail/queries'
import { useAuth } from '../../../contexts/AuthContext'
import type { SendingIdentity } from '../../mail/api/mailTypes'

vi.mock('../../mail/queries', () => ({
  useIdentities: vi.fn(), useReplaceIdentities: vi.fn(), useAliases: vi.fn(),
}))
vi.mock('../../../contexts/AuthContext', () => ({ useAuth: vi.fn() }))

const identities: SendingIdentity[] = [
  { address: 'mick@weesky.be', displayName: 'Mick Dubois', isDefault: true, isPrimary: true, stale: false, labelIsCustom: false },
  { address: 'michel@weesky.be', displayName: 'Michel', isDefault: false, isPrimary: false, stale: false, labelIsCustom: true },
  { address: 'gone@weesky.be', displayName: 'Ancien', isDefault: false, isPrimary: false, stale: true, labelIsCustom: true },
]

function tileNames(container: HTMLElement) {
  return [...container.querySelectorAll('.admin-list-item-email')].map(e => e.textContent)
}

describe('IdentitiesPage', () => {
  const mutate = vi.fn()

  beforeEach(() => {
    mutate.mockReset()
    vi.mocked(useIdentities).mockReturnValue({ data: identities, isLoading: false, isError: false } as never)
    vi.mocked(useReplaceIdentities).mockReturnValue({ mutate, isPending: false } as never)
    vi.mocked(useAliases).mockReturnValue({ data: [], isLoading: false } as never)
    vi.mocked(useAuth).mockReturnValue({
      identity: { email: 'mick@weesky.be', displayName: 'Mick Dubois', initials: 'MW', subDomains: [] },
    } as never)
  })

  it('renders a tile per identity with its name, address and tags', () => {
    render(<IdentitiesPage />)
    expect(screen.getByText('Mick Dubois')).toBeInTheDocument()
    expect(screen.getByText('michel@weesky.be')).toBeInTheDocument()
    expect(screen.getByText('primary')).toBeInTheDocument()
    expect(screen.getByText('unavailable')).toBeInTheDocument()
  })

  it('pins the primary first, then orders the rest alphabetically by display name', () => {
    const { container } = render(<IdentitiesPage />)
    expect(tileNames(container)).toEqual(['Mick Dubois', 'Ancien', 'Michel'])
  })

  it('moving the default saves the whole list', () => {
    render(<IdentitiesPage />)
    fireEvent.click(screen.getByRole('button', { name: 'Make michel@weesky.be the default' }))
    expect(mutate).toHaveBeenCalledWith(
      [{ address: 'michel@weesky.be', displayName: 'Michel', isDefault: true },
       { address: 'gone@weesky.be', displayName: 'Ancien', isDefault: false }],
      expect.anything())
  })

  it('removing an identity keeps the others', () => {
    render(<IdentitiesPage />)
    fireEvent.click(screen.getByRole('button', { name: 'Remove gone@weesky.be' }))
    expect(mutate).toHaveBeenCalledWith(
      [{ address: 'michel@weesky.be', displayName: 'Michel', isDefault: false }],
      expect.anything())
  })

  it('renaming through the edit dialog saves the new name', () => {
    render(<IdentitiesPage />)
    fireEvent.click(screen.getByRole('button', { name: 'Edit michel@weesky.be' }))
    fireEvent.change(screen.getByLabelText('Display name'), { target: { value: 'Michel D.' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(mutate).toHaveBeenCalledWith(
      [{ address: 'michel@weesky.be', displayName: 'Michel D.', isDefault: false },
       { address: 'gone@weesky.be', displayName: 'Ancien', isDefault: false }],
      expect.anything())
  })

  it('adds the alias picked in the dialog and closes it', () => {
    vi.mocked(useAliases).mockReturnValue({
      data: [{ name: 'support', domain: 'weesky.be' }], isLoading: false,
    } as never)
    render(<IdentitiesPage />)
    fireEvent.click(screen.getByRole('button', { name: 'Add identity' }))
    fireEvent.change(screen.getByLabelText('Alias'), { target: { value: 'support' } })
    fireEvent.mouseDown(screen.getByRole('button', { name: 'support@weesky.be' }))
    fireEvent.click(screen.getByRole('button', { name: 'Add' }))
    expect(mutate).toHaveBeenCalledWith(
      [{ address: 'michel@weesky.be', displayName: 'Michel', isDefault: false },
       { address: 'gone@weesky.be', displayName: 'Ancien', isDefault: false },
       { address: 'support@weesky.be', displayName: 'Mick Dubois', isDefault: false }],
      expect.anything())
    expect(screen.queryByText('Add identity')).toBeNull()
  })

  it('the primary carries no rename, no remove and no default button', () => {
    render(<IdentitiesPage />)
    expect(screen.queryByRole('button', { name: 'Edit mick@weesky.be' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Remove mick@weesky.be' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Make mick@weesky.be the default' })).toBeNull()
    expect(screen.getByText('mick@weesky.be is the default')).toBeInTheDocument()
  })

  it('a stale identity offers no star and no edit, only a remove', () => {
    render(<IdentitiesPage />)
    expect(screen.queryByRole('button', { name: 'Make gone@weesky.be the default' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Edit gone@weesky.be' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Remove gone@weesky.be' })).toBeInTheDocument()
  })

  it('shows the live account name on the primary tile, not a stale query label', () => {
    vi.mocked(useIdentities).mockReturnValue({
      data: [{ ...identities[0], displayName: 'Old Name' }, identities[1], identities[2]],
      isLoading: false, isError: false,
    } as never)
    vi.mocked(useAuth).mockReturnValue({
      identity: { email: 'mick@weesky.be', displayName: 'New Name', initials: 'MW', subDomains: [] },
    } as never)
    render(<IdentitiesPage />)
    const primaryTile = screen.getByText('primary').closest('.admin-list-item')!
    expect(primaryTile.querySelector('.admin-list-item-email')?.textContent).toBe('New Name')
  })

  // Every action PUTs the whole set, and the invalidation's refetch has not landed yet, so an
  // action built on the server snapshot would ship the pre-rename label and undo the rename.
  it('a second action before the refetch builds on the first, not on the server snapshot', () => {
    render(<IdentitiesPage />)
    fireEvent.click(screen.getByRole('button', { name: 'Edit michel@weesky.be' }))
    fireEvent.change(screen.getByLabelText('Display name'), { target: { value: 'Michel D.' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    fireEvent.click(screen.getByRole('button', { name: 'Remove gone@weesky.be' }))
    expect(mutate).toHaveBeenLastCalledWith(
      [{ address: 'michel@weesky.be', displayName: 'Michel D.', isDefault: false }],
      expect.anything())
  })

  it('a refused save shows the message and puts the server state back on screen', () => {
    mutate.mockImplementation((_rows, options) => options.onError(new Error('gone@weesky.be is not yours')))
    render(<IdentitiesPage />)
    fireEvent.click(screen.getByRole('button', { name: 'Edit michel@weesky.be' }))
    fireEvent.change(screen.getByLabelText('Display name'), { target: { value: 'Michel D.' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(screen.getByText('gone@weesky.be is not yours')).toBeInTheDocument()
    expect(screen.getByText('Michel')).toBeInTheDocument()
    expect(screen.queryByText('Michel D.')).toBeNull()
  })

  it('falls back to its own wording when the refusal carries no message', () => {
    mutate.mockImplementation((_rows, options) => options.onError(new Error('')))
    render(<IdentitiesPage />)
    fireEvent.click(screen.getByRole('button', { name: 'Remove gone@weesky.be' }))
    expect(screen.getByText('Could not save your identities')).toBeInTheDocument()
  })

  it('fills the primary star when the alias holding the default is removed', () => {
    vi.mocked(useIdentities).mockReturnValue({
      data: [{ ...identities[0], isDefault: false }, { ...identities[1], isDefault: true }],
      isLoading: false,
    } as never)
    render(<IdentitiesPage />)
    fireEvent.click(screen.getByRole('button', { name: 'Remove michel@weesky.be' }))
    expect(screen.getByText('mick@weesky.be is the default')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Make mick@weesky.be the default' })).toBeNull()
  })

  it('keeps a populated list on screen when a background refetch fails, and says it may be stale', () => {
    vi.mocked(useIdentities).mockReturnValue({ data: identities, isLoading: false, isError: true } as never)
    render(<IdentitiesPage />)
    expect(screen.getByText('michel@weesky.be')).toBeInTheDocument()
    expect(screen.getByText('Could not refresh this list — it may be out of date.')).toBeInTheDocument()
    expect(screen.queryByText('Could not load your identities.')).toBeNull()
  })

  it('reports the failure when there is nothing to show', () => {
    vi.mocked(useIdentities).mockReturnValue({ data: undefined, isLoading: false, isError: true } as never)
    render(<IdentitiesPage />)
    expect(screen.getByText('Could not load your identities.')).toBeInTheDocument()
  })
})
