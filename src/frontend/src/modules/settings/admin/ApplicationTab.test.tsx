import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import ApplicationTab from './ApplicationTab'
import { createTestQueryClient, setupUser } from '../../../test-utils'

const mocks = vi.hoisted(() => ({
  getAppSettings: vi.fn(),
  setAppSetting: vi.fn(),
}))
vi.mock('../../../api.js', () => ({ api: mocks }))
// Each section the tab mounts has its own test file and its own API calls; here they would only
// re-render on every keystroke and fail on calls this mock does not answer.
vi.mock('./LogoSection', () => ({ default: () => <div data-section="logo" /> }))
vi.mock('./SchedulingAccountSection', () => ({ default: () => <div data-section="scheduling" /> }))
vi.mock('./DeliveryRepliesSection', () => ({ default: () => <div data-section="delivery" /> }))

function wrapper({ children }: { children: ReactNode }) {
  const client = createTestQueryClient()
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

const addToast = vi.fn()

function renderTab(settings: Record<string, string> = {
  'app.installable': 'true', 'app.name': 'Scotty mail', 'app.shortName': 'Scotty',
}) {
  mocks.getAppSettings.mockResolvedValue(settings)
  mocks.setAppSetting.mockResolvedValue(undefined)
  return render(<ApplicationTab addToast={addToast} />, { wrapper })
}

describe('ApplicationTab', () => {
  let user: ReturnType<typeof setupUser>
  beforeEach(() => {
    vi.clearAllMocks()
    user = setupUser()
  })

  it('mounts its three sections, logo first and delivery replies last', async () => {
    const { container } = renderTab()
    await screen.findByLabelText('Application name')

    expect([...container.querySelectorAll('[data-section]')].map(e => e.getAttribute('data-section')))
      .toEqual(['logo', 'scheduling', 'delivery'])
  })

  it('shows the stored values, not values of its own', async () => {
    renderTab({ 'app.installable': 'true', 'app.name': 'Weesky Mail', 'app.shortName': 'Weesky' })

    expect(await screen.findByLabelText('Application name')).toHaveValue('Weesky Mail')
    expect(screen.getByLabelText('Short name')).toHaveValue('Weesky')
    expect(screen.getByLabelText('Enable app installation')).toBeChecked()
  })

  it('saves the toggle as soon as it is flipped, with the value it was actually flipped to', async () => {
    renderTab()
    const toggle = await screen.findByLabelText('Enable app installation')

    await user.click(toggle)

    await waitFor(() => expect(mocks.setAppSetting)
      .toHaveBeenCalledWith('app.installable', 'false'))
  })

  // Naming an app that is not exposed is meaningless; greying the fields says so without
  // removing the values from the screen.
  it('disables the names while the app is off', async () => {
    renderTab({ 'app.installable': 'false', 'app.name': 'Scotty mail', 'app.shortName': 'Scotty' })

    expect(await screen.findByLabelText('Application name')).toBeDisabled()
    expect(screen.getByLabelText('Short name')).toBeDisabled()
  })

  it('saves both names on Save', async () => {
    renderTab()
    const name = await screen.findByLabelText('Application name')

    await user.clear(name)
    await user.type(name, 'Weesky Mail')
    await user.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(mocks.setAppSetting).toHaveBeenCalledWith('app.name', 'Weesky Mail'))
    expect(mocks.setAppSetting).toHaveBeenCalledWith('app.shortName', 'Scotty')
  })

  // Server prose never reaches the toast; the local fallback does — see apiErrorMessage.
  it('reports a refused save instead of claiming success', async () => {
    renderTab()
    mocks.setAppSetting.mockRejectedValue(new Error('Short name is too long'))
    await screen.findByLabelText('Application name')

    await user.click(screen.getByRole('button', { name: 'Save' }))

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

    await user.clear(name)
    await user.type(name, 'A name nobody accepted')
    await user.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(addToast)
      .toHaveBeenCalledWith('Could not save the name', 'error'))
    await waitFor(() => expect(screen.getByLabelText('Application name')).toHaveValue('Scotty mail'))
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

    await user.clear(name)
    await user.type(name, 'Weesky Mail')
    await user.clear(shortName)
    await user.type(shortName, 'A rejected short name')
    await user.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Could not save the name', 'error'))
    expect(screen.getByLabelText('Application name')).toHaveValue('Weesky Mail')
    await waitFor(() => expect(screen.getByLabelText('Short name')).toHaveValue('Scotty'))
  })
})
