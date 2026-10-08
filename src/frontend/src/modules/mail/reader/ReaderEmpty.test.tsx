import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { act, render, screen, waitFor } from '@testing-library/react'
import { QueryClientProvider } from '@tanstack/react-query'
import { createTestQueryClient } from '../../../test-utils'
import ReaderEmpty from './ReaderEmpty'

const mocks = vi.hoisted(() => ({ getPreferences: vi.fn(), getAppSettings: vi.fn(), getDailyImage: vi.fn() }))
vi.mock('../../../api.js', () => ({ api: mocks }))

class LoadingImage {
  onload: (() => void) | null = null
  set src(_: string) { queueMicrotask(() => this.onload?.()) }
}

function renderPane(style: string) {
  mocks.getPreferences.mockResolvedValue({ 'ui.dailyImage': style })
  const client = createTestQueryClient()
  const view = render(<QueryClientProvider client={client}><ReaderEmpty /></QueryClientProvider>)
  return { client, ...view }
}

beforeEach(() => {
  vi.stubGlobal('Image', LoadingImage)
  mocks.getAppSettings.mockResolvedValue({ 'app.dailyImage': 'true' })
  mocks.getDailyImage.mockResolvedValue({ version: 'v1', title: 'Poulpe fiction', copyright: '© G. Barathieu' })
})
afterEach(() => { vi.unstubAllGlobals(); vi.clearAllMocks() })

describe('ReaderEmpty', () => {
  it('stays the plain text, and asks for nothing, when the user chose none', async () => {
    const { container } = renderPane('none')

    await waitFor(() => expect(mocks.getPreferences).toHaveBeenCalled())
    expect(screen.getByText('Select a message')).toHaveClass('mail-empty')
    expect(container.querySelector('.reader-daily')).toBeNull()
    expect(mocks.getDailyImage).not.toHaveBeenCalled()
  })

  it.each([
    ['fullBleed', 'is-full-bleed'],
    ['postcard', 'is-postcard'],
    ['watermark', 'is-watermark'],
  ])('draws the %s variant with the photo and its credit', async (style, className) => {
    const { container } = renderPane(style)

    await waitFor(() => expect(container.querySelector('.reader-daily')).toHaveClass(className))
    expect(container.querySelector('img')).toHaveAttribute('src', expect.stringMatching(/daily-image\/v1\?lang=en$/))
    expect(screen.getByText('Select a message')).toBeInTheDocument()
    expect(screen.getByText('Poulpe fiction')).toBeInTheDocument()
    expect(screen.getByText('© G. Barathieu')).toBeInTheDocument()
  })

  it('stays the plain text while no image is available', async () => {
    mocks.getDailyImage.mockRejectedValue(new Error('404'))
    const { container } = renderPane('watermark')

    await waitFor(() => expect(mocks.getDailyImage).toHaveBeenCalled())
    expect(screen.getByText('Select a message')).toHaveClass('mail-empty')
    expect(container.querySelector('.reader-daily')).toBeNull()
  })

  it('falls back to the text when the admin switches the feature off', async () => {
    const { client, container } = renderPane('watermark')
    await waitFor(() => expect(container.querySelector('.reader-daily')).not.toBeNull())

    act(() => { client.setQueryData(['appSettings'], { 'app.dailyImage': 'false' }) })

    await waitFor(() => expect(container.querySelector('.reader-daily')).toBeNull())
    expect(screen.getByText('Select a message')).toHaveClass('mail-empty')
  })

  // A folder change unmounts the pane and brings it back: the photo must be there on the first
  // paint, not after a frame of plain text.
  it('draws the photo on the first paint when it comes back', async () => {
    mocks.getPreferences.mockResolvedValue({ 'ui.dailyImage': 'postcard' })
    const client = createTestQueryClient()
    const first = render(<QueryClientProvider client={client}><ReaderEmpty /></QueryClientProvider>)
    await waitFor(() => expect(first.container.querySelector('.reader-daily')).not.toBeNull())
    first.unmount()

    const again = render(<QueryClientProvider client={client}><ReaderEmpty /></QueryClientProvider>)

    expect(again.container.querySelector('.reader-daily')).not.toBeNull()
    expect(again.container.querySelector('.mail-empty')).toBeNull()
  })
})
