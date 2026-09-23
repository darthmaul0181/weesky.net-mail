import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import AddressLabel, { AddressList } from './AddressLabel'

describe('AddressLabel', () => {
  it('shows the display name and keeps the full address in a tooltip', () => {
    render(<AddressLabel name="Claude Team" address="no-reply@email.claude.com" />)

    expect(screen.getByText('Claude Team')).toBeInTheDocument()
    expect(screen.getByRole('tooltip'))
      .toHaveTextContent('"Claude Team" <no-reply@email.claude.com>')
  })

  it('falls back to the address when the message carried no name', () => {
    render(<AddressLabel name="" address="bob@x.be" />)

    expect(screen.getByText('bob@x.be')).toBeInTheDocument()
  })

  // The backend already falls back FromName to the address, so a label equal to the address
  // is the no-name case — a bubble repeating the text under the cursor is noise.
  it('offers no tooltip when the label is already the address', () => {
    render(<AddressLabel name="bob@x.be" address="bob@x.be" />)

    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument()
  })

  it('renders the sender as a focusable control, ready to be wired to a composer', () => {
    render(<AddressLabel sender name="Claude Team" address="no-reply@email.claude.com" />)

    expect(screen.getByRole('button', { name: 'Claude Team' })).toBeInTheDocument()
  })

  // The same Tooltip now gives its bubble a real id for the recipient case — the sender button
  // beside it, wrapped by that same Tooltip, was left pointing at nothing.
  it('describes the sender with its own bubble too, once it has one', () => {
    render(<AddressLabel sender name="Claude Team" address="no-reply@email.claude.com" />)

    const trigger = screen.getByRole('button', { name: 'Claude Team' })
    const bubble = screen.getByRole('tooltip')

    expect(trigger).toHaveAttribute('aria-describedby', bubble.id)
    expect(bubble.id).toBeTruthy()
  })

  // Recipients are plain text, so without this they would be unreachable by keyboard and
  // their tooltip — the only place the address is written — invisible to anyone not using a mouse.
  // The precedent is HelpTooltip: a real button, describing itself with the bubble it opens.
  it('turns a recipient carrying a tooltip into a real button, describing itself with the bubble it opens', () => {
    render(<AddressLabel name="Bob" address="bob@x.be" />)

    const trigger = screen.getByRole('button', { name: 'Bob' })
    const bubble = screen.getByRole('tooltip')

    expect(trigger).toHaveAttribute('type', 'button')
    expect(trigger).toHaveAttribute('aria-describedby', bubble.id)
    expect(bubble.id).toBeTruthy()
  })

  it('leaves a recipient with nothing to reveal out of the tab order', () => {
    render(<AddressLabel name="" address="bob@x.be" />)

    expect(screen.getByText('bob@x.be')).not.toHaveAttribute('tabindex')
  })
})

describe('AddressList', () => {
  // Asserted on nameless recipients: a named one renders its tooltip inside the wrapper, so
  // its textContent is "Bob" plus the whole bubble, and a separator assertion would not hold.
  it('separates the addresses with a comma', () => {
    const { container } = render(
      <AddressList addresses={[{ name: '', address: 'bob@x.be' }, { name: '', address: 'eve@x.be' }]} />)

    expect(container.textContent).toBe('bob@x.be, eve@x.be')
  })

  it('renders nothing for an empty list', () => {
    const { container } = render(<AddressList addresses={[]} />)

    expect(container.textContent).toBe('')
  })
})
