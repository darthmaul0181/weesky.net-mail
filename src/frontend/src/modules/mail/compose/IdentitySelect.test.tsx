import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import IdentitySelect from './IdentitySelect'
import type { SendingIdentity } from '../api/mailTypes'

function identity(over: Partial<SendingIdentity>): SendingIdentity {
  return {
    address: 'mick@weesky.be', displayName: 'Mick', isDefault: true,
    isPrimary: true, stale: false, labelIsCustom: false, ...over,
  }
}
const primary = identity({})
const alias = identity({ address: 'michel@weesky.be', displayName: 'Michel', isDefault: false, isPrimary: false })

describe('IdentitySelect', () => {
  it('renders plain text with a single identity — the 2c1 look', () => {
    render(<IdentitySelect identities={[primary]} value="mick@weesky.be" onChange={vi.fn()} />)
    expect(screen.getByText('Mick (mick@weesky.be)')).toBeInTheDocument()
    expect(screen.queryByRole('button')).toBeNull()
  })

  it('offers a menu with several identities and reports the pick', () => {
    const onChange = vi.fn()
    render(<IdentitySelect identities={[primary, alias]} value="mick@weesky.be" onChange={onChange} />)
    fireEvent.click(screen.getByRole('button', { name: 'From identity' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Michel <michel@weesky.be>' }))
    expect(onChange).toHaveBeenCalledWith('michel@weesky.be')
  })

  it('shows the selected identity on the trigger', () => {
    render(<IdentitySelect identities={[primary, alias]} value="michel@weesky.be" onChange={vi.fn()} />)
    expect(screen.getByRole('button', { name: 'From identity' })).toHaveTextContent('Michel (michel@weesky.be)')
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
    expect(screen.getByRole('menuitem', { name: 'Mick <mick@weesky.be>' })).toBeInTheDocument()
  })

  it('names a stale identity as plain text when no other one is usable', () => {
    render(<IdentitySelect identities={[{ ...primary, stale: true }]} value="mick@weesky.be" onChange={vi.fn()} />)

    expect(screen.getByText('Mick (mick@weesky.be)')).toBeInTheDocument()
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
