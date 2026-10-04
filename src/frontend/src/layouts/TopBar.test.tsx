import { describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import TopBar from './TopBar'
import { createTestQueryClient, settle, withQueryClient } from '../test-utils'

const mocks = vi.hoisted(() => ({ getAppSettings: vi.fn() }))
vi.mock('../api.js', () => ({ api: mocks }))

describe('TopBar', () => {
  // The product name is fixed; the admin's application name only titles the tab and the installed app.
  it('names the product beside its logo', async () => {
    mocks.getAppSettings.mockResolvedValue({})
    render(<TopBar />, { wrapper: withQueryClient(createTestQueryClient()) })

    expect(screen.getByRole('banner')).toHaveTextContent('Scotty webmail')
    expect(screen.queryByAltText('weesky.net')).not.toBeInTheDocument()
    await settle()
  })

  it('shows the administrator’s logo once the settings name one', async () => {
    mocks.getAppSettings.mockResolvedValue({ 'app.logo': 'v1' })
    const { container } = render(<TopBar />, { wrapper: withQueryClient(createTestQueryClient()) })

    await waitFor(() => expect(container.querySelector('.topbar-logo')!.getAttribute('src')).toMatch(/logo\/192[?]v=v1$/))
  })
})
