import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import IdentitiesPage from './IdentitiesPage'
import { useIdentities, useReplaceIdentities, useAliases } from '../../mail/queries'
import { useAuth } from '../../../contexts/AuthContext'
import type { SendingIdentity } from '../../mail/api/mailTypes'

vi.mock('../../mail/queries', () => ({
  useIdentities: vi.fn(), useReplaceIdentities: vi.fn(), useAliases: vi.fn(),
}))
// Full AccountIdentity shape, not a hand-picked subset — a mock missing a field the component
// reads (labelFallback) would evaluate it as undefined and hide exactly the bug this guards.
vi.mock('../../../contexts/AuthContext', () => ({ useAuth: vi.fn() }))

const identities: SendingIdentity[] = [
  { address: 'mick@weesky.be', displayName: 'Mick Dubois', isDefault: true, isPrimary: true, stale: false, labelIsCustom: false },
  { address: 'michel@weesky.be', displayName: 'Michel', isDefault: false, isPrimary: false, stale: false, labelIsCustom: true },
  { address: 'gone@weesky.be', displayName: 'Ancien', isDefault: false, isPrimary: false, stale: true, labelIsCustom: true },
]

describe('IdentitiesPage', () => {
  const mutate = vi.fn()

  beforeEach(() => {
    // Reset, not clear: the refusal tests install an implementation that must not leak on.
    mutate.mockReset()
    vi.mocked(useIdentities).mockReturnValue({ data: identities, isLoading: false, isError: false } as never)
    vi.mocked(useReplaceIdentities).mockReturnValue({ mutate, isPending: false } as never)
    vi.mocked(useAliases).mockReturnValue({ data: [], isLoading: false } as never)
    vi.mocked(useAuth).mockReturnValue({
      identity: {
        email: 'mick@weesky.be', displayName: 'Mick Dubois', labelFallback: 'Mick Dubois',
        initials: 'MW', subDomains: [],
      },
    } as never)
  })

  it('renders each identity with its address and tags', () => {
    render(<IdentitiesPage />)
    expect(screen.getByText('mick@weesky.be')).toBeInTheDocument()
    expect(screen.getByText('primary')).toBeInTheDocument()
    expect(screen.getByText('unavailable')).toBeInTheDocument()
  })

  it('moving the default saves the whole list', () => {
    render(<IdentitiesPage />)
    fireEvent.click(screen.getByRole('button', { name: 'Make michel@weesky.be the default' }))
    expect(mutate).toHaveBeenCalledWith(
      [{ address: 'michel@weesky.be', displayName: 'Michel', isDefault: true },
       { address: 'gone@weesky.be', displayName: 'Ancien', isDefault: false }],
      expect.anything())
  })

  // The backend's own limit: a longer name would cost a refused PUT rather than a keystroke.
  it('caps the rename input at the stored column length', () => {
    render(<IdentitiesPage />)
    fireEvent.click(screen.getByRole('button', { name: 'Rename michel@weesky.be' }))
    expect(screen.getByLabelText('Display name for michel@weesky.be'))
      .toHaveAttribute('maxlength', '100')
  })

  it('renaming an identity commits on Enter', () => {
    render(<IdentitiesPage />)
    fireEvent.click(screen.getByRole('button', { name: 'Rename michel@weesky.be' }))
    const input = screen.getByLabelText('Display name for michel@weesky.be')
    fireEvent.change(input, { target: { value: 'Michel D.' } })
    fireEvent.keyDown(input, { key: 'Enter' })
    // Sorted the way the server sorts: no default among these two, so by label.
    expect(mutate).toHaveBeenCalledWith(
      [{ address: 'gone@weesky.be', displayName: 'Ancien', isDefault: false },
       { address: 'michel@weesky.be', displayName: 'Michel D.', isDefault: false }],
      expect.anything())
  })

  // Every action PUTs the whole set, and the invalidation's refetch has not landed yet, so an
  // action built on the server snapshot would ship the pre-rename label and undo the rename.
  it('a second action before the refetch builds on the first, not on the server snapshot', () => {
    render(<IdentitiesPage />)
    fireEvent.click(screen.getByRole('button', { name: 'Rename michel@weesky.be' }))
    const input = screen.getByLabelText('Display name for michel@weesky.be')
    fireEvent.change(input, { target: { value: 'Michel D.' } })
    fireEvent.keyDown(input, { key: 'Enter' })

    fireEvent.click(screen.getByRole('button', { name: 'Remove gone@weesky.be' }))
    expect(mutate).toHaveBeenLastCalledWith(
      [{ address: 'michel@weesky.be', displayName: 'Michel D.', isDefault: false }],
      expect.anything())
  })

  it('a refused save shows the message and puts the server state back on screen', () => {
    mutate.mockImplementation((_rows, options) => options.onError(new Error('gone@weesky.be is not yours')))
    render(<IdentitiesPage />)
    fireEvent.click(screen.getByRole('button', { name: 'Rename michel@weesky.be' }))
    const input = screen.getByLabelText('Display name for michel@weesky.be')
    fireEvent.change(input, { target: { value: 'Michel D.' } })
    fireEvent.keyDown(input, { key: 'Enter' })

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

  it('adds the alias picked in the dialog and closes it', () => {
    vi.mocked(useAliases).mockReturnValue({
      data: [{ name: 'support', domain: 'weesky.be' }], isLoading: false,
    } as never)
    render(<IdentitiesPage />)
    fireEvent.click(screen.getByRole('button', { name: '+ Add identity' }))
    fireEvent.click(screen.getByText('support@weesky.be'))
    fireEvent.click(screen.getByRole('button', { name: 'Add' }))

    expect(mutate).toHaveBeenCalledWith(
      [{ address: 'gone@weesky.be', displayName: 'Ancien', isDefault: false },
       { address: 'michel@weesky.be', displayName: 'Michel', isDefault: false },
       { address: 'support@weesky.be', displayName: 'Mick Dubois', isDefault: false }],
      expect.anything())
    expect(screen.queryByText('Add identity')).toBeNull()
  })

  it('states the default without a dead tab stop', () => {
    render(<IdentitiesPage />)
    expect(screen.getByText('mick@weesky.be is the default')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /mick@weesky\.be is the default/ })).toBeNull()
  })

  it('removing an identity keeps the others', () => {
    render(<IdentitiesPage />)
    fireEvent.click(screen.getByRole('button', { name: 'Remove gone@weesky.be' }))
    expect(mutate).toHaveBeenCalledWith(
      [{ address: 'michel@weesky.be', displayName: 'Michel', isDefault: false }],
      expect.anything())
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

  it('a stale identity offers no star and no rename', () => {
    render(<IdentitiesPage />)
    expect(screen.queryByRole('button', { name: 'Make gone@weesky.be the default' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Rename gone@weesky.be' })).toBeNull()
  })

  function renameMichel() {
    fireEvent.click(screen.getByRole('button', { name: 'Rename michel@weesky.be' }))
    const input = screen.getByLabelText('Display name for michel@weesky.be')
    fireEvent.change(input, { target: { value: 'Michel D.' } })
    fireEvent.keyDown(input, { key: 'Enter' })
  }

  // A refetch landing between two saves must not reset the list the second one chains onto.
  it('keeps the in-flight list when a refetch lands mid-save', () => {
    vi.mocked(useReplaceIdentities).mockReturnValue({ mutate, isPending: true } as never)
    const { rerender } = render(<IdentitiesPage />)
    renameMichel()

    vi.mocked(useIdentities).mockReturnValue({ data: [...identities], isLoading: false } as never)
    rerender(<IdentitiesPage />)
    expect(screen.getByText('Michel D.')).toBeInTheDocument()
  })

  it('drops the optimistic list once the refetch lands with nothing in flight', () => {
    const { rerender } = render(<IdentitiesPage />)
    renameMichel()
    expect(screen.getByText('Michel D.')).toBeInTheDocument()

    vi.mocked(useIdentities).mockReturnValue({ data: [...identities], isLoading: false } as never)
    rerender(<IdentitiesPage />)
    expect(screen.getByText('Michel')).toBeInTheDocument()
    expect(screen.queryByText('Michel D.')).toBeNull()
  })

  it('keeps a populated list on screen when a background refetch fails, and says it may be stale', () => {
    vi.mocked(useIdentities).mockReturnValue({ data: identities, isLoading: false, isError: true } as never)
    render(<IdentitiesPage />)
    expect(screen.getByText('michel@weesky.be')).toBeInTheDocument()
    expect(screen.getByText('Could not refresh this list — it may be out of date.')).toBeInTheDocument()
    expect(screen.queryByText('Could not load your identities.')).toBeNull()
  })

  it('says nothing about staleness while the refetches are succeeding', () => {
    render(<IdentitiesPage />)
    expect(screen.queryByText('Could not refresh this list — it may be out of date.')).toBeNull()
  })

  it('reports the failure when there is nothing to show', () => {
    vi.mocked(useIdentities).mockReturnValue({ data: undefined, isLoading: false, isError: true } as never)
    render(<IdentitiesPage />)
    expect(screen.getByText('Could not load your identities.')).toBeInTheDocument()
  })

  it('the primary has no remove button', () => {
    render(<IdentitiesPage />)
    expect(screen.queryByRole('button', { name: 'Remove mick@weesky.be' })).toBeNull()
  })

  // The repro: an uppercase stored userName with a whitespace-only fullName. LabelFor's own
  // fallback is the canonical address, never the stored casing — clearing the primary's label
  // must land on the same string the refetch would bring back, not on displayName's casing.
  it('clearing the primary label falls back to the canonical address, not the stored casing', () => {
    vi.mocked(useAuth).mockReturnValue({
      identity: {
        email: 'Mick@weesky.be', displayName: 'Mick@weesky.be', labelFallback: 'mick@weesky.be',
        initials: 'MW', subDomains: [],
      },
    } as never)
    render(<IdentitiesPage />)
    fireEvent.click(screen.getByRole('button', { name: 'Rename mick@weesky.be' }))
    const input = screen.getByLabelText('Display name for mick@weesky.be')
    fireEvent.change(input, { target: { value: '' } })
    fireEvent.keyDown(input, { key: 'Enter' })

    const primaryRow = screen.getByText('primary').closest('li')!
    expect(primaryRow.querySelector('.identity-name')?.textContent).toBe('mick@weesky.be')
  })
})
