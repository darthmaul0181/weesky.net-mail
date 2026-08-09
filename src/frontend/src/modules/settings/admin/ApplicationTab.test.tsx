import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import ApplicationTab from './ApplicationTab'

const mocks = vi.hoisted(() => ({ getAppSettings: vi.fn(), setAppSetting: vi.fn() }))
vi.mock('../../../api.js', () => ({ api: mocks }))

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

const addToast = vi.fn()

function renderTab(settings: Record<string, string> = {
  'app.installable': 'true', 'app.name': 'Snoopy mail', 'app.shortName': 'Snoopy',
}) {
  mocks.getAppSettings.mockResolvedValue(settings)
  mocks.setAppSetting.mockResolvedValue(undefined)
  return render(<ApplicationTab addToast={addToast} />, { wrapper })
}

describe('ApplicationTab', () => {
  beforeEach(() => vi.clearAllMocks())

  it('shows the stored values, not values of its own', async () => {
    renderTab({ 'app.installable': 'true', 'app.name': 'Weesky Mail', 'app.shortName': 'Weesky' })

    expect(await screen.findByLabelText('Application name')).toHaveValue('Weesky Mail')
    expect(screen.getByLabelText('Short name')).toHaveValue('Weesky')
    expect(screen.getByLabelText('Enable app installation')).toBeChecked()
  })

  it('saves the toggle as soon as it is flipped, with the value it was actually flipped to', async () => {
    renderTab()
    const toggle = await screen.findByLabelText('Enable app installation')

    await userEvent.click(toggle)

    await waitFor(() => expect(mocks.setAppSetting)
      .toHaveBeenCalledWith('app.installable', 'false'))
  })

  // Naming an app that is not exposed is meaningless; greying the fields says so without
  // removing the values from the screen.
  it('disables the names while the app is off', async () => {
    renderTab({ 'app.installable': 'false', 'app.name': 'Snoopy mail', 'app.shortName': 'Snoopy' })

    expect(await screen.findByLabelText('Application name')).toBeDisabled()
    expect(screen.getByLabelText('Short name')).toBeDisabled()
  })

  it('saves both names on Save', async () => {
    renderTab()
    const name = await screen.findByLabelText('Application name')

    await userEvent.clear(name)
    await userEvent.type(name, 'Weesky Mail')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(mocks.setAppSetting).toHaveBeenCalledWith('app.name', 'Weesky Mail'))
    expect(mocks.setAppSetting).toHaveBeenCalledWith('app.shortName', 'Snoopy')
  })

  // Server prose never reaches the toast; the local fallback does — see apiErrorMessage.
  it('reports a refused save instead of claiming success', async () => {
    renderTab()
    mocks.setAppSetting.mockRejectedValue(new Error('Short name is too long'))
    await screen.findByLabelText('Application name')

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Could not save the name', 'error'))
  })

  // The global constraint is that a refused save leaves the screen on server state, never on an
  // optimistic lie. None of the tests above type into a field and then have the save rejected —
  // they either don't touch the field, or the mutation succeeds — so none of them would notice
  // a version of this component that just left the rejected text sitting in the input forever.
  it('reverts the name field to the server value after a refused save', async () => {
    renderTab()
    const name = await screen.findByLabelText('Application name')
    mocks.setAppSetting.mockRejectedValue(new Error('Application name is too long'))

    await userEvent.clear(name)
    await userEvent.type(name, 'A name nobody accepted')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(addToast)
      .toHaveBeenCalledWith('Could not save the name', 'error'))
    await waitFor(() => expect(screen.getByLabelText('Application name')).toHaveValue('Snoopy mail'))
  })

  // The two names save sequentially. If the name's own save already succeeded, a refusal on the
  // short name must not drag the name back to its pre-save value — only the field that was
  // actually refused should revert.
  it('does not revert a name that was already accepted when the second save is refused', async () => {
    renderTab()
    const name = await screen.findByLabelText('Application name')
    const shortName = screen.getByLabelText('Short name')

    mocks.setAppSetting.mockImplementation((key: string) => (key === 'app.name'
      ? Promise.resolve(undefined)
      : Promise.reject(new Error('Short name is too long'))))

    await userEvent.clear(name)
    await userEvent.type(name, 'Weesky Mail')
    await userEvent.clear(shortName)
    await userEvent.type(shortName, 'A rejected short name')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Could not save the name', 'error'))
    expect(screen.getByLabelText('Application name')).toHaveValue('Weesky Mail')
    await waitFor(() => expect(screen.getByLabelText('Short name')).toHaveValue('Snoopy'))
  })
})
