import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import GeneralPage from './GeneralPage'

const mocks = vi.hoisted(() => ({
  getPreferences: vi.fn(),
  setPreference: vi.fn(),
  playNewMailSound: vi.fn(),
  desktopPermission: vi.fn(),
  requestDesktopPermission: vi.fn(),
}))
vi.mock('../../../api.js', () => ({ api: mocks }))
vi.mock('../../../modules/mail/notify/channels', () => ({
  playNewMailSound: mocks.playNewMailSound,
  desktopPermission: mocks.desktopPermission,
  requestDesktopPermission: mocks.requestDesktopPermission,
}))

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

function renderPage(preferences: Record<string, string> =
  { 'mail.pageSize': '30', 'mail.showPreview': 'true' }) {
  mocks.getPreferences.mockResolvedValue(preferences)
  mocks.setPreference.mockResolvedValue(undefined)
  return render(<GeneralPage />, { wrapper })
}

describe('GeneralPage', () => {
  beforeEach(() => vi.clearAllMocks())

  it('shows the stored page size, not a value of its own', async () => {
    renderPage({ 'mail.pageSize': '100', 'mail.showPreview': 'true' })

    expect(await screen.findByLabelText('Messages per page')).toHaveValue('100')
  })

  it('offers the five steps and All', async () => {
    renderPage()

    const options = Array.from((await screen.findByLabelText('Messages per page')).querySelectorAll('option'))
    expect(options.map(o => o.value)).toEqual(['10', '20', '30', '50', '100', 'all'])
    expect(options.map(o => o.textContent)).toEqual(['10', '20', '30', '50', '100', 'All'])
  })

  it('shows All as the selection when it is stored', async () => {
    renderPage({ 'mail.pageSize': 'all', 'mail.showPreview': 'true' })

    expect(await screen.findByLabelText('Messages per page')).toHaveValue('all')
  })

  it('saves All as the string the backend accepts', async () => {
    renderPage()

    fireEvent.change(await screen.findByLabelText('Messages per page'), { target: { value: 'all' } })

    await waitFor(() =>
      expect(mocks.setPreference).toHaveBeenCalledWith('mail.pageSize', 'all'))
  })

  it('saves a new page size', async () => {
    renderPage()

    fireEvent.change(await screen.findByLabelText('Messages per page'), { target: { value: '50' } })

    await waitFor(() =>
      expect(mocks.setPreference).toHaveBeenCalledWith('mail.pageSize', '50'))
  })

  it('shows the preview toggle on when it is on', async () => {
    renderPage()

    expect(await screen.findByLabelText('Preview in the message list')).toBeChecked()
  })

  it('shows it off when it is off', async () => {
    renderPage({ 'mail.pageSize': '30', 'mail.showPreview': 'false' })

    expect(await screen.findByLabelText('Preview in the message list')).not.toBeChecked()
  })

  it('saves the toggle as a string the backend accepts', async () => {
    renderPage()

    fireEvent.click(await screen.findByLabelText('Preview in the message list'))

    await waitFor(() =>
      expect(mocks.setPreference).toHaveBeenCalledWith('mail.showPreview', 'false'))
  })

  // A boolean shown as a switch, like every other boolean in the app.
  it('uses the house toggle switch', async () => {
    const { container } = renderPage()
    await screen.findByLabelText('Preview in the message list')

    expect(container.querySelector('.toggle-switch')).toBeTruthy()
  })

  it('shows the images toggle off by default and on when it is stored', async () => {
    renderPage()
    expect(await screen.findByLabelText('Always show remote images')).not.toBeChecked()
  })

  it('saves the images toggle and warns about what it costs', async () => {
    renderPage()

    fireEvent.click(await screen.findByLabelText('Always show remote images'))

    await waitFor(() =>
      expect(mocks.setPreference).toHaveBeenCalledWith('mail.alwaysShowImages', 'true'))
    expect(await screen.findByText(/remote images will always load/i)).toBeInTheDocument()
  })

  // The warning the banner used to carry has to survive somewhere, and the moment of choosing
  // is the one place it is useful.
  it('carries the privacy note only while it is on', async () => {
    renderPage({ 'mail.pageSize': '30', 'mail.showPreview': 'true', 'mail.alwaysShowImages': 'true' })

    expect(await screen.findByLabelText('Always show remote images')).toBeChecked()
    expect(screen.getByText(/tells the sender you opened the message/i)).toBeInTheDocument()
  })

  it('hides the privacy note when the setting is off', async () => {
    renderPage()

    expect(await screen.findByLabelText('Always show remote images')).not.toBeChecked()
    expect(screen.queryByText(/tells the sender you opened the message/i)).not.toBeInTheDocument()
  })

  it('shows the contacts row disabled with its note', async () => {
    renderPage()

    const row = await screen.findByLabelText('Trust my contacts')
    expect(row).toBeDisabled()
    expect(row).not.toBeChecked()
    expect(screen.getByText('Available once Contacts ships.')).toBeInTheDocument()
  })

  it('shows the spam score toggle on by default', async () => {
    renderPage()
    expect(await screen.findByLabelText('Show the spam score in the message reader')).toBeChecked()
  })

  it('shows the spam score toggle off when stored off', async () => {
    renderPage({ 'mail.pageSize': '30', 'mail.showSpamScore': 'false' })
    expect(await screen.findByLabelText('Show the spam score in the message reader')).not.toBeChecked()
  })

  it('saves the spam score toggle', async () => {
    renderPage({ 'mail.pageSize': '30', 'mail.showSpamScore': 'true' })

    fireEvent.click(await screen.findByLabelText('Show the spam score in the message reader'))

    await waitFor(() =>
      expect(mocks.setPreference).toHaveBeenCalledWith('mail.showSpamScore', 'false'))
  })

  it('surfaces a failure to save instead of pretending', async () => {
    renderPage()
    mocks.setPreference.mockRejectedValue(new Error('Refused by the server'))

    fireEvent.change(await screen.findByLabelText('Messages per page'), { target: { value: '10' } })

    expect(await screen.findByText('Refused by the server')).toBeInTheDocument()
  })

  it('reports a load failure rather than showing empty controls', async () => {
    mocks.getPreferences.mockRejectedValue(new Error('nope'))
    render(<GeneralPage />, { wrapper })

    expect(await screen.findByText('Could not load the settings.')).toBeInTheDocument()
  })

  // .field-h was drawn for the admin dialogs: a 110px label column and a control on flex:1.
  // A settings page is the opposite shape — sentence-length labels in a wide column — so the
  // rows carry the modifier that widens the label and lets the control size to its content.
  it('lays its rows out as settings rows, not dialog rows', async () => {
    const { container } = renderPage()
    await screen.findByLabelText('Messages per page')

    const rows = container.querySelectorAll('.field-h')
    rows.forEach(row => expect(row).toHaveClass('is-setting'))
  })
})

describe('reading pane', () => {
  it('checks the stored position', async () => {
    renderPage({ 'mail.pageSize': '30', 'mail.showPreview': 'true', 'mail.readingPane': 'bottom' })

    expect(await screen.findByLabelText('Bottom')).toBeChecked()
    expect(screen.getByLabelText('Right')).not.toBeChecked()
    expect(screen.getByRole('radiogroup', { name: 'Reading pane' })).toBeInTheDocument()
  })

  it('saves the chosen position', async () => {
    renderPage({ 'mail.pageSize': '30', 'mail.showPreview': 'true', 'mail.readingPane': 'right' })

    fireEvent.click(await screen.findByLabelText('Hidden'))

    await waitFor(() =>
      expect(mocks.setPreference).toHaveBeenCalledWith('mail.readingPane', 'none'))
    expect(await screen.findByText('Messages will open in place of the list')).toBeInTheDocument()
  })
})

describe('GeneralPage notifications', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.desktopPermission.mockReturnValue('default')
  })
  afterEach(() => vi.unstubAllGlobals())

  it('shows both toggles off by default', async () => {
    renderPage()

    expect(await screen.findByLabelText('Sound on new mail')).not.toBeChecked()
    expect(screen.getByLabelText('Desktop notification on new mail')).not.toBeChecked()
  })

  // Enabling is the interaction that unlocks autoplay, so the confirmation sound doubles as
  // proof it works and as the engagement the browser remembers.
  it('plays the sound when the sound toggle is switched on', async () => {
    renderPage()

    fireEvent.click(await screen.findByLabelText('Sound on new mail'))

    await waitFor(() =>
      expect(mocks.setPreference).toHaveBeenCalledWith('mail.notifySound', 'true'))
    expect(mocks.playNewMailSound).toHaveBeenCalled()
  })

  // Asserted with no await between the click and the expectation, because "inside the gesture"
  // is the property: WebKit revokes transient activation at the first await, so a sound moved
  // after the save is a sound the browser refuses.
  it('plays the sound inside the click, before the save is awaited', async () => {
    renderPage()
    const toggle = await screen.findByLabelText('Sound on new mail')

    fireEvent.click(toggle)

    expect(mocks.playNewMailSound).toHaveBeenCalled()
  })

  it('does not play when the sound toggle is switched off', async () => {
    renderPage({ 'mail.pageSize': '30', 'mail.showPreview': 'true', 'mail.notifySound': 'true' })

    fireEvent.click(await screen.findByLabelText('Sound on new mail'))

    await waitFor(() =>
      expect(mocks.setPreference).toHaveBeenCalledWith('mail.notifySound', 'false'))
    expect(mocks.playNewMailSound).not.toHaveBeenCalled()
  })

  it('asks the browser when the desktop toggle is switched on', async () => {
    mocks.requestDesktopPermission.mockResolvedValue('granted')
    renderPage()

    fireEvent.click(await screen.findByLabelText('Desktop notification on new mail'))

    await waitFor(() =>
      expect(mocks.setPreference).toHaveBeenCalledWith('mail.notifyDesktop', 'true'))
  })

  // Same synchronous assertion, same reason: Safari only shows the prompt while the gesture is
  // live, and an await ahead of the ask makes it silently do nothing.
  it('asks for permission inside the click, before any await', async () => {
    mocks.requestDesktopPermission.mockResolvedValue('granted')
    renderPage()
    const toggle = await screen.findByLabelText('Desktop notification on new mail')

    fireEvent.click(toggle)

    expect(mocks.requestDesktopPermission).toHaveBeenCalled()
  })

  // Storing a true that produces nothing is the lying switch: on, and silent.
  it('leaves the setting off and explains when the browser refuses', async () => {
    mocks.requestDesktopPermission.mockResolvedValue('denied')
    renderPage({ 'mail.pageSize': '30', 'mail.showPreview': 'true', 'mail.notifyDesktop': 'true' })

    fireEvent.click(await screen.findByLabelText('Desktop notification on new mail'))

    expect(await screen.findByText(/blocked/i)).toBeInTheDocument()
    expect(mocks.setPreference).not.toHaveBeenCalledWith('mail.notifyDesktop', 'true')
    expect(screen.getByLabelText('Desktop notification on new mail')).not.toBeChecked()
  })

  // Granted yesterday, revoked today in browser settings, preference still true.
  it('renders the desktop toggle off when the permission was revoked', async () => {
    mocks.desktopPermission.mockReturnValue('denied')
    renderPage({ 'mail.pageSize': '30', 'mail.showPreview': 'true', 'mail.notifyDesktop': 'true' })

    expect(await screen.findByLabelText('Desktop notification on new mail')).not.toBeChecked()
    expect(screen.getByText(/blocked/i)).toBeInTheDocument()
  })

  // Browsers reset notification permission for sites left unvisited, so a stored true can
  // outlive its grant. Anything short of "granted" must read as off.
  it('renders the desktop toggle off when the permission is merely default', async () => {
    mocks.desktopPermission.mockReturnValue('default')
    renderPage({ 'mail.pageSize': '30', 'mail.showPreview': 'true', 'mail.notifyDesktop': 'true' })

    expect(await screen.findByLabelText('Desktop notification on new mail')).not.toBeChecked()
  })

  // Blocked is not a state a click can leave, so the control says so instead of doing nothing.
  it('disables the desktop toggle while the browser blocks it', async () => {
    mocks.desktopPermission.mockReturnValue('denied')
    renderPage()

    expect(await screen.findByLabelText('Desktop notification on new mail')).toBeDisabled()
  })

  // Revoking or granting happens in browser settings, one tab switch away from this page.
  it('re-reads the permission when the tab comes back', async () => {
    mocks.desktopPermission.mockReturnValue('granted')
    renderPage({ 'mail.pageSize': '30', 'mail.showPreview': 'true', 'mail.notifyDesktop': 'true' })

    expect(await screen.findByLabelText('Desktop notification on new mail')).toBeChecked()

    mocks.desktopPermission.mockReturnValue('denied')
    fireEvent(document, new Event('visibilitychange'))

    await waitFor(() =>
      expect(screen.getByLabelText('Desktop notification on new mail')).not.toBeChecked())
    expect(screen.getByText(/blocked/i)).toBeInTheDocument()
  })

  it('says so where the browser has no notifications at all', async () => {
    mocks.desktopPermission.mockReturnValue('unsupported')
    renderPage()

    expect(await screen.findByText(/does not support/i)).toBeInTheDocument()
    expect(screen.getByLabelText('Desktop notification on new mail')).toBeDisabled()
  })

  // Plain HTTP hides Notification entirely, which looks exactly like an old browser. Telling a
  // staging user their browser is at fault sends them to fix the wrong thing.
  it('blames the connection, not the browser, outside a secure context', async () => {
    vi.stubGlobal('isSecureContext', false)
    mocks.desktopPermission.mockReturnValue('unsupported')
    renderPage()

    expect(await screen.findByText(/secure connection/i)).toBeInTheDocument()
    expect(screen.queryByText(/does not support/i)).not.toBeInTheDocument()
    expect(screen.getByLabelText('Desktop notification on new mail')).toBeDisabled()
  })

  it('switching the desktop toggle off needs no permission', async () => {
    mocks.desktopPermission.mockReturnValue('granted')
    renderPage({ 'mail.pageSize': '30', 'mail.showPreview': 'true', 'mail.notifyDesktop': 'true' })

    fireEvent.click(await screen.findByLabelText('Desktop notification on new mail'))

    await waitFor(() =>
      expect(mocks.setPreference).toHaveBeenCalledWith('mail.notifyDesktop', 'false'))
    expect(mocks.requestDesktopPermission).not.toHaveBeenCalled()
  })
})
