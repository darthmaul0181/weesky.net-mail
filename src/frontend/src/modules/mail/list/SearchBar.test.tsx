import { describe, it, expect, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { useRef } from 'react'
import SearchBar from './SearchBar'
import { useDismiss } from '../../../hooks/useDismiss'

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

  // Safari focuses no button on a click, so the caret can still be here while the reader's kebab
  // is open: that Escape belongs to the menu, and closing the bar under it leaves the menu open.
  it('leaves Escape to an open layer', () => {
    function Menu() {
      const root = useRef<HTMLDivElement>(null)
      useDismiss({ open: true, rootRef: root, onDismiss: () => {} })
      return <div ref={root}><button type="button">Entry</button></div>
    }
    const { onClose, input } = setup()
    render(<Menu />)

    fireEvent.keyDown(input, { key: 'Escape' })

    expect(onClose).not.toHaveBeenCalled()
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

  // The placeholder alone is not a reliable accessible name — gone once typed, unreliable in AT.
  it('names the field for assistive tech', () => {
    setup()
    expect(screen.getByRole('searchbox', { name: 'Search in Inbox' })).toBeInTheDocument()
  })
})
