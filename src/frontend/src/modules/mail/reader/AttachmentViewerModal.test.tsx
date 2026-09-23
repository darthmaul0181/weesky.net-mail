import { describe, it, expect, vi, beforeEach } from 'vitest'
import { act, render, screen, fireEvent } from '@testing-library/react'
import { Profiler } from 'react'
import AttachmentViewerModal from './AttachmentViewerModal'
import Modal from '../../../components/Modal'
import { fireEscape } from '../../../test-utils'
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
    expect(screen.getByRole('status', { name: 'Loading' })).toBeInTheDocument()
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

  // Server prose never reaches the modal; the local fallback does — see apiErrorMessage.
  it('shows the local fallback inside the modal when the fetch fails', async () => {
    vi.mocked(requestBlob).mockRejectedValue(new Error('boom'))
    renderModal()
    await screen.findByText('Could not load the image')
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

  it('is a modal dialog named by the file it shows', async () => {
    renderModal()
    await screen.findByRole('img', { name: 'photo.png' })

    const dialog = screen.getByRole('dialog', { name: 'photo.png' })
    expect(dialog).toHaveAttribute('aria-modal', 'true')
    expect(dialog).toHaveClass('attachment-viewer')
  })

  it('closes on Escape', async () => {
    const { onClose } = renderModal()
    await screen.findByRole('img', { name: 'photo.png' })

    fireEscape()

    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('takes the focus on open and hands it back to the opener on close', async () => {
    function Host({ open }: { open: boolean }) {
      return (
        <div>
          <button type="button">View</button>
          {open && (
            <AttachmentViewerModal images={[IMAGES[0]]} initialIndex={0}
              onDownload={vi.fn()} onClose={vi.fn()} />
          )}
        </div>
      )
    }
    const { rerender } = render(<Host open={false} />)
    const trigger = screen.getByRole('button', { name: 'View' })
    trigger.focus()

    rerender(<Host open />)
    await screen.findByRole('img', { name: 'photo.png' })
    expect(screen.getByRole('button', { name: 'Close' })).toHaveFocus()

    rerender(<Host open={false} />)
    expect(trigger).toHaveFocus()
  })

  // The arrows are the viewer's own keys, so they are bound to its box rather than to the
  // document: a keypress reaches them only while the viewer is the surface in hand.
  it('navigates with the keyboard arrows and never wraps', async () => {
    renderModal({ images: IMAGES, initialIndex: 1 })
    await screen.findByRole('img', { name: 'diagram.png' })
    const dialog = screen.getByRole('dialog')

    fireEvent.keyDown(dialog, { key: 'ArrowLeft' })
    await screen.findByRole('img', { name: 'photo.png' })
    expect(screen.getByText('1 / 3')).toBeInTheDocument()

    // At the first image, another ArrowLeft stays put — no wrap, no refetch.
    const fetches = vi.mocked(requestBlob).mock.calls.length
    fireEvent.keyDown(dialog, { key: 'ArrowLeft' })
    expect(screen.getByText('1 / 3')).toBeInTheDocument()
    expect(vi.mocked(requestBlob).mock.calls.length).toBe(fetches)

    fireEvent.keyDown(dialog, { key: 'ArrowRight' })
    await screen.findByRole('img', { name: 'diagram.png' })
    expect(screen.getByText('2 / 3')).toBeInTheDocument()
  })

  it('leaves the arrows alone while another dialog stands over it', async () => {
    render(
      <div>
        <AttachmentViewerModal images={IMAGES} initialIndex={1}
          onDownload={vi.fn()} onClose={vi.fn()} />
        <Modal title="Delete this message?" onClose={vi.fn()}><p>body</p></Modal>
      </div>,
    )
    await screen.findByRole('img', { name: 'diagram.png' })

    fireEvent.keyDown(screen.getByRole('dialog', { name: 'Delete this message?' }), { key: 'ArrowRight' })

    expect(screen.getByText('2 / 3')).toBeInTheDocument()
  })

  describe('never commits a frame of the previous image', () => {
    const shown: (string | null)[] = []
    const record = () => shown.push(
      document.querySelector('.attachment-viewer-img')?.getAttribute('src')
        ?? document.querySelector('.attachment-viewer-error')?.textContent ?? null)
    const renderProfiled = () => render(
      <Profiler id="viewer" onRender={record}>
        <AttachmentViewerModal images={IMAGES} initialIndex={0} onDownload={vi.fn()} onClose={vi.fn()} />
      </Profiler>,
    )
    beforeEach(() => {
      shown.length = 0
      let made = 0
      URL.createObjectURL = vi.fn(() => `blob:url-${++made}`)
    })

    it('neither its picture nor its error', async () => {
      vi.mocked(requestBlob).mockRejectedValueOnce(new Error('boom'))
      renderProfiled()
      await screen.findByText('Could not load the image')
      shown.length = 0
      fireEvent.click(screen.getByLabelText('Next image'))
      await screen.findByRole('img', { name: 'diagram.png' })
      expect(shown).not.toContain('Could not load the image')

      shown.length = 0
      fireEvent.click(screen.getByLabelText('Next image'))
      await screen.findByRole('img', { name: 'last.png' })
      expect(shown).not.toContain('blob:url-1')
    })

    it('nor its revoked URL when coming back before the next one loads', async () => {
      renderProfiled()
      await screen.findByRole('img', { name: 'photo.png' })
      vi.mocked(requestBlob).mockImplementationOnce(() => new Promise(() => {}))
      fireEvent.click(screen.getByLabelText('Next image'))
      shown.length = 0
      fireEvent.click(screen.getByLabelText('Previous image'))
      await screen.findByRole('img', { name: 'photo.png' })
      await act(async () => {})
      expect(shown).not.toContain('blob:url-1')
      expect(screen.getByRole('img', { name: 'photo.png' })).toHaveAttribute('src', 'blob:url-2')
    })
  })
})
