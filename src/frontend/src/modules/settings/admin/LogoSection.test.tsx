import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import LogoSection from './LogoSection'
import { createTestQueryClient, withQueryClient } from '../../../test-utils'

const mocks = vi.hoisted(() => ({ getAppSettings: vi.fn(), setAppLogo: vi.fn(), deleteAppLogo: vi.fn() }))
vi.mock('../../../api.js', () => ({ api: mocks }))
const prepare = vi.hoisted(() => ({ prepareLogo: vi.fn() }))
vi.mock('./logoImage', async importOriginal => ({ ...(await importOriginal<object>()), ...prepare }))

const addToast = vi.fn()
const images = { 32: new Blob(['a']), 192: new Blob(['b']), 512: new Blob(['c']) }

function renderSection(settings: Record<string, string> = { 'app.logo': '' }) {
  mocks.getAppSettings.mockResolvedValue(settings)
  return render(<LogoSection addToast={addToast} />, { wrapper: withQueryClient(createTestQueryClient()) })
}

async function choose(file = new File(['x'], 'logo.png', { type: 'image/png' })) {
  await userEvent.upload(screen.getByLabelText('Logo file'), file)
}

let previews = 0

beforeEach(() => {
  vi.clearAllMocks()
  previews = 0
  URL.createObjectURL = vi.fn(() => `blob:preview-${++previews}`)
  URL.revokeObjectURL = vi.fn()
  prepare.prepareLogo.mockResolvedValue({ images, lowResolution: false })
  mocks.setAppLogo.mockResolvedValue(null)
  mocks.deleteAppLogo.mockResolvedValue(null)
})

describe('LogoSection', () => {
  it('offers no restore while Scotty is the logo', async () => {
    renderSection()
    expect(await screen.findByRole('button', { name: 'Change logo…' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Restore default' })).not.toBeInTheDocument()
  })

  it('previews a chosen file without sending it', async () => {
    renderSection()
    await screen.findByRole('button', { name: 'Change logo…' })
    await choose()

    expect(await screen.findByRole('button', { name: 'Save' })).toBeInTheDocument()
    expect(screen.getByRole('img', { name: 'Top bar preview' })).toHaveAttribute('src', 'blob:preview-1')
    expect(screen.getByRole('img', { name: 'Browser tab preview' })).toHaveAttribute('src', 'blob:preview-2')
    expect(mocks.setAppLogo).not.toHaveBeenCalled()
  })

  it('saves the three renditions, says so and lets the previews go', async () => {
    renderSection()
    await screen.findByRole('button', { name: 'Change logo…' })
    await choose()
    await userEvent.click(await screen.findByRole('button', { name: 'Save' }))

    await waitFor(() => expect(mocks.setAppLogo).toHaveBeenCalledWith(images))
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('The logo was saved'))
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument()
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:preview-1')
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:preview-2')
  })

  it('cancel drops the preview and sends nothing', async () => {
    renderSection()
    await screen.findByRole('button', { name: 'Change logo…' })
    await choose()
    await userEvent.click(await screen.findByRole('button', { name: 'Cancel' }))

    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument()
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:preview-1')
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:preview-2')
    expect(mocks.setAppLogo).not.toHaveBeenCalled()
  })

  it('lets the previews go when the section closes on a pending choice', async () => {
    const { unmount } = renderSection()
    await screen.findByRole('button', { name: 'Change logo…' })
    await choose()
    await screen.findByRole('button', { name: 'Save' })
    unmount()

    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:preview-1')
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:preview-2')
  })

  it('keeps the latest choice when an earlier one finishes preparing last', async () => {
    let finishFirst: (value: unknown) => void = () => {}
    const second = { 32: new Blob(['d']), 192: new Blob(['e']), 512: new Blob(['f']) }
    prepare.prepareLogo
      .mockImplementationOnce(() => new Promise(resolve => { finishFirst = resolve }))
      .mockResolvedValueOnce({ images: second, lowResolution: false })
    renderSection()
    await screen.findByRole('button', { name: 'Change logo…' })
    await choose()
    await choose(new File(['y'], 'other.png', { type: 'image/png' }))
    await screen.findByRole('button', { name: 'Save' })
    finishFirst({ images, lowResolution: true })
    await new Promise(resolve => setTimeout(resolve, 0))
    expect(screen.queryByText(/blurry/)).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    // Blobs compare equal by value, so the renditions sent are checked by identity.
    await waitFor(() => expect(mocks.setAppLogo).toHaveBeenCalled())
    expect(mocks.setAppLogo.mock.calls[0]?.[0]).toBe(second)
  })

  it('warns that a small image will be blurry once installed', async () => {
    prepare.prepareLogo.mockResolvedValue({ images, lowResolution: true })
    renderSection()
    await screen.findByRole('button', { name: 'Change logo…' })
    await choose()

    expect(await screen.findByText(/blurry/)).toBeInTheDocument()
  })

  it('names an unreadable file and offers no save', async () => {
    prepare.prepareLogo.mockRejectedValue(new Error('application.logoUnreadable'))
    renderSection()
    await screen.findByRole('button', { name: 'Change logo…' })
    await choose()

    expect(await screen.findByRole('alert')).toHaveTextContent('This file isn’t a readable image')
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument()
  })

  it('names an image too detailed to fit the size limits', async () => {
    prepare.prepareLogo.mockRejectedValue(new Error('application.logoTooDetailed'))
    renderSection()
    await screen.findByRole('button', { name: 'Change logo…' })
    await choose()

    expect(await screen.findByRole('alert')).toHaveTextContent(/too detailed/)
  })

  // The API's refusal carries prose, not a known code: the toast falls back to the section's own words.
  it('reports a refused save in a toast and keeps the preview', async () => {
    mocks.setAppLogo.mockRejectedValue(new Error('The 512 px logo is over 1024 KB'))
    renderSection()
    await screen.findByRole('button', { name: 'Change logo…' })
    await choose()
    await userEvent.click(await screen.findByRole('button', { name: 'Save' }))

    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Could not save the logo', 'error'))
    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument()
    expect(URL.revokeObjectURL).not.toHaveBeenCalled()
  })

  it('restores Scotty after confirmation', async () => {
    renderSection({ 'app.logo': 'v1' })
    await userEvent.click(await screen.findByRole('button', { name: 'Restore default' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Restore' }))

    await waitFor(() => expect(mocks.deleteAppLogo).toHaveBeenCalled())
    expect(addToast).toHaveBeenCalledWith('The default logo is back')
  })

  it('reports a refused restore in a toast', async () => {
    mocks.deleteAppLogo.mockRejectedValue(new Error('boom'))
    renderSection({ 'app.logo': 'v1' })
    await userEvent.click(await screen.findByRole('button', { name: 'Restore default' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Restore' }))

    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Could not restore the default logo', 'error'))
  })
})
