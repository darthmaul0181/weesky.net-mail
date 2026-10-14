import { describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import TopBar from './TopBar'
import { createTestQueryClient, settle, withQueryClient } from '../test-utils'

const mocks = vi.hoisted(() => ({ getAppSettings: vi.fn(), getWhatsNew: vi.fn(), markWhatsNew: vi.fn() }))
vi.mock('../api.js', () => ({ api: mocks }))

function renderBar() {
  const client = createTestQueryClient()
  client.setQueryData(['whatsNew'], { baseline: '0.0.0', items: [] })
  return render(<MemoryRouter><TopBar /></MemoryRouter>, { wrapper: withQueryClient(client) })
}

describe('TopBar', () => {
  // The product name is fixed; the admin's application name only titles the tab and the installed app.
  it('names the product beside its logo', async () => {
    mocks.getAppSettings.mockResolvedValue({})
    renderBar()

    expect(screen.getByRole('banner')).toHaveTextContent('Scotty webmail')
    expect(screen.queryByAltText('weesky.net')).not.toBeInTheDocument()
    await settle()
  })

  it('shows the administrator’s logo once the settings name one', async () => {
    mocks.getAppSettings.mockResolvedValue({ 'app.logo': 'v1' })
    const { container } = renderBar()

    await waitFor(() => expect(container.querySelector('.topbar-logo')!.getAttribute('src')).toMatch(/logo\/192[?]v=v1$/))
  })

  it('offers the novelties at the right of the bar', async () => {
    mocks.getAppSettings.mockResolvedValue({})
    renderBar()
    const banner = screen.getByRole('banner')
    expect(banner.lastElementChild).toContainElement(screen.getByRole('button', { name: /What's new/ }))
    await settle()
  })
})
