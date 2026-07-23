import { describe, it, expect, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import SearchBar from './SearchBar'

function setup() {
  const onSearch = vi.fn(); const onOpenAdvanced = vi.fn(); const onClose = vi.fn()
  render(<SearchBar folderTitle="Inbox" onSearch={onSearch} onOpenAdvanced={onOpenAdvanced} onClose={onClose} />)
  return { onSearch, onOpenAdvanced, onClose, input: screen.getByPlaceholderText('Search in Inbox') }
}

describe('SearchBar', () => {
  it('submits the trimmed text on Enter', () => {
    const { onSearch, input } = setup()
    fireEvent.change(input, { target: { value: '  facture ' } })
    fireEvent.keyDown(input, { key: 'Enter' })
    expect(onSearch).toHaveBeenCalledWith('facture')
  })

  it('ignores Enter on a blank field', () => {
    const { onSearch, input } = setup()
    fireEvent.keyDown(input, { key: 'Enter' })
    expect(onSearch).not.toHaveBeenCalled()
  })

  it('closes on Escape', () => {
    const { onClose, input } = setup()
    fireEvent.keyDown(input, { key: 'Escape' })
    expect(onClose).toHaveBeenCalled()
  })

  it('opens the advanced search with the current text', () => {
    const { onOpenAdvanced, input } = setup()
    fireEvent.change(input, { target: { value: 'alice' } })
    fireEvent.click(screen.getByRole('button', { name: 'Advanced search' }))
    expect(onOpenAdvanced).toHaveBeenCalledWith('alice')
  })

  it('focuses the field on mount', () => {
    const { input } = setup()
    expect(input).toHaveFocus()
  })
})
