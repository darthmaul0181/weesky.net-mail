import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import {
  BLOCK_SIZE, PREFERENCE_KEYS, alwaysShowImagesOf, captureRecipientsOf, composeFormatOf, isStreaming,
  notifiesOf, notifyDesktopOf, notifySoundOf, readingPaneOf, requestSizeOf, showPreviewOf,
  trustContactsOf, usePreferences, useSetPreference,
} from './usePreferences'

const mocks = vi.hoisted(() => ({ getPreferences: vi.fn(), setPreference: vi.fn() }))
vi.mock('../api.js', () => ({ api: mocks }))

let client: QueryClient
function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

describe('usePreferences', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  })

  it('reads the map the backend answers', async () => {
    mocks.getPreferences.mockResolvedValue({ [PREFERENCE_KEYS.pageSize]: '50' })

    const { result } = renderHook(() => usePreferences(), { wrapper })

    await waitFor(() => expect(result.current.data).toBeDefined())
    expect(requestSizeOf(result.current.data!)).toBe(50)
  })

  // Every cached message page was computed under the old size, so it is not merely stale — it
  // is the wrong shape. Both caches have to go.
  it('drops the cached message pages when a preference changes', async () => {
    mocks.setPreference.mockResolvedValue(undefined)
    const invalidate = vi.spyOn(client, 'invalidateQueries')

    const { result } = renderHook(() => useSetPreference(), { wrapper })
    result.current.mutate({ key: PREFERENCE_KEYS.pageSize, value: '10' })

    await waitFor(() => expect(mocks.setPreference).toHaveBeenCalledWith(PREFERENCE_KEYS.pageSize, '10'))
    await waitFor(() =>
      expect(invalidate).toHaveBeenCalledWith({ queryKey: ['mail'] }))
  })

  it('leaves the caches alone when the write fails', async () => {
    mocks.setPreference.mockRejectedValue(new Error('nope'))
    const invalidate = vi.spyOn(client, 'invalidateQueries')

    const { result } = renderHook(() => useSetPreference(), { wrapper })
    result.current.mutate({ key: PREFERENCE_KEYS.pageSize, value: '10' })

    await waitFor(() => expect(result.current.isError).toBe(true))
    expect(invalidate).not.toHaveBeenCalled()
  })
})

describe('the accessors', () => {
  it('reads a numeric page size as a number', () => {
    expect(requestSizeOf({ [PREFERENCE_KEYS.pageSize]: '100' })).toBe(100)
  })

  // "all" is a string in a field every other value fills with a number. Number('all') is NaN,
  // and NaN throws nothing: it would travel into the request URL and surface as an empty list.
  it('reads "all" as the block size, never NaN', () => {
    const preferences = { [PREFERENCE_KEYS.pageSize]: 'all' }

    expect(requestSizeOf(preferences)).toBe(BLOCK_SIZE)
    expect(Number.isNaN(requestSizeOf(preferences))).toBe(false)
  })

  it.each([
    ['all', true],
    ['30', false],
    ['100', false],
  ])('reads streaming for %s as %s', (stored, expected) => {
    expect(isStreaming({ [PREFERENCE_KEYS.pageSize]: stored })).toBe(expected)
  })

  // Anything but the explicit "false" means shown: a key the backend has not sent yet must not
  // silently hide the preview.
  it.each([
    ['true', true],
    ['false', false],
    [undefined, true],
  ])('reads showPreview %s as %s', (stored, expected) => {
    const preferences: Record<string, string> =
      stored === undefined ? {} : { [PREFERENCE_KEYS.showPreview]: stored }

    expect(showPreviewOf(preferences)).toBe(expected)
  })

  // The mirror of showPreviewOf: a key the backend has not sent yet must leave the app silent,
  // never guess that the user wanted noise.
  it.each([
    ['true', true],
    ['false', false],
    [undefined, false],
  ])('reads notifySound %s as %s', (stored, expected) => {
    const preferences: Record<string, string> =
      stored === undefined ? {} : { [PREFERENCE_KEYS.notifySound]: stored }

    expect(notifySoundOf(preferences)).toBe(expected)
  })

  it.each([
    ['true', true],
    ['false', false],
    [undefined, false],
  ])('reads notifyDesktop %s as %s', (stored, expected) => {
    const preferences: Record<string, string> =
      stored === undefined ? {} : { [PREFERENCE_KEYS.notifyDesktop]: stored }

    expect(notifyDesktopOf(preferences)).toBe(expected)
  })

  // The mirror of notifySoundOf: an absent or malformed row must keep blocking, never reveal.
  it.each([
    ['true', true],
    ['false', false],
    ['yes', false],
    [undefined, false],
  ])('reads alwaysShowImages %s as %s', (stored, expected) => {
    const preferences: Record<string, string> =
      stored === undefined ? {} : { [PREFERENCE_KEYS.alwaysShowImages]: stored }

    expect(alwaysShowImagesOf(preferences)).toBe(expected)
  })

  // The background poll and the notification hook both ask this question; derived twice they
  // could disagree about who pays for the poll.
  it.each([
    ['false', 'false', false],
    ['true', 'false', true],
    ['false', 'true', true],
    ['true', 'true', true],
  ])('reads notifies for sound %s and desktop %s as %s', (sound, desktop, expected) => {
    expect(notifiesOf({
      [PREFERENCE_KEYS.notifySound]: sound, [PREFERENCE_KEYS.notifyDesktop]: desktop,
    })).toBe(expected)
  })

  it('reads notifies as false for an empty map', () => {
    expect(notifiesOf({})).toBe(false)
  })

  // The backend registry only serves the three values, but this layer must not trust that:
  // an older API answers nothing for the key, and nothing must mean today's layout.
  it.each([
    ['right', 'right'],
    ['bottom', 'bottom'],
    ['none', 'none'],
    ['sideways', 'right'],
    [undefined, 'right'],
  ])('reads readingPane %s as %s', (stored, expected) => {
    const preferences: Record<string, string> =
      stored === undefined ? {} : { [PREFERENCE_KEYS.readingPane]: stored }

    expect(readingPaneOf(preferences)).toBe(expected)
  })
})

describe('captureRecipientsOf', () => {
  // On unless explicitly off, like showPreviewOf: the default is true, so an account whose row
  // has never been written must capture.
  it('is on by default and off only for an explicit false', () => {
    expect(captureRecipientsOf({})).toBe(true)
    expect(captureRecipientsOf({ 'contacts.captureRecipients': 'true' })).toBe(true)
    expect(captureRecipientsOf({ 'contacts.captureRecipients': 'false' })).toBe(false)
  })
})

describe('trustContactsOf', () => {
  // Off unless explicitly on, like alwaysShowImagesOf: a key the backend has not sent yet must
  // not load remote images.
  it('is off by default and on only for an explicit true', () => {
    expect(trustContactsOf({})).toBe(false)
    expect(trustContactsOf({ 'mail.trustContacts': 'true' })).toBe(true)
    expect(trustContactsOf({ 'mail.trustContacts': 'garbage' })).toBe(false)
  })
})

describe('composeFormatOf', () => {
  it('reads the stored editor', () => {
    expect(composeFormatOf({ 'mail.composeFormat': 'text' })).toBe('text')
    expect(composeFormatOf({ 'mail.composeFormat': 'html' })).toBe('html')
  })

  // An older backend does not send the key at all, and today's composer is the HTML one.
  it('falls back to html on an absent or unrecognised value', () => {
    expect(composeFormatOf({})).toBe('html')
    expect(composeFormatOf({ 'mail.composeFormat': 'plain' })).toBe('html')
  })
})
