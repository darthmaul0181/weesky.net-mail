import { describe, expect, it, vi, beforeEach } from 'vitest'
import { act, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { createTestQueryClient, settle, waitFor, withQueryClient } from '../../test-utils'
import WhatsNewPage from './WhatsNewPage'

const mocks = vi.hoisted(() => ({ markWhatsNew: vi.fn(), getWhatsNew: vi.fn() }))
vi.mock('../../api.js', () => ({ api: mocks }))

function renderPage(baseline: string) {
  const client = createTestQueryClient()
  client.setQueryData(['appSettings'], { 'app.dailyImage': 'true' })
  client.setQueryData(['whatsNew'], { baseline, items: [] })
  const stamp = '2026-10-14T00:00:00Z'
  mocks.getWhatsNew.mockResolvedValue({ baseline, items: [{ id: 'daily-image', firstShownAt: stamp, listedAt: stamp }] })
  return render(<MemoryRouter><WhatsNewPage /></MemoryRouter>, { wrapper: withQueryClient(client) })
}

beforeEach(() => { vi.clearAllMocks(); mocks.markWhatsNew.mockResolvedValue(null) })

describe('WhatsNewPage', () => {
  it('groups the whole history by version and marks the fresh ones listed', async () => {
    renderPage('0.0.0')
    expect(screen.getByRole('heading', { level: 1, name: "What's new" })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Version 2.4.0' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Image of the day' }).closest('li')).toHaveClass('is-active')
    expect(screen.getByRole('img', { name: 'New' })).toBeInTheDocument()
    await waitFor(() => expect(mocks.markWhatsNew).toHaveBeenCalledWith(['daily-image'], 'listed'))
    // Once: the optimistic stamp and the refetch each hand the page a new `markListed`, which must find nothing left.
    await waitFor(() => expect(mocks.getWhatsNew).toHaveBeenCalled())
    await settle()
    expect(mocks.markWhatsNew).toHaveBeenCalledTimes(1)
  })

  it('lists a novelty once even when the server keeps refusing the stamp', async () => {
    mocks.markWhatsNew.mockImplementation(() => new Promise((_, reject) => setTimeout(() => reject(new Error('offline')), 5)))
    renderPage('0.0.0')
    mocks.getWhatsNew.mockResolvedValue({ baseline: '0.0.0', items: [] })
    await waitFor(() => expect(mocks.getWhatsNew).toHaveBeenCalled())
    await act(() => new Promise(resolve => setTimeout(resolve, 100)))
    expect(mocks.markWhatsNew).toHaveBeenCalledTimes(1)
    expect(mocks.getWhatsNew).toHaveBeenCalledTimes(1)
  })

  it('keeps older novelties in the history without a dot', async () => {
    renderPage('9.9.9')
    // Faded through the row's class, which the stylesheet mutes: the title stays a heading.
    const title = screen.getByRole('heading', { name: 'Image of the day' })
    expect(title.closest('li')).not.toHaveClass('is-active')
    expect(screen.queryByRole('img', { name: 'New' })).toBeNull()
    await settle()
    expect(mocks.markWhatsNew).not.toHaveBeenCalled()
  })
})
