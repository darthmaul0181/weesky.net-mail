import { describe, it, expect, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import RecipientsField, { isValidAddress } from './RecipientsField'

function setup(tokens: string[] = []) {
  const onChange = vi.fn()
  render(<RecipientsField id="to" label="To" tokens={tokens} onChange={onChange} />)
  return { onChange }
}

describe('isValidAddress', () => {
  it.each(['a@b.co', 'first.last@sub.domain.org'])('accepts %s', v => expect(isValidAddress(v)).toBe(true))
  it.each(['nope', 'a@b', 'a b@c.d', '@x.y'])('refuses %s', v => expect(isValidAddress(v)).toBe(false))
})

describe('RecipientsField', () => {
  it('commits a token on Enter', () => {
    const { onChange } = setup()
    fireEvent.change(screen.getByLabelText('To'), { target: { value: 'a@b.co' } })
    fireEvent.keyDown(screen.getByLabelText('To'), { key: 'Enter' })
    expect(onChange).toHaveBeenCalledWith(['a@b.co'])
  })

  it('commits on comma, semicolon and blur', () => {
    const { onChange } = setup()
    const input = screen.getByLabelText('To')
    fireEvent.change(input, { target: { value: 'a@b.co' } })
    fireEvent.keyDown(input, { key: ',' })
    expect(onChange).toHaveBeenCalledWith(['a@b.co'])
    fireEvent.change(input, { target: { value: 'c@d.co' } })
    fireEvent.blur(input)
    expect(onChange).toHaveBeenLastCalledWith(['c@d.co'])
  })

  it('splits a paste on separators', () => {
    const { onChange } = setup()
    const input = screen.getByLabelText('To')
    fireEvent.paste(input, { clipboardData: { getData: () => 'a@b.co, c@d.co; e@f.co' } })
    expect(onChange).toHaveBeenCalledWith(['a@b.co', 'c@d.co', 'e@f.co'])
  })

  it('marks an invalid token and removes on its ✕', () => {
    const { onChange } = setup(['bad-token', 'ok@x.co'])
    expect(screen.getByText('bad-token').closest('.recipient-token')).toHaveClass('is-invalid')
    fireEvent.click(screen.getAllByRole('button', { name: /^Remove / })[0])
    expect(onChange).toHaveBeenCalledWith(['ok@x.co'])
  })

  it('Backspace on an empty input removes the last token', () => {
    const { onChange } = setup(['a@b.co'])
    fireEvent.keyDown(screen.getByLabelText('To'), { key: 'Backspace' })
    expect(onChange).toHaveBeenCalledWith([])
  })
})
