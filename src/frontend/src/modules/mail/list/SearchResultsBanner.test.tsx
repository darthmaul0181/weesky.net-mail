import { describe, it, expect, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import SearchResultsBanner from './SearchResultsBanner'

describe('SearchResultsBanner', () => {
  it('quotes the query with its count', () => {
    render(<SearchResultsBanner total={3} label="facture" onClear={() => {}} />)
    expect(screen.getByText('3 results for “facture”')).toBeInTheDocument()
  })

  it('singularizes one result and handles a label-less search', () => {
    const { rerender } = render(<SearchResultsBanner total={1} label="x" onClear={() => {}} />)
    expect(screen.getByText('1 result for “x”')).toBeInTheDocument()
    rerender(<SearchResultsBanner total={2} label={null} onClear={() => {}} />)
    expect(screen.getByText('2 results')).toBeInTheDocument()
  })

  it('says searching while the total is unknown, with Clear still offered', () => {
    const onClear = vi.fn()
    render(<SearchResultsBanner total={null} label="x" onClear={onClear} />)
    expect(screen.getByText('Searching…')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Clear' }))
    expect(onClear).toHaveBeenCalled()
  })

  it('shows a zero count, not the searching state', () => {
    render(<SearchResultsBanner total={0} label={null} onClear={() => {}} />)
    expect(screen.getByText('0 results')).toBeInTheDocument()
    expect(screen.queryByText(/Searching/)).not.toBeInTheDocument()
  })
})
