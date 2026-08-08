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

  it('shows no identity count in the list header', () => {
    const { container } = render(<IdentitiesPage />)
    expect(container.querySelector('.admin-list-title')).toBeNull()
  })

  it('greys out Add when the account owns no alias', () => {
    render(<IdentitiesPage />)
    expect(screen.getByRole('button', { name: 'Add identity' })).toBeDisabled()
  })

  it('enables Add as soon as the account owns an alias', () => {
    vi.mocked(useAliases).mockReturnValue({
      data: [{ name: 'support', domain: 'weesky.be' }], isLoading: false,
    } as never)
    render(<IdentitiesPage />)
    expect(screen.getByRole('button', { name: 'Add identity' })).toBeEnabled()
  })

  // A network blip must not read as "you have no aliases" and lock the button.
  it('keeps Add enabled while the alias list is loading or failed', () => {
    vi.mocked(useAliases).mockReturnValue({ data: undefined, isLoading: true } as never)
    render(<IdentitiesPage />)
    expect(screen.getByRole('button', { name: 'Add identity' })).toBeEnabled()
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

  // A pending replace blocks the reset the arriving server data would have done, so the optimistic
  // list built from account A used to survive the switch and be PUT over account B's own set.
  it('drops the optimistic list when the account changes under a pending save', () => {
    const authFor = (activeAccountId: string) => ({
      activeAccountId,
      identity: { email: 'mick@weesky.be', displayName: 'Mick Dubois', initials: 'MW', subDomains: [] },
    })
    vi.mocked(useReplaceIdentities).mockReturnValue({ mutate, isPending: true } as never)
    vi.mocked(useAuth).mockReturnValue(authFor('primary') as never)
    const { rerender } = render(<IdentitiesPage />)

    fireEvent.click(screen.getByRole('button', { name: 'Remove michel@weesky.be' }))
    vi.mocked(useAuth).mockReturnValue(authFor('linked-1') as never)
    rerender(<IdentitiesPage />)

    fireEvent.click(screen.getByRole('button', { name: 'Remove gone@weesky.be' }))
    expect(mutate).toHaveBeenLastCalledWith(
      [{ address: 'michel@weesky.be', displayName: 'Michel', isDefault: false }],
      expect.anything())
  })

  // Server prose never reaches the toast; the local fallback does — see apiErrorMessage.
  it('a refused save shows the local fallback and puts the server state back on screen', () => {
    mutate.mockImplementation((_rows, options) => options.onError(new Error('gone@weesky.be is not yours')))
    render(<IdentitiesPage />)
    fireEvent.click(screen.getByRole('button', { name: 'Edit michel@weesky.be' }))
    fireEvent.change(screen.getByLabelText('Display name'), { target: { value: 'Michel D.' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(screen.getByText('Could not save your identities')).toBeInTheDocument()
    expect(screen.getByText('Michel')).toBeInTheDocument()
    expect(screen.queryByText('Michel D.')).toBeNull()
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

  // The identities below already answer under the stored account id while the account list is
  // still in flight, so the variant on screen is a guess — and a save built on the wrong one is
  // refused with a 400.
  it('locks every action while the account list is still loading', () => {
    vi.mocked(useAliases).mockReturnValue({
      data: [{ name: 'support', domain: 'weesky.be' }], isLoading: false,
    } as never)
    vi.mocked(useAuth).mockReturnValue({
      identity: { email: 'mick@weesky.be', displayName: 'Mick Dubois', initials: 'MW', subDomains: [] },
      activeAccount: null, accountsLoading: true,
    } as never)
    render(<IdentitiesPage />)
    expect(screen.getByRole('button', { name: 'Add identity' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Make michel@weesky.be the default' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Edit michel@weesky.be' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Remove michel@weesky.be' })).toBeDisabled()
  })

  // A connected mailbox: same wire shape, but its own address is the default the server forces
  // and there is no alias list to pick an identity from.
  describe('on a connected account', () => {
    const connected: SendingIdentity[] = [
      { address: 'shared@ext.example', displayName: 'Shared box', isDefault: true, isPrimary: true, stale: false, labelIsCustom: true },
      { address: 'team@ext.example', displayName: 'Team', isDefault: false, isPrimary: false, stale: false, labelIsCustom: true },
    ]

    beforeEach(() => {
      vi.mocked(useIdentities).mockReturnValue({ data: connected, isLoading: false, isError: false } as never)
      vi.mocked(useAuth).mockReturnValue({
        identity: { email: 'mick@weesky.be', displayName: 'Mick Dubois', initials: 'MW', subDomains: [] },
        activeAccount: { id: 'linked-1', email: 'shared@ext.example', displayName: 'Shared box', isPrimary: false },
      } as never)
    })

    it('tags the account address, which can be renamed but not removed or unstarred', () => {
      render(<IdentitiesPage />)
      expect(screen.getByText('Account address')).toBeInTheDocument()
      expect(screen.queryByText('primary')).toBeNull()
      expect(screen.getByRole('button', { name: 'Edit shared@ext.example' })).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Remove shared@ext.example' })).toBeNull()
      expect(screen.queryByRole('button', { name: 'Make shared@ext.example the default' })).toBeNull()
      expect(screen.queryByText('shared@ext.example is the default')).toBeNull()
    })

    it('an added identity offers edit and delete, and no star either', () => {
      render(<IdentitiesPage />)
      expect(screen.getByRole('button', { name: 'Edit team@ext.example' })).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Remove team@ext.example' })).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Make team@ext.example the default' })).toBeNull()
    })

    // The label is the account's own, not the primary account's FullName, and the server refuses
    // a set that does not carry the account address.
    it('renames the account address and sends it back in the set', () => {
      render(<IdentitiesPage />)
      expect(screen.getByText('Shared box')).toBeInTheDocument()
      fireEvent.click(screen.getByRole('button', { name: 'Edit shared@ext.example' }))
      fireEvent.change(screen.getByLabelText('Display name'), { target: { value: 'Support' } })
      fireEvent.click(screen.getByRole('button', { name: 'Save' }))
      expect(mutate).toHaveBeenCalledWith(
        [{ address: 'shared@ext.example', displayName: 'Support', isDefault: true },
         { address: 'team@ext.example', displayName: 'Team', isDefault: false }],
        expect.anything())
    })

    it('adds a freely typed address, with no alias to own it', () => {
      render(<IdentitiesPage />)
      const add = screen.getByRole('button', { name: 'Add identity' })
      expect(add).toBeEnabled()
      fireEvent.click(add)
      fireEvent.change(screen.getByLabelText('Address'), { target: { value: 'sales@ext.example' } })
      fireEvent.change(screen.getByLabelText('Display name'), { target: { value: 'Sales' } })
      fireEvent.click(screen.getByRole('button', { name: 'Add' }))
      expect(mutate).toHaveBeenCalledWith(
        [{ address: 'shared@ext.example', displayName: 'Shared box', isDefault: true },
         { address: 'team@ext.example', displayName: 'Team', isDefault: false },
         { address: 'sales@ext.example', displayName: 'Sales', isDefault: false }],
        expect.anything())
    })

    it('drops the alias wording, which names nothing here', () => {
      render(<IdentitiesPage />)
      expect(screen.queryByText(/linked to one of your aliases/)).toBeNull()
    })
  })
})
