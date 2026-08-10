import { describe, it, expect, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import AttachmentTray from './AttachmentTray'
import type { StagedItem } from './useStagedAttachments'

function item(overrides: Partial<StagedItem> = {}): StagedItem {
  return {
    key: 'staged-0', id: null, fileName: 'a.txt', size: 4096, progress: 0, error: null,
    accountId: 'primary', ...overrides,
  }
}

describe('AttachmentTray', () => {
  it('shows name and formatted size once uploaded', () => {
    render(<AttachmentTray items={[item({ id: 'id-1', progress: 1 })]} onRemove={vi.fn()} />)
    expect(screen.getByText('a.txt')).toBeInTheDocument()
    expect(screen.getByText('4 KB')).toBeInTheDocument()
  })

  it('shows a progress bar while uploading and not errored', () => {
    render(<AttachmentTray items={[item({ progress: 0.5 })]} onRemove={vi.fn()} />)
    expect(screen.getByRole('progressbar')).toBeInTheDocument()
    expect(screen.queryByText('4 KB')).not.toBeInTheDocument()
  })

  it('shows the error message and no progress bar on a refused file', () => {
    render(<AttachmentTray items={[item({ progress: 1, error: 'The attachment exceeds the 25 MB limit' })]} onRemove={vi.fn()} />)
    expect(screen.getByText('The attachment exceeds the 25 MB limit')).toBeInTheDocument()
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument()
  })

  it('calls onRemove with the item key when ✕ is clicked', () => {
    const onRemove = vi.fn()
    render(<AttachmentTray items={[item()]} onRemove={onRemove} />)
    fireEvent.click(screen.getByRole('button', { name: 'Remove a.txt' }))
    expect(onRemove).toHaveBeenCalledWith('staged-0')
  })

  // The tray is a list of files, and an empty list is not a band: it cost 61px of a 640px phone
  // screen — border included — to hold one button that is now in the toolbar, where the image
  // picker beside it already lived.
  it('renders nothing at all while no file is staged', () => {
    const { container } = render(<AttachmentTray items={[]} onRemove={vi.fn()} />)
    expect(container).toBeEmptyDOMElement()
  })
})
