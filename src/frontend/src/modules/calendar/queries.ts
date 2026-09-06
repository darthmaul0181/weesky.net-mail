import {
  keepPreviousData, useMutation, useQuery, useQueryClient, type QueryClient,
} from '@tanstack/react-query'
import { api, ApiError } from '../../api.js'
import { useAccountId } from '../../hooks/useAccountId'
import type {
  Calendar, CalendarImportOutcome, CalendarImportReport, CalendarListResponse, CalendarWrite,
  EditScope, EventDetail, EventUpdateBody, EventWrite, Occurrence, OccurrenceListResponse,
} from './calendarTypes'
import type { Window } from './windowOf'

/** Scoped by account like the mail and contacts keys: a second mailbox keeps its own calendars
    rather than reading the first one's out of the cache. */
export const calendarKeys = {
  all: (accountId: string) => ['calendar', accountId] as const,
  calendars: (accountId: string) => ['calendar', accountId, 'calendars'] as const,
  /** The zone is part of the key: it decides which day a floating instance falls on, so the same
      bounds read in another zone are a different answer, not the same one. */
  window: (accountId: string, from: string, to: string, tz: string) =>
    ['calendar', accountId, 'window', from, to, tz] as const,
  event: (accountId: string, id: string) => ['calendar', accountId, 'event', id] as const,
  search: (accountId: string, q: string) => ['calendar', accountId, 'search', q] as const,
}

/** A save refused because the event moved since the editor read it — the one failure the editor
    answers with a dialog rather than a toast. */
export function isConflict(error: unknown): boolean {
  return error instanceof ApiError && error.status === 409
}

export function useCalendars(tz: string) {
  const accountId = useAccountId()

  return useQuery({
    queryKey: calendarKeys.calendars(accountId),
    queryFn: () => api.getCalendars(tz) as Promise<CalendarListResponse>,
    staleTime: 5 * 60_000,
    select: (data): Calendar[] => data.calendars,
  })
}

/** One screenful of occurrences. `keepPreviousData` is what stops the grid blanking between two
    weeks: the previous one stays drawn, greyed by `isPlaceholderData`, until the next answers. */
/** `keepPrevious` is the grid's own behaviour and nobody else's: a caller that reads the answer to
    decide something — which occurrence is being edited — must not be handed the previous window's
    list as though it were this one's. */
export function useWindow(
  window: Window, tz: string, options: { enabled?: boolean; keepPrevious?: boolean } = {},
) {
  const accountId = useAccountId()

  return useQuery({
    queryKey: calendarKeys.window(accountId, window.from, window.to, tz),
    queryFn: () => api.getOccurrences(window.from, window.to, tz) as
      Promise<OccurrenceListResponse>,
    enabled: options.enabled ?? true,
    staleTime: 60_000,
    placeholderData: options.keepPrevious === false ? undefined : keepPreviousData,
    select: (data): Occurrence[] => data.occurrences,
  })
}

export function useEvent(id: string | null) {
  const accountId = useAccountId()

  return useQuery({
    queryKey: calendarKeys.event(accountId, id ?? ''),
    queryFn: () => api.getEvent(id) as Promise<EventDetail>,
    enabled: id != null,
    staleTime: 60_000,
  })
}

/** A search answers one occurrence per matching event — its next one, or its last once it is
    over — so the list is not a window and carries none of a window's bounds. */
export function useSearch(q: string) {
  const accountId = useAccountId()
  const query = q.trim()

  return useQuery({
    queryKey: calendarKeys.search(accountId, query),
    queryFn: () => api.searchEvents(query) as Promise<OccurrenceListResponse>,
    enabled: query.length > 0,
    staleTime: 60_000,
    select: (data): Occurrence[] => data.occurrences,
  })
}

// Settled, not success: a refused write must leave the screen on server state rather than on an
// optimistic lie. One root key, so a single invalidation reaches the grid, the sidebar, the open
// event and any search at once — every one of them can be changed by any one of these writes.
function useCalendarMutation<TArgs, TResult = unknown>(
  mutationFn: (args: TArgs) => Promise<TResult>,
) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn,
    onSettled: () => invalidateAll(queryClient, accountId),
  })
}

function invalidateAll(queryClient: QueryClient, accountId: string) {
  queryClient.invalidateQueries({ queryKey: calendarKeys.all(accountId) })
}

export function useCreateEvent() {
  return useCalendarMutation((event: EventWrite) => api.createEvent(event) as Promise<EventDetail>)
}

export function useUpdateEvent() {
  return useCalendarMutation(
    ({ id, body }: { id: string; body: EventUpdateBody }) => api.updateEvent(id, body))
}

export function useDeleteEvent() {
  return useCalendarMutation(
    ({ id, scope, instanceId }: { id: string; scope: EditScope; instanceId?: string }) =>
      api.deleteEvent(id, scope, instanceId))
}

export function useCreateCalendar() {
  return useCalendarMutation(
    ({ calendar, tz }: { calendar: CalendarWrite; tz: string }) =>
      api.createCalendar(calendar, tz) as Promise<Calendar>)
}

export function useUpdateCalendar() {
  return useCalendarMutation(
    ({ id, calendar }: { id: string; calendar: CalendarWrite }) =>
      api.updateCalendar(id, calendar))
}

export function useSetCalendarVisible() {
  return useCalendarMutation(
    ({ id, visible }: { id: string; visible: boolean }) => api.setCalendarVisible(id, visible))
}

export function useDeleteCalendar() {
  return useCalendarMutation((id: string) => api.deleteCalendar(id))
}

export function useImportCalendar() {
  return useCalendarMutation(({ id, file }: { id: string; file: File }) =>
    api.importCalendar(id, file) as Promise<CalendarImportReport>)
}

export function useImportCalendarAsNew() {
  return useCalendarMutation(
    ({ file, displayName, color, tz }:
      { file: File; displayName: string; color: string; tz: string }) =>
      api.importCalendarAsNew(file, displayName, color, tz) as Promise<CalendarImportOutcome>)
}

interface MoveVariables {
  id: string
  body: EventUpdateBody
  /** The occurrence as the drop left it, spliced into the window cache before the round trip. */
  moved: Occurrence
}

/** A drag or a resize, applied to the cache first: the block has to stay where the finger left
    it, a round trip of its own snapping it back to its old slot for the width of the request,
    which reads as the drop having failed. */
export function useMoveOccurrence(window: Window, tz: string) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()
  const key = calendarKeys.window(accountId, window.from, window.to, tz)

  return useMutation({
    mutationFn: ({ id, body }: MoveVariables) => api.updateEvent(id, body),
    onMutate: async ({ moved }: MoveVariables) => {
      // Without this, a refetch already in flight lands after the patch and undoes it.
      await queryClient.cancelQueries({ queryKey: key })
      const previous = queryClient.getQueryData<OccurrenceListResponse>(key)

      queryClient.setQueryData<OccurrenceListResponse>(key, current => current && {
        ...current,
        occurrences: current.occurrences.map(o =>
          o.eventId === moved.eventId && o.instanceId === moved.instanceId ? moved : o),
      })
      return { previous }
    },
    onError: (_error, _variables, context) => {
      if (context?.previous) queryClient.setQueryData(key, context.previous)
    },
    onSettled: () => invalidateAll(queryClient, accountId),
  })
}
