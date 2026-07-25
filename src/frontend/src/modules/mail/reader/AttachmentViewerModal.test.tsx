import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import AttachmentViewerModal from './AttachmentViewerModal'
import { requestBlob } from '../../../api.js'

vi.mock('../../../api.js', () => ({ requestBlob: vi.fn() }))

beforeEach(() => {
  vi.mocked(requestBlob).mockReset()
  URL.createObjectURL = vi.fn(() => 'blob:mock-url')
  URL.revokeObjectURL = vi.fn()
})

function renderModal(props: Partial<React.ComponentProps<typeof AttachmentViewerModal>> = {}) {
  const onDownload = props.onDownload ?? vi.fn()
  const onClose = props.onClose ?? vi.fn()
  const view = render(
    <AttachmentViewerModal
      src={props.src ?? '/u'}
      fileName={props.fileName ?? 'photo.png'}
      size={props.size ?? 12345}
      onDownload={onDownload}
      onClose={onClose}
    />,
  )
  return { ...view, onDownload, onClose }
}

describe('AttachmentViewerModal', () => {
  it('shows the image once the blob arrives', async () => {
    vi.mocked(requestBlob).mockResolvedValue({ blob: new Blob(['x']), fileName: 'photo.png' })
    renderModal()
    expect(screen.getByText('Loading…')).toBeInTheDocument()
    const img = await screen.findByRole('img', { name: 'photo.png' })
    expect(img).toHaveAttribute('src', 'blob:mock-url')
    expect(screen.getByText('photo.png')).toBeInTheDocument()
  })

  it('revokes the object URL on unmount', async () => {
    vi.mocked(requestBlob).mockResolvedValue({ blob: new Blob(['x']), fileName: 'photo.png' })
    const { unmount } = renderModal()
    await screen.findByRole('img', { name: 'photo.png' })
    unmount()
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:mock-url')
  })

  it('shows the error inside the modal when the fetch fails', async () => {
    vi.mocked(requestBlob).mockRejectedValue(new Error('boom'))
    renderModal()
    await screen.findByText('boom')
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
  })

  it('wires Download and the close button', async () => {
    vi.mocked(requestBlob).mockResolvedValue({ blob: new Blob(['x']), fileName: 'photo.png' })
    const { onDownload, onClose } = renderModal()
    await screen.findByRole('img', { name: 'photo.png' })

    fireEvent.click(screen.getByText('Download'))
    expect(onDownload).toHaveBeenCalledTimes(1)

    fireEvent.click(screen.getByLabelText('Close'))
    expect(onClose).toHaveBeenCalledTimes(1)
  })
})
