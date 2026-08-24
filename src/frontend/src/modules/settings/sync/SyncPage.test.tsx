import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import i18next from 'i18next'
import SyncPage from './SyncPage'
import { api, ApiError } from '../../../api.js'

// The rest of the module comes through untouched: the 404 branch is an `instanceof ApiError`
// check, so a hand-rolled stand-in would not match the class the component imports.
vi.mock('../../../api.js', async importOriginal => ({
  ...await importOriginal<typeof import('../../../api.js')>(),
  api: {
    getDavCredentials: vi.fn(),
    setDavCardDav: vi.fn(),
    regenerateDavSecret: vi.fn(),
  },
}))

const OFF = {
  serverUrl: 'https://api.mail.weesky.net', username: 'alice@weesky.be',
  configured: false, cardDavEnabled: false,
}
const ON = { ...OFF, configured: true, cardDavEnabled: true }

beforeEach(() => {
  vi.mocked(api.getDavCredentials).mockResolvedValue(OFF)
  vi.mocked(api.setDavCardDav).mockResolvedValue(ON)
  vi.mocked(api.regenerateDavSecret).mockResolvedValue({ ...ON, password: 'TSRQPONMLKJIHGFEDCBA' })
})

describe('SyncPage', () => {
  it('shows the address the server gave, not one composed here', async () => {
    render(<SyncPage />)

    expect(await screen.findByText('https://api.mail.weesky.net')).toBeInTheDocument()
    expect(screen.getByText('alice@weesky.be')).toBeInTheDocument()
  })

  // The very first visit, which every other case here skips by starting from the ON fixture: both
  // branches used to be gated on `configured`, so the row rendered its label beside nothing at all.
  it('tells a first-time visitor how to get a password rather than leaving the row empty', async () => {
    render(<SyncPage />)

    expect(await screen.findByText('Turn Contacts (CardDAV) on to get a password')).toBeInTheDocument()
  })

  it('says a deployment with no sync address does not offer it, rather than blaming the load', async () => {
    vi.mocked(api.getDavCredentials).mockRejectedValue(new ApiError('Not served', 404))
    render(<SyncPage />)

    expect(await screen.findByText('This server does not offer synchronisation.')).toBeInTheDocument()
    expect(screen.queryByText('Could not load the sync settings')).not.toBeInTheDocument()
  })

  it('still blames the load on a failure that is not a 404', async () => {
    vi.mocked(api.getDavCredentials).mockRejectedValue(new ApiError('Boom', 500))
    render(<SyncPage />)

    expect(await screen.findByText('Could not load the sync settings')).toBeInTheDocument()
  })

  it('turning the switch on generates and shows the secret in one gesture', async () => {
    vi.mocked(api.setDavCardDav).mockResolvedValue({ ...ON, password: 'ABCDEFGHIJKLMNOPQRST' })
    render(<SyncPage />)

    await userEvent.click(await screen.findByRole('checkbox', { name: 'Contacts (CardDAV)' }))

    expect(api.setDavCardDav).toHaveBeenCalledWith(true)
    expect(await screen.findByText('ABCDEFGHIJKLMNOPQRST')).toBeInTheDocument()
    // Shown once, and the screen says so rather than letting the user find out later.
    expect(screen.getByText('Copy it now — it will not be shown again.')).toBeInTheDocument()
  })

  it('turning it back on shows no secret and never offers to reveal one', async () => {
    vi.mocked(api.getDavCredentials).mockResolvedValue({ ...ON, cardDavEnabled: false })
    vi.mocked(api.setDavCardDav).mockResolvedValue(ON)
    render(<SyncPage />)

    await userEvent.click(await screen.findByRole('checkbox', { name: 'Contacts (CardDAV)' }))

    await waitFor(() => expect(api.setDavCardDav).toHaveBeenCalledWith(true))
    expect(screen.getByText('Hidden — regenerate to get a new one')).toBeInTheDocument()
    // The assertion that keeps the door shut: there is nothing to reveal, and never will be.
    expect(screen.queryByRole('button', { name: /reveal|show/i })).not.toBeInTheDocument()
  })

  it('turning it off keeps the connection values on screen', async () => {
    vi.mocked(api.getDavCredentials).mockResolvedValue(ON)
    vi.mocked(api.setDavCardDav).mockResolvedValue({ ...ON, cardDavEnabled: false })
    render(<SyncPage />)

    await userEvent.click(await screen.findByRole('checkbox', { name: 'Contacts (CardDAV)' }))

    await waitFor(() => expect(api.setDavCardDav).toHaveBeenCalledWith(false))
    // Configured stays configured: the values are what one comes back for on a second device.
    expect(screen.getByText('alice@weesky.be')).toBeInTheDocument()
  })

  it('regenerating asks first, and the question names the consequence and the order', async () => {
    vi.mocked(api.getDavCredentials).mockResolvedValue(ON)
    render(<SyncPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Regenerate' }))

    expect(screen.getByText(/Every device will stop syncing until you enter the new password/))
      .toBeInTheDocument()
    // The order is the consequential half: IsBlocked runs before the digest comparison and only a
    // success clears the key, so regenerating while devices still sync locks the account out —
    // and the correct new secret then answers 429 until the window expires.
    expect(screen.getByText(/Turn syncing off on your devices first/)).toBeInTheDocument()
    expect(api.regenerateDavSecret).not.toHaveBeenCalled()
  })

  it('confirming regenerates and shows the new secret', async () => {
    vi.mocked(api.getDavCredentials).mockResolvedValue(ON)
    render(<SyncPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Regenerate' }))
    await userEvent.click(screen.getByRole('button', { name: 'Regenerate the sync password?' }))

    expect(await screen.findByText('TSRQPONMLKJIHGFEDCBA')).toBeInTheDocument()
  })

  it('says never used rather than leaving the line blank', async () => {
    // The most common symptom of a client configuration that never got through.
    vi.mocked(api.getDavCredentials).mockResolvedValue(ON)
    render(<SyncPage />)

    expect(await screen.findByText('Never used')).toBeInTheDocument()
  })

  it('renders a used date in the relative past', async () => {
    vi.mocked(api.getDavCredentials).mockResolvedValue({
      ...ON, lastUsedAt: new Date(Date.now() - 2 * 3600 * 1000).toISOString(),
    })
    render(<SyncPage />)

    expect(await screen.findByText('2 hours ago')).toBeInTheDocument()
  })

  it('moves the switch at the click and puts it back when the write is refused', async () => {
    let refuse: (error: Error) => void = () => {}
    vi.mocked(api.setDavCardDav).mockReturnValue(new Promise((_, reject) => { refuse = reject }))
    render(<SyncPage />)

    const box = await screen.findByRole('checkbox', { name: 'Contacts (CardDAV)' })
    await userEvent.click(box)
    // Pure server state would leave it off for the whole round trip, with nothing acknowledging
    // the click; a refusal is what has to put it back.
    expect(box).toBeChecked()

    refuse(new Error('refused'))
    await waitFor(() => expect(box).not.toBeChecked())
  })

  // Parity checks keys and typography, never prose: the order clause is the most consequential
  // sentence on the screen and could be shortened in French alone with every other test green.
  it('states the order in French too, not only the consequence', async () => {
    vi.mocked(api.getDavCredentials).mockResolvedValue(ON)
    await i18next.changeLanguage('fr')
    try {
      render(<SyncPage />)

      await userEvent.click(await screen.findByRole('button', { name: 'Régénérer' }))

      expect(screen.getByText(/Désactivez d’abord la synchronisation sur vos appareils/))
        .toBeInTheDocument()
    } finally {
      cleanup()
      await i18next.changeLanguage('en')
    }
  })
})
