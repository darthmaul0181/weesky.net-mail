import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import AttachmentViewerModal from './AttachmentViewerModal'
import { requestBlob } from '../../../api.js'

vi.mock('../../../api.js', () => ({ requestBlob: vi.fn() }))

const IMAGES = [
  { part: '2', src: '/u/2', fileName: 'photo.png', size: 12345 },
  { part: '3', src: '/u/3', fileName: 'diagram.png', size: 200 },
  { part: '5', src: '/u/5', fileName: 'last.png', size: 9 },
]

beforeEach(() => {
  vi.mocked(requestBlob).mockReset()
  vi.mocked(requestBlob).mockImplementation(() => Promise.resolve({ blob: new Blob(['x']), fileName: 'x' }))
  URL.createObjectURL = vi.fn(() => 'blob:mock-url')
  URL.revokeObjectURL = vi.fn()
})

function renderModal(props: Partial<React.ComponentProps<typeof AttachmentViewerModal>> = {}) {
  const onDownload = props.onDownload ?? vi.fn()
  const onClose = props.onClose ?? vi.fn()
  const view = render(
    <AttachmentViewerModal
      images={props.images ?? [IMAGES[0]]}
      initialIndex={props.initialIndex ?? 0}
      onDownload={onDownload}
      onClose={onClose}
    />,
  )
  return { ...view, onDownload, onClose }
}

describe('AttachmentViewerModal', () => {
  it('shows the image once the blob arrives', async () => {
    renderModal()
    expect(screen.getByText('Loading…')).toBeInTheDocument()
    const img = await screen.findByRole('img', { name: 'photo.png' })
    expect(img).toHaveAttribute('src', 'blob:mock-url')
    expect(screen.getByText('photo.png')).toBeInTheDocument()
    expect(requestBlob).toHaveBeenCalledWith('/u/2')
  })

  it('revokes the object URL on unmount', async () => {
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
    const { onDownload, onClose } = renderModal()
    await screen.findByRole('img', { name: 'photo.png' })

    fireEvent.click(screen.getByText('Download'))
    expect(onDownload).toHaveBeenCalledWith(IMAGES[0])

    fireEvent.click(screen.getByLabelText('Close'))
    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('shows neither arrows nor a counter for a single image', async () => {
    renderModal()
    await screen.findByRole('img', { name: 'photo.png' })
    expect(screen.queryByLabelText('Previous image')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Next image')).not.toBeInTheDocument()
    expect(screen.queryByText('1 / 1')).not.toBeInTheDocument()
  })

  it('navigates between images with the arrows and counts the position', async () => {
    renderModal({ images: IMAGES })
    await screen.findByRole('img', { name: 'photo.png' })
    expect(screen.getByText('1 / 3')).toBeInTheDocument()
    expect(screen.getByLabelText('Previous image')).toBeDisabled()

    fireEvent.click(screen.getByLabelText('Next image'))
    await screen.findByRole('img', { name: 'diagram.png' })
    expect(requestBlob).toHaveBeenCalledWith('/u/3')
    expect(screen.getByText('2 / 3')).toBeInTheDocument()
    expect(screen.getByLabelText('Previous image')).toBeEnabled()
    // Moving on revoked the previous image's URL — the src-change path, not just unmount.
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:mock-url')

    fireEvent.click(screen.getByLabelText('Next image'))
    await screen.findByRole('img', { name: 'last.png' })
    expect(screen.getByText('3 / 3')).toBeInTheDocument()
    expect(screen.getByLabelText('Next image')).toBeDisabled()
  })

  it('navigates with the keyboard arrows and never wraps', async () => {
    renderModal({ images: IMAGES, initialIndex: 1 })
    await screen.findByRole('img', { name: 'diagram.png' })

    fireEvent.keyDown(document, { key: 'ArrowLeft' })
    await screen.findByRole('img', { name: 'photo.png' })
    expect(screen.getByText('1 / 3')).toBeInTheDocument()

    // At the first image, another ArrowLeft stays put — no wrap, no refetch.
    const fetches = vi.mocked(requestBlob).mock.calls.length
    fireEvent.keyDown(document, { key: 'ArrowLeft' })
    expect(screen.getByText('1 / 3')).toBeInTheDocument()
    expect(vi.mocked(requestBlob).mock.calls.length).toBe(fetches)

    fireEvent.keyDown(document, { key: 'ArrowRight' })
    await screen.findByRole('img', { name: 'diagram.png' })
    expect(screen.getByText('2 / 3')).toBeInTheDocument()
  })
})
