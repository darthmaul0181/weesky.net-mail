import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, it, expect, vi } from 'vitest'
import { api, ApiError } from '../../../api.js'
import ChangePasswordSection from './ChangePasswordSection'

function renderSection() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter><ChangePasswordSection /></MemoryRouter>
    </QueryClientProvider>
  )
}

const sync = (configured: boolean) => ({
  serverUrl: 'https://x/dav/', username: 'u', configured, cardDavEnabled: configured, calDavEnabled: false,
})

describe('ChangePasswordSection', () => {
  beforeEach(() => { vi.restoreAllMocks() })

  it('announces a validation error assertively', async () => {
    vi.spyOn(api, 'getDavCredentials').mockResolvedValue(sync(true))
    renderSection()

    await userEvent.click(screen.getByRole('button', { name: 'Change password' }))
    const dialog = screen.getByRole('dialog')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Change password' }))

    expect(within(dialog).getByRole('alert')).toHaveTextContent('Current password is required.')
  })

  it('opens on the current password field', async () => {
    vi.spyOn(api, 'getDavCredentials').mockResolvedValue(sync(true))
    renderSection()

    await userEvent.click(screen.getByRole('button', { name: 'Change password' }))

    expect(screen.getByLabelText('Current password')).toHaveFocus()
  })

  it('warns while the sync state is unknown, then drops the warning for an account that does not sync', async () => {
    vi.spyOn(api, 'getDavCredentials').mockResolvedValue(sync(false))
    renderSection()
    expect(screen.getByText(/stops your devices from syncing/i)).toBeInTheDocument()

    expect(await screen.findByText('The one that opens your mailbox.')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Change password' }))
    expect(within(screen.getByRole('dialog')).queryByText(/will stop syncing/i)).not.toBeInTheDocument()
  })

  it('names no sync on a deployment that publishes no sync address', async () => {
    vi.spyOn(api, 'getDavCredentials').mockRejectedValue(new ApiError('Not found', 404, null))
    renderSection()

    expect(await screen.findByText('The one that opens your mailbox.')).toBeInTheDocument()
  })

  it('keeps the warning when the sync state cannot be read', async () => {
    const read = vi.spyOn(api, 'getDavCredentials').mockRejectedValue(new ApiError('Boom', 500, null))
    renderSection()

    await vi.waitFor(() => expect(read).toHaveBeenCalled())
    await userEvent.click(screen.getByRole('button', { name: 'Change password' }))
    expect(within(screen.getByRole('dialog')).getByText(/will stop syncing/i)).toBeInTheDocument()
  })
})
