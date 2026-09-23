import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import RouteError, { isChunkLoadError } from './RouteError'

function Boom(): never {
  throw new Error('boom')
}

function ChunkBoom(): never {
  throw new Error('Failed to fetch dynamically imported module: /assets/x.js')
}

// React's dev-mode error rethrow (invokeGuardedCallbackDev) surfaces as a `window` error event in
// jsdom on top of the console.error call React makes itself; preventing its default stops jsdom
// logging it a second time.
function preventLog(event: ErrorEvent) {
  event.preventDefault()
}

function renderAt(Element: () => never) {
  const router = createMemoryRouter(
    [{ path: '/', errorElement: <RouteError />, children: [{ index: true, element: <Element /> }] }],
    { initialEntries: ['/'] },
  )
  render(<RouterProvider router={router} />)
}

describe('RouteError', () => {
  let reload: ReturnType<typeof vi.fn>
  const originalLocation = Object.getOwnPropertyDescriptor(window, 'location')!

  beforeEach(() => {
    sessionStorage.clear()
    reload = vi.fn()
    Object.defineProperty(window, 'location', {
      configurable: true,
      value: { ...window.location, reload },
    })
    vi.spyOn(console, 'error').mockImplementation(() => {})
    window.addEventListener('error', preventLog)
  })

  afterEach(() => {
    window.removeEventListener('error', preventLog)
    Object.defineProperty(window, 'location', originalLocation)
    vi.restoreAllMocks()
  })

  it('renders the translated message with a Reload button that reloads on click', async () => {
    renderAt(Boom)

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Something went wrong on this page.')

    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: 'Reload' }))
    expect(reload).toHaveBeenCalledTimes(1)
  })

  it('reloads once automatically on a stale-chunk error, and not again within 10s', async () => {
    renderAt(ChunkBoom)

    expect(await screen.findAllByRole('alert')).toHaveLength(1)
    expect(reload).toHaveBeenCalledTimes(1)

    // A second mount landing moments later: proven by two alerts actually being on screen (not
    // just that `findAllByRole` matched the first render's, which was still mounted either way),
    // and that mount must still not reload again.
    renderAt(ChunkBoom)
    await waitFor(() => expect(screen.getAllByRole('alert')).toHaveLength(2))
    expect(reload).toHaveBeenCalledTimes(1)
  })

  it('reloads again once 10s have passed since the last reload', async () => {
    sessionStorage.setItem('chunkReloadAt', String(Date.now() - 10_001))
    renderAt(ChunkBoom)

    await screen.findByRole('alert')
    expect(reload).toHaveBeenCalledTimes(1)
  })

  it('does not reload when sessionStorage throws', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('blocked') })
    renderAt(ChunkBoom)

    await screen.findByRole('alert')
    expect(reload).not.toHaveBeenCalled()
  })

  it('does not auto-reload while offline, and names the reason instead', async () => {
    const originalOnLine = Object.getOwnPropertyDescriptor(window.navigator, 'onLine')
    Object.defineProperty(window.navigator, 'onLine', { configurable: true, value: false })
    try {
      renderAt(ChunkBoom)

      const alert = await screen.findByRole('alert')
      expect(alert).toHaveTextContent('You are offline. Reconnect, then reload.')
      expect(reload).not.toHaveBeenCalled()
    } finally {
      if (originalOnLine) Object.defineProperty(window.navigator, 'onLine', originalOnLine)
      else delete (window.navigator as { onLine?: boolean }).onLine
    }
  })
})

describe('isChunkLoadError', () => {
  it.each([
    ['Chrome', 'Failed to fetch dynamically imported module: /assets/x-abc123.js'],
    ['Firefox', 'error loading dynamically imported module: /assets/x-abc123.js'],
    ['Safari', 'Importing a module script failed'],
  ])('is true for %s\'s wording', (_browser, message) => {
    expect(isChunkLoadError(new Error(message))).toBe(true)
  })

  it('is false for an ordinary error', () => {
    expect(isChunkLoadError(new Error('boom'))).toBe(false)
  })

  it('is false for a non-Error thrown value', () => {
    expect(isChunkLoadError('boom')).toBe(false)
  })
})
