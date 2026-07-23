import { describe, it, expect, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import AdvancedSearchModal from './AdvancedSearchModal'

function setup(initialSubject = '') {
  const onSearch = vi.fn(); const onClose = vi.fn()
  render(<AdvancedSearchModal folderTitle="Inbox" initialSubject={initialSubject}
    onSearch={onSearch} onClose={onClose} />)
  return { onSearch, onClose }
}

describe('AdvancedSearchModal', () => {
  it('prefills the subject with the quick text', () => {
    setup('facture')
    expect(screen.getByLabelText('Subject')).toHaveValue('facture')
  })

  it('submits the assembled form', () => {
    const { onSearch } = setup()
    fireEvent.change(screen.getByLabelText('From'), { target: { value: 'alice' } })
    fireEvent.change(screen.getByLabelText('Body'), { target: { value: 'invoice' } })
    fireEvent.change(screen.getByLabelText('Date'), { target: { value: '14' } })
    fireEvent.click(screen.getByLabelText('Unread'))
    fireEvent.change(screen.getByLabelText('Search in'), { target: { value: 'all' } })
    fireEvent.click(screen.getByRole('button', { name: 'Search' }))
    expect(onSearch).toHaveBeenCalledWith({
      from: 'alice', to: '', subject: '', text: 'invoice',
      sinceDays: 14, unread: true, flagged: false, hasAttachment: false, allFolders: true,
    })
  })

  it('maps This year to a day count', () => {
    const { onSearch } = setup()
    fireEvent.change(screen.getByLabelText('Subject'), { target: { value: 'x' } })
    fireEvent.change(screen.getByLabelText('Date'), { target: { value: 'year' } })
    fireEvent.click(screen.getByRole('button', { name: 'Search' }))
    const form = onSearch.mock.calls[0][0]
    expect(form.sinceDays).toBeGreaterThanOrEqual(1)
    expect(form.sinceDays).toBeLessThanOrEqual(366)
  })

  it('refuses an empty form', () => {
    const { onSearch } = setup()
    fireEvent.click(screen.getByRole('button', { name: 'Search' }))
    expect(onSearch).not.toHaveBeenCalled()
  })

  it('closes on Escape and on the cross', () => {
    const { onClose } = setup()
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(onClose).toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(onClose).toHaveBeenCalledTimes(2)
  })
})
