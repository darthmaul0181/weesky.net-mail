import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { createTestQueryClient, fireEscape, setupUser, withQueryClient } from '../../test-utils'
import WhatsNewButton from './WhatsNewButton'

const mocks = vi.hoisted(() => ({ markWhatsNew: vi.fn(), getWhatsNew: vi.fn() }))
vi.mock('../../api.js', () => ({ api: mocks }))
const STAMP = '2026-10-14T00:00:00Z'

function renderButton(state: { baseline: string; items: object[] } = { baseline: '0.0.0', items: [] }) {
  const client = createTestQueryClient()
  client.setQueryData(['appSettings'], { 'app.dailyImage': 'true' })
  client.setQueryData(['whatsNew'], state)
  // The refetch after a mark answers as the server would: listed.
  mocks.getWhatsNew.mockResolvedValue({ ...state, items: [{ id: 'daily-image', firstShownAt: STAMP, listedAt: STAMP }] })
  return render(
    <MemoryRouter initialEntries={['/mail']}>
      <WhatsNewButton />
      <Routes>
        <Route path="/mail" element={null} />
        <Route path="/settings/appearance" element={<p>Appearance page</p>} />
        <Route path="/settings/whats-new" element={<p>History page</p>} />
      </Routes>
    </MemoryRouter>,
    { wrapper: withQueryClient(client) },
  )
}

beforeEach(() => { vi.clearAllMocks(); mocks.markWhatsNew.mockResolvedValue(null) })

describe('WhatsNewButton', () => {
  it('announces how many novelties are waiting', () => {
    renderButton()
    expect(screen.getByRole('button', { name: "What's new, 1 new" })).toBeInTheDocument()
  })

  it('opens the list, marks it listed, and the button dot goes out', async () => {
    const user = setupUser()
    renderButton()
    await user.click(screen.getByRole('button', { name: "What's new, 1 new" }))
    // Named by its own heading, once: a region, not an unnamed section beside a title.
    expect(screen.getByRole('region', { name: "What's new" })).toContainElement(screen.getByRole('heading', { name: "What's new" }))
    expect(screen.getByRole('heading', { name: 'Image of the day' })).toBeInTheDocument()
    expect(mocks.markWhatsNew).toHaveBeenCalledWith(['daily-image'], 'listed')
    expect(await screen.findByRole('button', { name: "What's new" })).toHaveAttribute('aria-expanded', 'true')
  })

  it('leads Show me to the novelty and closes', async () => {
    const user = setupUser()
    renderButton()
    await user.click(screen.getByRole('button', { name: /What's new/ }))
    await user.click(screen.getByRole('button', { name: 'Show me' }))
    expect(screen.getByText('Appearance page')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Image of the day' })).toBeNull()
  })

  it('leads See all to the history and closes', async () => {
    const user = setupUser()
    renderButton()
    await user.click(screen.getByRole('button', { name: /What's new/ }))
    await user.click(screen.getByRole('link', { name: 'See all' }))
    expect(screen.getByText('History page')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Image of the day' })).toBeNull()
  })

  it('says so when nothing came after the account joined', async () => {
    const user = setupUser()
    renderButton({ baseline: '9.9.9', items: [] })
    await user.click(screen.getByRole('button', { name: "What's new" }))
    expect(screen.getByText('Nothing new since you joined.')).toBeInTheDocument()
    expect(mocks.markWhatsNew).not.toHaveBeenCalled()
  })

  it('keeps a listed novelty in the list, dot out, until it is seen', async () => {
    const user = setupUser()
    renderButton({ baseline: '0.0.0', items: [{ id: 'daily-image', firstShownAt: STAMP, listedAt: STAMP }] })
    await user.click(screen.getByRole('button', { name: "What's new" }))
    expect(screen.getByRole('heading', { name: 'Image of the day' })).toBeInTheDocument()
    expect(screen.getByRole('img', { name: 'New' })).toBeInTheDocument()
    expect(mocks.markWhatsNew).not.toHaveBeenCalled()
  })

  it('closes on Escape and gives the focus back', async () => {
    const user = setupUser()
    renderButton()
    const button = screen.getByRole('button', { name: /What's new/ })
    await user.click(button)
    fireEscape()
    await waitFor(() => expect(screen.queryByRole('heading', { name: 'Image of the day' })).toBeNull())
    expect(button).toHaveFocus()
    expect(button).toHaveAttribute('aria-expanded', 'false')
  })

  it('closes on a press outside', async () => {
    const user = setupUser()
    renderButton()
    await user.click(screen.getByRole('button', { name: /What's new/ }))
    await user.click(document.body)
    expect(screen.queryByRole('heading', { name: 'Image of the day' })).toBeNull()
  })
})
