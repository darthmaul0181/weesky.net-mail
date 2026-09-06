import { beforeEach, describe, expect, it, vi } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import type { Occurrence, OccurrenceListResponse } from './calendarTypes'
import {
  calendarKeys, isConflict, useCalendars, useCreateEvent, useMoveOccurrence, useSearch,
  useSetCalendarVisible, useWindow,
} from './queries'
import type { Window } from './windowOf'

// The class lives in the hoisted block with the mocks: `vi.mock`'s factory runs before any
// module-level declaration of the test file itself.
const { mocks, FakeApiError } = vi.hoisted(() => {
  class FakeApiError extends Error {
    status: number
    constructor(message: string, status: number) {
      super(message)
      this.status = status
    }
  }
  return {
    FakeApiError,
    mocks: {
      getCalendars: vi.fn(), getOccurrences: vi.fn(), getEvent: vi.fn(), searchEvents: vi.fn(),
      createEvent: vi.fn(), updateEvent: vi.fn(), deleteEvent: vi.fn(),
      setCalendarVisible: vi.fn(),
    },
  }
})
vi.mock('../../api.js', () => ({ api: mocks, ApiError: FakeApiError }))
vi.mock('../../hooks/useAccountId', () => ({ useAccountId: () => 'primary' }))

const TZ = 'Europe/Brussels'
const WINDOW: Window = {
  from: '2026-09-12T22:00:00.000Z', to: '2026-09-21T22:00:00.000Z',
  firstVisible: '2026-09-14', lastVisible: '2026-09-20',
}

function occurrence(overrides: Partial<Occurrence> = {}): Occurrence {
  return {
    eventId: 'e1', calendarId: 'c1', uid: 'u1', instanceId: '20260914T080000', isOverride: false,
    isAllDay: false, isFloating: false, timeZone: TZ,
    startUtc: '2026-09-14T06:00:00Z', endUtc: '2026-09-14T07:00:00Z',
    transparency: 'OPAQUE', hasAlarm: false, ...overrides,
  }
}

let client: QueryClient
function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

function windowKey() {
  return calendarKeys.window('primary', WINDOW.from, WINDOW.to, TZ)
}

function cached(): OccurrenceListResponse | undefined {
  return client.getQueryData(windowKey())
}

describe('calendarKeys', () => {
  // One invalidation of `all` has to reach every calendar query, so each key extends it.
  it('nests every key under the account root', () => {
    const root = calendarKeys.all('primary')

    for (const key of [
      calendarKeys.calendars('primary'),
      calendarKeys.window('primary', WINDOW.from, WINDOW.to, TZ),
      calendarKeys.event('primary', 'e1'),
      calendarKeys.search('primary', 'dentist'),
    ]) {
      expect(key.slice(0, root.length)).toEqual([...root])
    }
  })

  it('gives each window its own entry, zone included', () => {
    expect(calendarKeys.window('primary', WINDOW.from, WINDOW.to, TZ))
      .not.toEqual(calendarKeys.window('primary', WINDOW.from, WINDOW.to, 'UTC'))
  })
})

describe('the queries', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  })

  it('asks for the calendars in the screen zone and answers the list', async () => {
    mocks.getCalendars.mockResolvedValue({ calendars: [{ id: 'c1', displayName: 'Home' }] })

    const { result } = renderHook(() => useCalendars(TZ), { wrapper })

    await waitFor(() => expect(result.current.data).toHaveLength(1))
    expect(mocks.getCalendars).toHaveBeenCalledWith(TZ)
  })

  it('asks for the window bounds it was given and answers the occurrences', async () => {
    mocks.getOccurrences.mockResolvedValue({ occurrences: [occurrence()] })

    const { result } = renderHook(() => useWindow(WINDOW, TZ), { wrapper })

    await waitFor(() => expect(result.current.data).toHaveLength(1))
    expect(mocks.getOccurrences).toHaveBeenCalledWith(WINDOW.from, WINDOW.to, TZ)
  })

  it('leaves the search alone until there is something to search for', async () => {
    const { result } = renderHook(() => useSearch('  '), { wrapper })

    await waitFor(() => expect(result.current.fetchStatus).toBe('idle'))
    expect(mocks.searchEvents).not.toHaveBeenCalled()
  })
})

describe('the mutations', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  })

  it('resyncs the screen once a write lands', async () => {
    mocks.createEvent.mockResolvedValue({ id: 'e2' })
    const invalidate = vi.spyOn(client, 'invalidateQueries')

    const { result } = renderHook(() => useCreateEvent(), { wrapper })
    result.current.mutate({
      calendarId: 'c1', isAllDay: false, reminderMinutesBefore: [],
      availability: 'Busy', visibility: 'Default',
    })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(invalidate).toHaveBeenCalledWith({ queryKey: calendarKeys.all('primary') })
  })

  // Settled, never success: a refused write must leave the screen on server state.
  it('resyncs the screen when a write is refused too', async () => {
    mocks.setCalendarVisible.mockRejectedValue(new Error('nope'))
    const invalidate = vi.spyOn(client, 'invalidateQueries')

    const { result } = renderHook(() => useSetCalendarVisible(), { wrapper })
    result.current.mutate({ id: 'c1', visible: false })

    await waitFor(() => expect(result.current.isError).toBe(true))
    expect(invalidate).toHaveBeenCalledWith({ queryKey: calendarKeys.all('primary') })
  })
})

describe('useMoveOccurrence', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    client.setQueryData(windowKey(), {
      occurrences: [
        occurrence(),
        occurrence({ eventId: 'e2', instanceId: '', startUtc: '2026-09-15T06:00:00Z' }),
      ],
    })
  })

  const moved = occurrence({ startUtc: '2026-09-14T07:30:00Z', endUtc: '2026-09-14T08:30:00Z' })
  const body = {
    calendarId: 'c1', isAllDay: false, reminderMinutesBefore: [], availability: 'Busy' as const,
    visibility: 'Default' as const, scope: 'This' as const, ifHash: 'hash-1',
  }

  // The grid must follow the finger, not the round trip: the dropped block sits where it landed
  // before the API has answered.
  it('puts the occurrence where it was dropped before the answer comes back', async () => {
    let settle = () => {}
    mocks.updateEvent.mockReturnValue(new Promise<void>(resolve => { settle = () => resolve() }))

    const { result } = renderHook(() => useMoveOccurrence(WINDOW, TZ), { wrapper })
    result.current.mutate({ id: 'e1', body, moved })

    await waitFor(() => expect(cached()?.occurrences[0].startUtc).toBe('2026-09-14T07:30:00Z'))
    expect(cached()?.occurrences).toHaveLength(2)
    settle()
    await waitFor(() => expect(result.current.isSuccess).toBe(true))
  })

  it('puts it back where it was when the API refuses the move', async () => {
    mocks.updateEvent.mockRejectedValue(new FakeApiError('gone', 409))

    const { result } = renderHook(() => useMoveOccurrence(WINDOW, TZ), { wrapper })
    result.current.mutate({ id: 'e1', body, moved })

    await waitFor(() => expect(result.current.isError).toBe(true))
    expect(cached()?.occurrences[0].startUtc).toBe('2026-09-14T06:00:00Z')
  })

  it('resyncs the screen whichever way the move went', async () => {
    mocks.updateEvent.mockResolvedValue(undefined)
    const invalidate = vi.spyOn(client, 'invalidateQueries')

    const { result } = renderHook(() => useMoveOccurrence(WINDOW, TZ), { wrapper })
    result.current.mutate({ id: 'e1', body, moved })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(invalidate).toHaveBeenCalledWith({ queryKey: calendarKeys.all('primary') })
  })
})

describe('isConflict', () => {
  // The editor's own answer to a 409 is a dialog, so the status has to be told from the prose.
  it('recognises the version clash and nothing else', () => {
    expect(isConflict(new FakeApiError('changed', 409))).toBe(true)
    expect(isConflict(new FakeApiError('bad', 400))).toBe(false)
    expect(isConflict(new Error('409'))).toBe(false)
    expect(isConflict(null)).toBe(false)
  })
})
