import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import EmptyFolderBanner from './EmptyFolderBanner'

describe('EmptyFolderBanner', () => {
  it('renders trash copy and fires onEmpty from the link', () => {
    const onEmpty = vi.fn()
    render(<EmptyFolderBanner role="trash" total={4} onEmpty={onEmpty} />)
    expect(screen.getByText('Emptying the trash permanently deletes these messages.')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Empty trash now' }))
    expect(onEmpty).toHaveBeenCalledOnce()
  })

  it('renders junk copy', () => {
    render(<EmptyFolderBanner role="junk" total={2} onEmpty={vi.fn()} />)
    expect(screen.getByText('Emptying the junk folder permanently deletes these messages.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Empty junk now' })).toBeInTheDocument()
  })

  it('renders nothing outside trash/junk', () => {
    const { container } = render(<EmptyFolderBanner role="archive" total={9} onEmpty={vi.fn()} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('renders nothing when the folder is empty', () => {
    const { container } = render(<EmptyFolderBanner role="trash" total={0} onEmpty={vi.fn()} />)
    expect(container).toBeEmptyDOMElement()
  })
})
