import { useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { NavigateFunction } from 'react-router'
import type { TFunction } from 'i18next'
import type { AddToast } from '../../hooks/useToasts'
import { apiErrorMessage } from '../../lib/apiErrorMessage'
import { readStored, writeStored } from '../../lib/safeStorage'
import type { WeekRules } from './calendarLocale'
import type { Calendar, Occurrence } from './calendarTypes'
import { formOf, newEventForm, type EventFormState } from './eventForm'
import { isPlainDate, type PlainDate } from './plainDate'
import { useEvent, useWindow, type useSearch } from './queries'
import { windowOf } from './windowOf'

const LAST_USED_KEY = 'calendar.lastUsed'
const HOUR_MS = 3_600_000

/** The calendar the last save chose, so the next new event opens on it rather than on the one the
    server calls default — the device's memory, like the stored view. */
function storedCalendar(): string | null {
  return readStored(LAST_USED_KEY)
}

export function rememberCalendar(id: string) {
  writeStored(LAST_USED_KEY, id)
}

/** The day a RECURRENCE-ID falls on, in the two shapes iCalendar writes one: `2026-09-16T09:00:00`
    and `20260916T090000Z`. `null` for anything else — a day that cannot be read is a window that
    must not be asked for. */
export function dayOfInstance(instance: string): PlainDate | null {
  if (isPlainDate(instance.slice(0, 10))) return instance.slice(0, 10)
  const basic = /^(\d{4})(\d{2})(\d{2})/.exec(instance)
  return basic ? `${basic[1]}-${basic[2]}-${basic[3]}` : null
}

/** The top of the next hour: what "New event" means when nothing on the grid named a slot. */
function nextHour(): Date {
  const now = new Date()
  now.setMinutes(0, 0, 0)
  return new Date(now.getTime() + HOUR_MS)
}

/** `null` for anything `Date` cannot parse, rather than an Invalid Date travelling further into
    the form and throwing the first time something reads it (`toISOString`, `getHours`, …). A
    hand-edited or stale `start`/`end` query param is the case this exists for. */
function parseDraftDate(value: string | null): Date | null {
  if (!value) return null
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? null : date
}

export function eventErrorOf(error: unknown, t: TFunction<'calendar'>): string {
  return (error as { status?: number }).status === 404 ? t('errors.notFound')
    : apiErrorMessage(error, t('errors.load'))
}

/** What the open form was sown from, and the version it claims to have read. */
export interface Seed { key: string; form: EventFormState; hash: string }

interface EditorSeedInput {
  params: URLSearchParams
  routeId: string | null
  instanceParam: string
  inEditor: boolean
  reloads: number
  anchor: PlainDate
  tz: string
  rules: WeekRules
  windowQuery: ReturnType<typeof useWindow>
  searchQuery: ReturnType<typeof useSearch>
  calendarsLoaded: boolean
  calendars: Calendar[]
  calendarById: Map<string, Calendar>
  addToast: AddToast
  navigate: NavigateFunction
}

export function useEditorSeed({
  params, routeId, instanceParam, inEditor, reloads, anchor, tz, rules, windowQuery, searchQuery,
  calendarsLoaded, calendars, calendarById, addToast, navigate,
}: EditorSeedInput) {
  const { t } = useTranslation('calendar')
  const eventQuery = useEvent(routeId)
  const detail = eventQuery.data ?? null

  // The window, then the search results, then one day fetched around the instance. Without the
  // last two, a search result seeded the master's hours and a narrow save moved the occurrence.
  const find = useCallback((list: Occurrence[] | undefined) => (routeId
    ? list?.find(one => one.eventId === routeId && one.instanceId === instanceParam) ?? null
    : null), [routeId, instanceParam])

  const fromWindow = find(windowQuery.data)
  const fromSearch = find(searchQuery.data)
  const instanceDay = instanceParam ? dayOfInstance(instanceParam) : null
  const windowSettled = windowQuery.data !== undefined || windowQuery.isError
  const needDay = routeId != null && instanceDay != null && windowSettled
    && fromWindow === null && fromSearch === null
  const dayWindow = useMemo(
    () => windowOf('day', instanceDay ?? anchor, tz, rules), [instanceDay, anchor, tz, rules])
  // No placeholder here: a second instance opened on another day would read the previous day's
  // list as this day's settled answer and sow the form from the master.
  const dayQuery = useWindow(dayWindow, tz, { enabled: needDay, keepPrevious: false })
  const daySettled = dayQuery.data !== undefined || dayQuery.isError

  const occurrence = fromWindow ?? fromSearch ?? find(dayQuery.data)
  // Everything that could still bring it has answered — the editor may seed.
  const occurrenceFound = instanceParam === '' || occurrence !== null
    || (windowSettled && (!needDay || daySettled))

  const defaultCalendarId = () => {
    const stored = storedCalendar()
    if (stored && calendarById.has(stored)) return stored
    return calendars.find(one => one.isDefault)?.id ?? calendars[0]?.id ?? ''
  }

  // The slot the grid named, or the next hour when the sidebar's button was the door.
  const newDraft = (): EventFormState => {
    const start = parseDraftDate(params.get('start')) ?? nextHour()
    const parsedEnd = parseDraftDate(params.get('end'))
    // An end that does not follow start — missing, unparsable, or from a URL whose start fell
    // back to a different instant — is not a duration worth keeping.
    const end = parsedEnd && parsedEnd.getTime() > start.getTime()
      ? parsedEnd : new Date(start.getTime() + HOUR_MS)
    return newEventForm(start, end, params.get('allDay') === '1', defaultCalendarId(), tz)
  }

  // The editor takes its values once, at the render it mounts on: a refetch landing behind an open
  // form must not reseed what is being typed. The key is what makes it seed again — `reloads` is
  // the deliberate lever, pulled by the Reload button a stale write puts on screen.
  const editorKey = inEditor ? `${routeId ?? 'new'}#${instanceParam}#${reloads}` : null
  const [seed, setSeed] = useState<Seed | null>(null)
  // A seed belongs to the editor it was sown for and dies with it. Kept past the close, the next
  // creation found it under the same `new##0` key and reused it: a click on the grid put its slot
  // in the URL and opened the draft of the last "New event" — the next hour of the clock.
  if (!editorKey && seed) setSeed(null)
  // Latched on the seed: a refetch without the edited instance flipped this and threw the draft
  // away. A stale detail being re-read (a save's own invalidation) or a failed read is never sown
  // from: its hash or its event is gone (docs/architecture-calendar.md).
  const detailCurrent = detail != null && !eventQuery.isError
    && !(eventQuery.isStale && eventQuery.fetchStatus !== 'idle')
  const editorReady = seed?.key === editorKey
    || ((routeId ? detailCurrent : calendarsLoaded) && occurrenceFound)
  if (editorKey && editorReady && seed?.key !== editorKey) {
    setSeed({
      key: editorKey,
      form: detail ? formOf(detail, occurrence, tz) : newDraft(),
      // Frozen here, never read live at save time: a refused write would otherwise hand the retry
      // the very version that refused it — a claim to have read what the user never saw.
      hash: detail?.icsHash ?? '',
    })
  }

  // An id the server no longer resolves is an obsolete bookmark, never an invitation to create.
  // Decided on the seed, not on the cache: before the form is sown a failed read is a target gone,
  // after it the form keeps the event it already read.
  const eventError = eventQuery.isError && eventQuery.fetchStatus === 'idle' && seed?.key !== editorKey
    ? eventQuery.error : null
  useEffect(() => {
    if (!eventError) return
    addToast(eventErrorOf(eventError, t), 'error')
    void navigate('/calendar', { replace: true })
  }, [eventError, addToast, navigate, t])

  return { eventQuery, detail, occurrence, editorKey, seed, editorReady }
}
