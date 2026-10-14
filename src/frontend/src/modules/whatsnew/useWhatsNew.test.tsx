import { describe, expect, it, vi, beforeEach } from 'vitest'
import { act, renderHook, waitFor } from '@testing-library/react'
import { createTestQueryClient, settle, withQueryClient } from '../../test-utils'
import { WEB_VERSION } from '../../lib/appVersion'
import { notifySessionEnd } from '../../contexts/sessionEvents'
import type { WhatsNewState } from './types'
import { useMarkWhatsNew, useWhatsNew, useWhatsNewSync } from './useWhatsNew'

const mocks = vi.hoisted(() => ({
  getWhatsNew: vi.fn(), markWhatsNew: vi.fn(), setWhatsNewBaseline: vi.fn(), getAppSettings: vi.fn(),
}))
vi.mock('../../api.js', () => ({ api: mocks }))

beforeEach(() => {
  vi.clearAllMocks()
  mocks.getAppSettings.mockResolvedValue({ 'app.dailyImage': 'true' })
  mocks.markWhatsNew.mockResolvedValue(null)
  mocks.setWhatsNewBaseline.mockResolvedValue(null)
})

describe('useWhatsNewSync', () => {
  it('writes the baseline of a new account once, and nothing else', async () => {
    mocks.getWhatsNew.mockResolvedValue({ items: [] })
    renderHook(() => useWhatsNewSync(), { wrapper: withQueryClient(createTestQueryClient()) })
    await waitFor(() => expect(mocks.setWhatsNewBaseline).toHaveBeenCalledWith(WEB_VERSION))
    expect(mocks.setWhatsNewBaseline).toHaveBeenCalledTimes(1)
    expect(mocks.markWhatsNew).not.toHaveBeenCalled()
  })

  it('reports the novelties whose dot shows for the first time', async () => {
    // The refetch answers as the server would, stamped: otherwise each answer would ask again.
    mocks.getWhatsNew.mockResolvedValueOnce({ baseline: '0.0.0', items: [] })
      .mockResolvedValue({ baseline: '0.0.0', items: [{ id: 'daily-image', firstShownAt: '2026-10-14T00:00:00Z' }] })
    renderHook(() => useWhatsNewSync(), { wrapper: withQueryClient(createTestQueryClient()) })
    await waitFor(() => expect(mocks.markWhatsNew).toHaveBeenCalledWith(['daily-image'], 'shown'))
    await waitFor(() => expect(mocks.getWhatsNew).toHaveBeenCalledTimes(2))
    expect(mocks.markWhatsNew).toHaveBeenCalledTimes(1)
    expect(mocks.setWhatsNewBaseline).not.toHaveBeenCalled()
  })

  it('reports a novelty once per session, even when the server never stamps it', async () => {
    // Later than the optimistic stamp's render, as a network answer is: an instant one hides the loop.
    mocks.markWhatsNew.mockImplementation(() => new Promise((_, reject) => setTimeout(() => reject(new Error('offline')), 5)))
    mocks.getWhatsNew.mockResolvedValue({ baseline: '0.0.0', items: [] })
    renderHook(() => useWhatsNewSync(), { wrapper: withQueryClient(createTestQueryClient()) })
    await waitFor(() => expect(mocks.getWhatsNew).toHaveBeenCalledTimes(2))
    await act(() => new Promise(resolve => setTimeout(resolve, 100)))
    expect(mocks.markWhatsNew).toHaveBeenCalledTimes(1)
    expect(mocks.getWhatsNew).toHaveBeenCalledTimes(2)
  })

  it('does not signal a novelty whose feature the administrator switched off', async () => {
    mocks.getAppSettings.mockResolvedValue({ 'app.dailyImage': 'false' })
    mocks.getWhatsNew.mockResolvedValue({ baseline: '0.0.0', items: [] })
    const client = createTestQueryClient()
    renderHook(() => useWhatsNewSync(), { wrapper: withQueryClient(client) })
    await waitFor(() => expect(client.getQueryData(['whatsNew'])).toBeDefined())
    expect(mocks.markWhatsNew).not.toHaveBeenCalled()
  })
})

function renderWhatsNew(state: WhatsNewState) {
  const client = createTestQueryClient()
  client.setQueryData(['appSettings'], { 'app.dailyImage': 'true' })
  client.setQueryData(['whatsNew'], state)
  const view = renderHook(() => ({ ...useWhatsNew(), mark: useMarkWhatsNew() }), { wrapper: withQueryClient(client) })
  return { client, view }
}

describe('useWhatsNew', () => {
  it('lights the anchors on the way to an active novelty and frames its target', () => {
    const { result } = renderWhatsNew({ baseline: '0.0.0', items: [] }).view
    expect([result.current.lights('rail:settings'), result.current.lights('settings:appearance')]).toEqual([true, true])
    expect(result.current.lights('rail:mail')).toBe(false)
    expect(result.current.targetOf('appearance:daily-image')?.entry.id).toBe('daily-image')
    expect(result.current.targetOf('rail:settings')).toBeUndefined()
  })

  it('marks the unlisted novelties listed', async () => {
    const { result } = renderWhatsNew({ baseline: '0.0.0', items: [] }).view
    expect(result.current.unlistedCount).toBe(1)
    act(() => result.current.markListed())
    await waitFor(() => expect(mocks.markWhatsNew).toHaveBeenCalledWith(['daily-image'], 'listed'))
  })

  it('marks listed only the ids it is handed, among the unlisted ones', async () => {
    const { result } = renderWhatsNew({ baseline: '0.0.0', items: [] }).view
    act(() => result.current.markListed(['other']))
    await settle()
    expect(mocks.markWhatsNew).not.toHaveBeenCalled()
    act(() => result.current.markListed(['daily-image', 'other']))
    await waitFor(() => expect(mocks.markWhatsNew).toHaveBeenCalledWith(['daily-image'], 'listed'))
  })

  // The server takes 1 to 50 ids per batch: a longer mark goes out in several.
  it('sends a long mark in batches of fifty', async () => {
    const { result } = renderWhatsNew({ baseline: '0.0.0', items: [] }).view
    const ids = Array.from({ length: 120 }, (_, i) => `n${i}`)
    act(() => result.current.mark(ids, 'shown'))
    await waitFor(() => expect(mocks.markWhatsNew).toHaveBeenCalledTimes(3))
    expect(mocks.markWhatsNew.mock.calls.map(([batch]) => (batch as string[]).length)).toEqual([50, 50, 20])
    expect(mocks.markWhatsNew.mock.calls.flatMap(([batch]) => batch as string[])).toEqual(ids)
  })

  // A refused mark leaves the novelty unstamped, so every caller would ask again on each refetch.
  it('sends one mark per novelty once per session, and again in the next', async () => {
    const { result } = renderWhatsNew({ baseline: '0.0.0', items: [] }).view
    act(() => result.current.mark(['daily-image'], 'seen'))
    act(() => result.current.mark(['daily-image', 'other'], 'seen'))
    act(() => result.current.mark(['daily-image'], 'listed'))
    await waitFor(() => expect(mocks.markWhatsNew).toHaveBeenCalledTimes(3))
    expect(mocks.markWhatsNew.mock.calls).toEqual([[['daily-image'], 'seen'], [['other'], 'seen'], [['daily-image'], 'listed']])
    notifySessionEnd()
    act(() => result.current.mark(['daily-image'], 'seen'))
    await waitFor(() => expect(mocks.markWhatsNew).toHaveBeenCalledTimes(4))
  })

  it('sends nothing when the list showed everything already', async () => {
    const { result } = renderWhatsNew({ baseline: '0.0.0', items: [{ id: 'daily-image', listedAt: '2026-10-14T00:00:00Z' }] }).view
    expect(result.current.unlistedCount).toBe(0)
    act(() => result.current.markListed())
    await settle()
    expect(mocks.markWhatsNew).not.toHaveBeenCalled()
  })

  // An earlier answer refetched while a later write is in flight would bring a dot back for a moment.
  it('refetches once the last of overlapping marks settles', async () => {
    const answers: Array<() => void> = []
    mocks.markWhatsNew.mockImplementation(() => new Promise(resolve => answers.push(() => resolve(null))))
    mocks.getWhatsNew.mockResolvedValue({ baseline: '0.0.0', items: [{ id: 'daily-image', seenAt: '2026-10-14T00:00:00Z' }] })
    const { client, view } = renderWhatsNew({ baseline: '0.0.0', items: [] })
    act(() => view.result.current.mark(['daily-image'], 'shown'))
    act(() => view.result.current.mark(['daily-image'], 'seen'))
    await waitFor(() => expect(answers).toHaveLength(2))
    await act(async () => answers[0]!())
    expect(mocks.getWhatsNew).not.toHaveBeenCalled()
    expect(view.result.current.active).toEqual([])
    await act(async () => answers[1]!())
    await waitFor(() => expect(mocks.getWhatsNew).toHaveBeenCalledTimes(1))
    expect(client.isMutating()).toBe(0)
  })
})
