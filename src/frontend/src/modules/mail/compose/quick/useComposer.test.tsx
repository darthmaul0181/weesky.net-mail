import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, render } from '@testing-library/react'
import { QueryClientProvider } from '@tanstack/react-query'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { createTestQueryClient, mockViewport, resetViewport } from '../../../../test-utils'
import { registerLeaveGuard } from '../../../../lib/leaveGuard'
import { newMessageSeed } from '../composeSeed'
import { QuickComposeContext, type QuickComposeApi, type QuickComposeState } from './QuickComposeContext'
import { useComposer, type Composer } from './useComposer'

vi.mock('../../../../api.js', () => ({ api: { getPreferences: () => new Promise(() => {}) } }))

const OPEN: QuickComposeState = { seed: null, accountId: 'primary', origin: '/mail', minimised: true, instance: 1 }

function fakeQuick(state: QuickComposeState | null = null): QuickComposeApi {
  return { state, open: vi.fn(), restore: vi.fn(), close: vi.fn(), showsDraft: vi.fn(() => false) }
}

function renderComposer(
  { preference, path = '/mail', quick = fakeQuick() }:
  { preference?: string; path?: string; quick?: QuickComposeApi } = {}) {
  const client = createTestQueryClient()
  client.setQueryData(['preferences'], preference ? { 'mail.composeWindow': preference } : {})
  let composer!: Composer
  function Probe() { composer = useComposer(); return null }
  const router = createMemoryRouter([
    { path: '/mail', element: <Probe /> },
    { path: '/mail/compose', element: <Probe /> },
  ], { initialEntries: [path] })
  render(
    <QueryClientProvider client={client}>
      <QuickComposeContext.Provider value={quick}><RouterProvider router={router} /></QuickComposeContext.Provider>
    </QueryClientProvider>)
  return { router, quick, get composer() { return composer } }
}

let withdraw: (() => void) | null = null
beforeEach(() => { withdraw = null })
afterEach(() => { withdraw?.(); resetViewport() })

describe('useComposer', () => {
  it('page by default', () => {
    const view = renderComposer()
    expect(view.composer.target).toBe('page')

    const seed = newMessageSeed(['alice@x.be'])
    act(() => { view.composer.open({ seed, from: 'INBOX' }) })

    expect(view.router.state.location.pathname).toBe('/mail/compose')
    expect(view.router.state.location.state).toEqual({ from: 'INBOX', seed })
    expect(view.quick.open).not.toHaveBeenCalled()
  })

  it('window when chosen on desktop', () => {
    mockViewport('desktop')
    const view = renderComposer({ preference: 'window' })
    expect(view.composer.target).toBe('window')

    const seed = newMessageSeed(['alice@x.be'])
    act(() => { view.composer.open({ seed, from: 'Lists/A&B' }) })

    expect(view.quick.open).toHaveBeenCalledWith(seed, '/mail?folder=Lists%2FA%26B')
    expect(view.router.state.location.pathname).toBe('/mail')
  })

  it('window opens on backTo when one is given', () => {
    const view = renderComposer({ preference: 'window' })

    act(() => { view.composer.open({ seed: null, backTo: '/contacts?id=c1' }) })

    expect(view.quick.open).toHaveBeenCalledWith(null, '/contacts?id=c1')
  })

  it('phone always page', () => {
    mockViewport('phone')
    const view = renderComposer({ preference: 'window' })
    expect(view.composer.target).toBe('page')
  })

  it('page while the page composer is mounted', () => {
    const view = renderComposer({ preference: 'window', path: '/mail/compose' })
    expect(view.composer.target).toBe('page')
  })

  it('newMessage restores an existing window rather than replacing it', () => {
    const view = renderComposer({ preference: 'window', quick: fakeQuick(OPEN) })

    act(() => { view.composer.newMessage('INBOX') })

    expect(view.quick.restore).toHaveBeenCalled()
    expect(view.quick.open).not.toHaveBeenCalled()
  })

  it('newMessage opens a blank window when none exists', () => {
    const view = renderComposer({ preference: 'window' })

    act(() => { view.composer.newMessage('INBOX') })

    expect(view.quick.open).toHaveBeenCalledWith(null, '/mail?folder=INBOX')
  })

  it('prepare on the same draft restores and returns false without asking', async () => {
    const quick = fakeQuick(OPEN)
    vi.mocked(quick.showsDraft).mockReturnValue(true)
    const guard = vi.fn(() => Promise.resolve(true))
    withdraw = registerLeaveGuard(guard)
    const view = renderComposer({ preference: 'window', quick })

    const answer = await view.composer.prepare({ folderPath: 'Drafts', uid: 7 })

    expect(answer).toBe(false)
    expect(quick.restore).toHaveBeenCalled()
    expect(guard).not.toHaveBeenCalled()
  })

  it('prepare with a dirty window asks; keep editing returns false', async () => {
    const guard = vi.fn(() => Promise.resolve(false))
    withdraw = registerLeaveGuard(guard)
    const view = renderComposer({ preference: 'window', quick: fakeQuick(OPEN) })

    const answer = await view.composer.prepare({ folderPath: 'Drafts', uid: 8 })

    expect(answer).toBe(false)
    expect(guard).toHaveBeenCalledTimes(1)
  })

  it('prepare asks nothing in page target or with no window', async () => {
    const guard = vi.fn(() => Promise.resolve(false))
    withdraw = registerLeaveGuard(guard)

    expect(await renderComposer({ quick: fakeQuick(OPEN) }).composer.prepare()).toBe(true)
    expect(await renderComposer({ preference: 'window' }).composer.prepare()).toBe(true)
    expect(guard).not.toHaveBeenCalled()
  })
})
