import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import IdentitySelect from './IdentitySelect'
import { useAuth } from '../../../contexts/AuthContext'
import type { SendingIdentity } from '../api/mailTypes'

vi.mock('../../../contexts/AuthContext', () => ({ useAuth: vi.fn() }))

function identity(over: Partial<SendingIdentity>): SendingIdentity {
  return {
    address: 'mick@weesky.be', displayName: 'Mick', isDefault: true,
    isPrimary: true, stale: false, labelIsCustom: false, ...over,
  }
}
const primary = identity({})
const alias = identity({ address: 'michel@weesky.be', displayName: 'Michel', isDefault: false, isPrimary: false })

describe('IdentitySelect', () => {
  beforeEach(() => {
    vi.mocked(useAuth).mockReturnValue({
      identity: { email: 'mick@weesky.be', displayName: 'Mick', initials: 'MW', subDomains: [] },
    } as never)
  })

  it('renders plain text with a single identity — the 2c1 look', () => {
    const { container } = render(<IdentitySelect identities={[primary]} value="mick@weesky.be" onChange={vi.fn()} />)
    const value = container.querySelector('.compose-from-value')
    expect(value).toHaveTextContent('Mick (mick@weesky.be)')
    expect(value?.querySelector('strong')?.textContent).toBe('Mick')
    expect(screen.queryByRole('button')).toBeNull()
  })

  it('offers a menu with several identities, each name bold, and reports the pick', () => {
    const onChange = vi.fn()
    render(<IdentitySelect identities={[primary, alias]} value="mick@weesky.be" onChange={onChange} />)
    fireEvent.click(screen.getByRole('button', { name: 'From identity' }))
    const item = screen.getByRole('menuitem', { name: 'Michel (michel@weesky.be)' })
    expect(item.querySelector('strong')?.textContent).toBe('Michel')
    fireEvent.click(item)
    expect(onChange).toHaveBeenCalledWith('michel@weesky.be')
  })

  it('shows the selected identity, name bold, on the trigger', () => {
    render(<IdentitySelect identities={[primary, alias]} value="michel@weesky.be" onChange={vi.fn()} />)
    const trigger = screen.getByRole('button', { name: 'From identity' })
    expect(trigger).toHaveTextContent('Michel (michel@weesky.be)')
    expect(trigger.querySelector('strong')?.textContent).toBe('Michel')
  })

  it('names the primary from the account display name, not its stored label', () => {
    vi.mocked(useAuth).mockReturnValue({
      identity: { email: 'mick@weesky.be', displayName: 'Mick Dubois', initials: 'MW', subDomains: [] },
    } as never)
    render(<IdentitySelect identities={[primary, alias]} value="mick@weesky.be" onChange={vi.fn()} />)
    fireEvent.click(screen.getByRole('button', { name: 'From identity' }))
    expect(screen.getByRole('menuitem', { name: 'Mick Dubois (mick@weesky.be)' })).toBeInTheDocument()
  })

  // The alias behind the pick is deleted from another client and the refetch marks it stale. The
  // send payload still carries that address, so the line has to keep naming it — and offer the
  // way out — instead of going blank.
  it('keeps naming a chosen identity that goes stale, flagged unavailable', () => {
    const { rerender } = render(
      <IdentitySelect identities={[primary, alias]} value="michel@weesky.be" onChange={vi.fn()} />)
    expect(screen.getByRole('button', { name: 'From identity' })).toHaveTextContent('Michel (michel@weesky.be)')

    rerender(
      <IdentitySelect identities={[primary, { ...alias, stale: true }]} value="michel@weesky.be" onChange={vi.fn()} />)

    const trigger = screen.getByRole('button', { name: 'From identity' })
    expect(trigger).toHaveTextContent('Michel (michel@weesky.be)')
    expect(screen.getByText('unavailable')).toBeInTheDocument()
    fireEvent.click(trigger)
    expect(screen.getByRole('menuitem', { name: 'Mick (mick@weesky.be)' })).toBeInTheDocument()
  })

  it('names a stale identity as plain text when no other one is usable', () => {
    const { container } = render(
      <IdentitySelect identities={[{ ...primary, stale: true }]} value="mick@weesky.be" onChange={vi.fn()} />)

    expect(container.querySelector('.compose-from-value')).toHaveTextContent('Mick (mick@weesky.be)')
    expect(screen.getByText('unavailable')).toBeInTheDocument()
    expect(screen.queryByRole('button')).toBeNull()
  })

  it('never proposes a stale identity', () => {
    const stale = identity({ address: 'gone@weesky.be', displayName: 'Ancien', isDefault: false, isPrimary: false, stale: true })
    render(<IdentitySelect identities={[primary, alias, stale]} value="mick@weesky.be" onChange={vi.fn()} />)
    fireEvent.click(screen.getByRole('button', { name: 'From identity' }))
    expect(screen.queryByRole('menuitem', { name: /gone@weesky.be/ })).toBeNull()
  })
})
