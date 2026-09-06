import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { useMatch, useNavigate, useSearchParams } from 'react-router-dom'
import type { TFunction } from 'i18next'
import { api } from '../../api.js'
import { DeleteConfirmModal } from '../../components/DeleteConfirmModal.jsx'
import FloatingAction from '../../components/FloatingAction'
import LoadingBlock from '../../components/LoadingBlock'
import Toasts from '../../components/Toasts.jsx'
import { useAccountId } from '../../hooks/useAccountId'
import { useToasts } from '../../hooks/useToasts.js'
import { useViewport } from '../../hooks/useViewport'
import PlusIcon from '../../icons/PlusIcon'
import ContextDrawer, { useContextDrawer } from '../../layouts/ContextDrawer'
import { apiErrorMessage } from '../../lib/apiErrorMessage'
import { downloadBlob } from '../../lib/downloadBlob'
import { CALENDAR_COLORS } from './calendarColors'
import { CalendarContext, type CalendarContextValue } from './calendarContext'
import CalendarDialog, { type CalendarValues } from './CalendarDialog'
import CalendarImportReportModal from './CalendarImportReportModal'
import CalendarSidebar from './CalendarSidebar'
import CalendarToolbar, { CalendarSearch } from './CalendarToolbar'
import { dateLocaleOf, formatRangeTitle, hourCycleOf, weekNumberOf, weekRulesOf } from './calendarLocale'
import type {
  Calendar, CalendarImportReport, EditScope, EventDetail, Occurrence,
} from './calendarTypes'
import EventEditor from './EventEditor'
import EventPreview from './EventPreview'
import {
  allowedScopes, formOf, isRecurring, movedBody, movedOccurrence, newEventForm, ruleOf,
  updateBodyOf, writeOf, type EventFormState,
} from './eventForm'
import ImportDialog, { type ImportChoice } from './ImportDialog'
import MonthView from './MonthView'
import { itemsByDay, placeAll } from './multiDay'
import { colorOf, occurrenceKey } from './occurrenceStyle'
import DayStrip from './phone/DayStrip'
import PhoneMonth from './phone/PhoneMonth'
import {
  addDays, daysBetween, isPlainDate, MINUTES_PER_DAY, todayIn, type PlainDate,
} from './plainDate'
import {
  calendarKeys, isConflict, useCalendars, useCreateCalendar, useCreateEvent, useDeleteCalendar,
  useDeleteEvent, useEvent, useImportCalendar, useImportCalendarAsNew, useMoveOccurrence,
  useSearch, useSetCalendarVisible, useUpdateCalendar, useUpdateEvent, useWindow,
} from './queries'
import { recurrenceSummary } from './recurrenceSummary'
import ScopeModal, { scopeSentence } from './ScopeModal'
import SearchResults from './SearchResults'
import UpcomingList from './UpcomingList'
import WeekView from './WeekView'
import { windowOf, type View, type Window } from './windowOf'

const VIEWS: View[] = ['day', 'week', 'month', 'list']
const VIEW_KEY = 'calendar.view'
const LAST_USED_KEY = 'calendar.lastUsed'
const EVERY_SCOPE: EditScope[] = ['This', 'ThisAndFollowing', 'All']
const HOUR_MS = 3_600_000

function isView(value: string | null): value is View {
  return value !== null && (VIEWS as string[]).includes(value)
}

/** The device remembers the view, never the account: a 4K screen and a laptop want different
    ones, exactly as the splitter sizes do. A blocked store leaves the default standing. */
function storedView(): View | null {
  try {
    const stored = localStorage.getItem(VIEW_KEY)
    return isView(stored) ? stored : null
  } catch {
    return null
  }
}

function rememberView(view: View) {
  try {
    localStorage.setItem(VIEW_KEY, view)
  } catch { /* a private window refuses the write; the URL still carries the choice */ }
}

/** The calendar the last save chose, so the next new event opens on it rather than on the one the
    server calls default — the device's memory, like the view above. */
function storedCalendar(): string | null {
  try {
    return localStorage.getItem(LAST_USED_KEY)
  } catch {
    return null
  }
}

function rememberCalendar(id: string) {
  try {
    localStorage.setItem(LAST_USED_KEY, id)
  } catch { /* a private window refuses the write; the choice still stands on this event */ }
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

/** Same day of the month, clamped: 31 January plus a month is the last day of February. */
function addMonths(day: PlainDate, delta: number): PlainDate {
  const [year, month, date] = day.split('-').map(Number)
  const index = (year * 12 + month - 1) + delta
  const target = new Date(Date.UTC(Math.floor(index / 12), index % 12, 1))
  const last = new Date(Date.UTC(target.getUTCFullYear(), target.getUTCMonth() + 1, 0)).getUTCDate()
  target.setUTCDate(Math.min(date, last))
  return target.toISOString().slice(0, 10)
}

const STEP_DAYS: Record<Exclude<View, 'month'>, number> = { day: 1, week: 7, list: 30 }

/** How long the box waits before asking, and how short a question it refuses to ask at all. */
const SEARCH_DEBOUNCE_MS = 300
const SEARCH_MIN = 2

function daysOf(window: Window): PlainDate[] {
  const count = daysBetween(window.firstVisible, window.lastVisible) + 1
  return Array.from({ length: count }, (_, index) => addDays(window.firstVisible, index))
}

function stepAnchor(view: View, anchor: PlainDate, delta: number): PlainDate {
  return view === 'month' ? addMonths(anchor, delta) : addDays(anchor, STEP_DAYS[view] * delta)
}

/** The one refusal the server words itself, matched here and re-spelled from the catalogue —
    `ImportReportModal`'s road for the same reason: English prose on a French screen is worse
    than a plain sentence of our own. */
function windowErrorOf(error: unknown, t: TFunction<'calendar'>): string {
  if (error instanceof Error
    && error.message === 'The window holds too many occurrences; narrow it') {
    return t('errors.windowTooLarge')
  }
  return apiErrorMessage(error, t('errors.load'))
}

type Editing =
  | { mode: 'create' }
  | { mode: 'rename' | 'colour'; calendar: Calendar }

interface Preview { occurrence: Occurrence; anchor: HTMLElement; rect: DOMRect }

/** The scope question, held open until its answer: one dialog, three callers — the editor's save,
    a deletion, and task 6's drop. */
interface ScopeAsk {
  title: string
  sentence: string
  allowed: EditScope[]
  resolve: (scope: EditScope | null) => void
}

/** A deletion with nothing to ask about: the shared confirm, then `scope: 'All'`. */
interface PendingEvent { id: string; name: string }

/** What the open form was sown from, and the version it claims to have read. */
interface Seed { key: string; form: EventFormState; hash: string }

/**
 * The calendar module's two columns inside the shell's single outlet, the way the mail and
 * contacts modules build theirs. It owns every query in the module and hands the answers down,
 * so the sidebar, the toolbar and the dialogues mount in a test with no provider at all — and so
 * the views and the editor that follow read one source rather than each asking again.
 */
export default function CalendarLayout() {
  const { t, i18n } = useTranslation('calendar')
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()
  const { toasts, addToast, removeToast } = useToasts()
  const drawer = useContextDrawer()
  const phone = useViewport() === 'phone'

  // Read once: the zone and the region rules are the machine's, and re-deriving them per render
  // would rebuild an Intl formatter on every keystroke in the search box.
  const tz = useMemo(() => Intl.DateTimeFormat().resolvedOptions().timeZone, [])
  const region = navigator.language
  const rules = useMemo(() => weekRulesOf(region), [region])
  const cycle = useMemo(() => hourCycleOf(region), [region])
  const lang = i18n.language
  const locale = useMemo(() => dateLocaleOf(lang, region), [lang, region])

  const rawView = params.get('view')
  const rawDate = params.get('date')
  const today = todayIn(tz)
  // Week is the one view a phone cannot draw: seven columns in 360px is six unreadable ones.
  const chosen = isView(rawView) ? rawView : storedView() ?? 'week'
  const view: View = phone && chosen === 'week' ? 'day' : chosen
  const anchor: PlainDate = rawDate && isPlainDate(rawDate) ? rawDate : today

  // Replace, not push: Back must leave the module rather than bounce off the normalisation.
  useEffect(() => {
    if (rawView === view && rawDate === anchor) return
    setParams(previous => {
      const next = new URLSearchParams(previous)
      next.set('view', view)
      next.set('date', anchor)
      return next
    }, { replace: true })
  }, [rawView, rawDate, view, anchor, setParams])

  const window = useMemo(() => windowOf(view, anchor, tz, rules), [view, anchor, tz, rules])
  const calendarsQuery = useCalendars(tz)
  const windowQuery = useWindow(window, tz)
  const calendars = useMemo(() => calendarsQuery.data ?? [], [calendarsQuery.data])
  const calendarById = useMemo(
    () => new Map(calendars.map(one => [one.id, one])), [calendars])

  const setVisible = useSetCalendarVisible()
  const createCalendar = useCreateCalendar()
  const updateCalendar = useUpdateCalendar()
  const deleteCalendar = useDeleteCalendar()
  const importInto = useImportCalendar()
  const importAsNew = useImportCalendarAsNew()
  const createEvent = useCreateEvent()
  const updateEvent = useUpdateEvent()
  const removeEvent = useDeleteEvent()
  const moveEvent = useMoveOccurrence(window, tz)

  const [query, setQuery] = useState('')
  const [asked, setAsked] = useState('')
  const [editing, setEditing] = useState<Editing | null>(null)
  const [importing, setImporting] = useState<Calendar | null>(null)
  const [report, setReport] = useState<CalendarImportReport | null>(null)
  const [pendingDelete, setPendingDelete] = useState<Calendar | null>(null)
  const [preview, setPreview] = useState<Preview | null>(null)
  const [scopeAsk, setScopeAsk] = useState<ScopeAsk | null>(null)
  const [pendingEvent, setPendingEvent] = useState<PendingEvent | null>(null)
  const [discarding, setDiscarding] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [conflict, setConflict] = useState(false)
  const [reloads, setReloads] = useState(0)

  const creating = useMatch('/calendar/new') != null
  const editMatch = useMatch('/calendar/:id/edit')
  const routeId = editMatch?.params.id ?? null
  const inEditor = creating || routeId != null
  const instanceParam = params.get('instance') ?? ''

  /** Every navigation inside the module keeps the grid where it was: the editor is a surface over
      this screen, not a trip away from it. */
  const searchWith = useCallback((extra: Record<string, string> = {}) => {
    const next = new URLSearchParams({ view, date: anchor, ...extra })
    return `?${next.toString()}`
  }, [view, anchor])

  const setView = useCallback((next: View) => {
    rememberView(next)
    setParams(previous => {
      const search = new URLSearchParams(previous)
      search.set('view', next)
      return search
    })
  }, [setParams])

  const setAnchor = useCallback((day: PlainDate) => {
    setParams(previous => {
      const search = new URLSearchParams(previous)
      search.set('date', day)
      return search
    })
  }, [setParams])

  const openNewEvent = useCallback(() => {
    navigate(`/calendar/new${searchWith()}`)
  }, [navigate, searchWith])

  const openEditor = useCallback((id: string, instanceId?: string) => {
    navigate(`/calendar/${id}/edit${searchWith(instanceId ? { instance: instanceId } : {})}`)
  }, [navigate, searchWith])

  // Search params rather than router state: a reload has to reopen the same draft, and state
  // does not survive one.
  const createAt = useCallback((start: Date, end: Date, allDay: boolean) => {
    navigate(`/calendar/new${searchWith({
      start: start.toISOString(), end: end.toISOString(), allDay: allDay ? '1' : '0',
    })}`)
  }, [navigate, searchWith])

  // The query's own refetch, not the query object: TanStack keeps that function stable while
  // the result is a fresh object every render, which would rebuild the context each time.
  const refetchWindow = windowQuery.refetch
  const retryWindow = useCallback(() => { void refetchWindow() }, [refetchWindow])

  const windowError = windowQuery.isError ? windowErrorOf(windowQuery.error, t) : null

  // A calendar the list has not answered for yet is drawn rather than withheld: a box nobody
  // has unticked hiding its own events would read as a load that lost them.
  const visible = useMemo(
    () => (windowQuery.data ?? []).filter(
      one => calendarById.get(one.calendarId)?.isVisible !== false),
    [windowQuery.data, calendarById])
  const days = useMemo(() => daysOf(window), [window])

  // The phone month's three dots a day, off the placement the grid already does. Empty on every
  // other screen: nothing reads it there, and a month's occurrences would be walked for nothing.
  const dotsByDay = useMemo(() => {
    if (!phone || view !== 'month') return new Map<PlainDate, string[]>()
    return new Map([...itemsByDay(placeAll(visible, tz, days), days)].map(
      ([day, items]) => [day, items.map(one => colorOf(one.occurrence, calendarById))]))
  }, [phone, view, visible, tz, days, calendarById])

  // 300ms after the last keystroke, or the moment Enter is pressed: a request per letter would
  // spend a round trip on every prefix of a word nobody has finished typing.
  useEffect(() => {
    const id = setTimeout(() => setAsked(query), SEARCH_DEBOUNCE_MS)
    return () => clearTimeout(id)
  }, [query])

  const typed = query.trim()
  const term = asked.trim()
  const searchQuery = useSearch(term.length >= SEARCH_MIN ? term : '')
  const clearSearch = useCallback(() => {
    setQuery('')
    setAsked('')
  }, [])

  const openFromChip = useCallback((one: Occurrence) => {
    openEditor(one.eventId, one.instanceId || undefined)
  }, [openEditor])

  // A 300px bubble has nowhere to hang off a 360px screen, so a tap there is the editor itself.
  // The chip's rectangle is read here rather than when the bubble mounts: a search result clears
  // the results as it opens, so the chip is off the screen by then.
  const openPreview = useCallback((one: Occurrence, anchor: HTMLElement) => {
    if (phone) openFromChip(one)
    else setPreview({ occurrence: one, anchor, rect: anchor.getBoundingClientRect() })
  }, [phone, openFromChip])

  const askScope = useCallback((
    mode: 'save' | 'delete', name: string, repeatText: string | null,
    allowed: EditScope[] = EVERY_SCOPE,
  ) => new Promise<EditScope | null>(resolve => {
    setScopeAsk({
      title: mode === 'save' ? t('scope.saveTitle') : t('scope.deleteTitle'),
      sentence: scopeSentence(mode, name, repeatText, t), allowed, resolve,
    })
  }), [t])

  const queryClient = useQueryClient()
  const accountId = useAccountId()
  // The mutate function and not the mutation: TanStack keeps the first stable while the object
  // around it is new on every render, which would rebuild the whole context each time.
  const moveEventAsync = moveEvent.mutateAsync
  /** One lane per event: the promise a gesture in flight resolves when it has settled. */
  const pending = useRef(new Map<string, Promise<void>>())

  /** The version a write has to prove it read: the cache while it is fresh, a request otherwise —
      and always a request when another write on this event has just landed. */
  const loadDetail = useCallback(async (id: string, refresh: boolean) => {
    try {
      return await queryClient.fetchQuery({
        queryKey: calendarKeys.event(accountId, id),
        queryFn: () => api.getEvent(id) as Promise<EventDetail>,
        staleTime: refresh ? 0 : 60_000,
      })
    } catch (error) {
      addToast(apiErrorMessage(error, t('errors.load')), 'error')
      return null
    }
  }, [queryClient, accountId, addToast, t])

  /**
   * A block dropped or a handle released. The detail is what a write needs — the version it read
   * and the zone the event is written in — so it is taken from the cache the editor fills, or
   * fetched once; a series is asked how far the change reaches before anything is sent; and the
   * occurrence is spliced into the window so the block stays where the pointer left it.
   *
   * One event's gestures are queued behind each other. "Move it, then a quarter of an hour more"
   * is one gesture in the user's head and two drops in ours: sent together they carry the same
   * `ifHash`, and the second comes back 409 — a refusal for having done exactly what the grid
   * invites. The second therefore waits for the first to settle and reads the version it wrote.
   */
  const applyGesture = useCallback(async (
    one: Occurrence, deltaMinutes: number, deltaDays: number, newDuration: number | null,
  ) => {
    const delta = deltaMinutes + deltaDays * MINUTES_PER_DAY
    if (delta === 0 && newDuration === null) return

    // Claimed before the first await: two drops a frame apart would otherwise both find the lane
    // empty and set off together.
    const queued = pending.current.get(one.eventId)
    let release = () => {}
    const mine = new Promise<void>(resolve => { release = resolve })
    pending.current.set(one.eventId, mine)

    try {
      // Behind another write, the cached copy is the version that write has just replaced.
      if (queued) await queued
      const detail = await loadDetail(one.eventId, queued !== undefined)
      if (detail === null) return

      // An occurrence of a series carries its RECURRENCE-ID; a lone event's is empty. A drop never
      // changes calendar, so it has no scope to withhold.
      let scope: EditScope = 'All'
      if (one.instanceId) {
        const chosen = await askScope('save', one.summary || t('views.noTitle'),
          one.recurrenceText ?? '')
        if (chosen === null) return
        scope = chosen
      }

      await moveEventAsync({
        id: one.eventId,
        body: movedBody(detail, one, delta, newDuration, scope),
        moved: movedOccurrence(detail, one, delta, newDuration),
      }).catch(error => addToast(apiErrorMessage(error, t('errors.save')), 'error'))
    } finally {
      release()
      if (pending.current.get(one.eventId) === mine) pending.current.delete(one.eventId)
    }
  }, [loadDetail, addToast, t, askScope, moveEventAsync])

  const startGesture = useCallback(() => setPreview(null), [])
  const moveOccurrence = useCallback(
    (one: Occurrence, deltaMinutes: number, deltaDays: number) => {
      void applyGesture(one, deltaMinutes, deltaDays, null)
    }, [applyGesture])
  const resizeOccurrence = useCallback((one: Occurrence, duration: number) => {
    void applyGesture(one, 0, 0, duration)
  }, [applyGesture])

  const context: CalendarContextValue = useMemo(() => ({
    tz, rules, lang, region, cycle, view, anchor, today, setView, setAnchor, calendars,
    calendarById, window, occurrences: windowQuery.data, visible, windowError, retryWindow,
    openEditor, createAt, askScope, startGesture, moveOccurrence, resizeOccurrence,
  }), [tz, rules, lang, region, cycle, view, anchor, today, setView, setAnchor, calendars,
    calendarById, window, windowQuery.data, visible, windowError, retryWindow, openEditor,
    createAt, askScope, startGesture, moveOccurrence, resizeOccurrence])

  const eventQuery = useEvent(routeId)
  const detail = eventQuery.data ?? null

  // Three places an occurrence can be found, in the order they cost nothing: the window the grid
  // already holds, the search results the editor may have been opened from, and — only when
  // neither has it — one day fetched around the instance itself. Without the last two, opening a
  // search result seeded the editor from the *master's* hours and a narrow save left with no
  // instance at all, which moves the occurrence instead of editing it.
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
    const rawStart = params.get('start')
    const rawEnd = params.get('end')
    const start = rawStart ? new Date(rawStart) : nextHour()
    const end = rawEnd ? new Date(rawEnd) : new Date(start.getTime() + HOUR_MS)
    return newEventForm(start, end, params.get('allDay') === '1', defaultCalendarId(), tz)
  }

  // The editor takes its values once, at the render it mounts on: a refetch landing behind an open
  // form must not reseed what is being typed. The key is what makes it seed again — `reloads` is
  // the deliberate lever, pulled by the Reload button a stale write puts on screen.
  const editorKey = inEditor ? `${routeId ?? 'new'}#${instanceParam}#${reloads}` : null
  const [seed, setSeed] = useState<Seed | null>(null)
  // Latched on the seed: `occurrenceFound` is recomputed every render, and any invalidation — one
  // of this module's own mutations, a focus refetch — can bring the window back without the
  // instance being edited, which flipped this false, unmounted the keyed editor and threw away
  // what was being typed. A form already sown never waits for anything again.
  const editorReady = seed?.key === editorKey
    || ((routeId ? detail != null : calendarsQuery.data !== undefined) && occurrenceFound)
  if (editorKey && editorReady && seed?.key !== editorKey) {
    setSeed({
      key: editorKey,
      form: detail ? formOf(detail, occurrence, tz) : newDraft(),
      // Frozen here, never read live at save time: a refused write would otherwise hand the retry
      // the very version that refused it — a claim to have read what the user never saw.
      hash: detail?.icsHash ?? '',
    })
  }

  // A refusal belongs to the form it happened in, and the bubble must not survive the editor
  // opening over it.
  const [errorKey, setErrorKey] = useState(editorKey)
  if (editorKey !== errorKey) {
    setErrorKey(editorKey)
    setSaveError(null)
    setConflict(false)
    if (editorKey) setPreview(null)
  }

  // An id the server no longer resolves is an obsolete bookmark, never an invitation to create.
  const eventError = eventQuery.isError ? eventQuery.error : null
  useEffect(() => {
    if (!eventError) return
    const status = (eventError as { status?: number }).status
    addToast(status === 404 ? t('errors.notFound')
      : apiErrorMessage(eventError, t('errors.load')), 'error')
    navigate('/calendar', { replace: true })
  }, [eventError, addToast, navigate, t])

  async function saveCalendar({ displayName, color }: CalendarValues) {
    try {
      if (editing?.mode === 'create') await createCalendar.mutateAsync({
        calendar: { displayName, color }, tz,
      })
      else if (editing) await updateCalendar.mutateAsync({
        id: editing.calendar.id, calendar: { displayName, color },
      })
      setEditing(null)
    } catch (error) {
      // The dialog stays open carrying what was typed: a refusal that closed it would make the
      // user retype the name to find out whether it was the name that was refused.
      addToast(apiErrorMessage(error, t('errors.calendarSave')), 'error')
    }
  }

  async function confirmDelete() {
    if (!pendingDelete) return
    try {
      await deleteCalendar.mutateAsync(pendingDelete.id)
    } catch (error) {
      addToast(apiErrorMessage(error, t('errors.calendarDelete')), 'error')
    } finally {
      setPendingDelete(null)
    }
  }

  function toggleVisible(calendar: Calendar, visible: boolean) {
    setVisible.mutate({ id: calendar.id, visible }, {
      onError: error => addToast(apiErrorMessage(error, t('errors.calendarSave')), 'error'),
    })
  }

  async function exportOne(calendar: Calendar) {
    try {
      const { blob, fileName } = await api.exportCalendar(calendar.id)
      downloadBlob(blob, fileName)
    } catch (error) {
      addToast(apiErrorMessage(error, t('errors.export')), 'error')
    }
  }

  async function runImport(choice: ImportChoice) {
    try {
      setReport(choice.mode === 'existing'
        ? await importInto.mutateAsync({ id: choice.id, file: choice.file })
        : (await importAsNew.mutateAsync({
            file: choice.file, displayName: choice.displayName, color: choice.color, tz,
          })).report)
      setImporting(null)
    } catch (error) {
      addToast(apiErrorMessage(error, t('errors.import')), 'error')
    }
  }

  const backToGrid = () => navigate(`/calendar${searchWith()}`, { replace: true })
  const summaryOf = (form: EventFormState) => {
    const rule = ruleOf(form.repeat)
    return rule ? recurrenceSummary(rule, t, lang, region) : null
  }

  async function saveEvent(form: EventFormState, scope: EditScope | null) {
    setSaveError(null)
    // Cleared with the message it belongs to: a Reload button left beside an unrelated refusal
    // offers a way out of a conflict that is no longer there.
    setConflict(false)
    try {
      if (detail) {
        let chosen = scope
        // Décision 8: the occurrence's own RECURRENCE-ID, never the repeat the picker holds. A
        // repeat just added has no other occurrence, so there is nothing to ask about.
        if (chosen === null && isRecurring(occurrence)) {
          chosen = await askScope('save', form.title || t('views.noTitle'), summaryOf(form),
            allowedScopes(form, detail, occurrence))
          if (chosen === null) return
        }
        // The editor always hands `scope` in as null, so the question above is the only door to a
        // narrow scope and it is asked only on an occurrence that has one. This covers a caller
        // that named a scope itself: sent with no instance, the server takes the whole series.
        if (chosen !== null && chosen !== 'All' && !occurrence?.instanceId) {
          setSaveError(t('errors.occurrenceMissing'))
          return
        }
        await updateEvent.mutateAsync({
          id: detail.id,
          body: updateBodyOf(form, { ...detail, icsHash: seed?.hash ?? detail.icsHash },
            occurrence, chosen ?? 'All'),
        })
      } else {
        await createEvent.mutateAsync(writeOf(form))
      }
      rememberCalendar(form.calendarId)
      addToast(t('editor.saved'), 'success')
      backToGrid()
    } catch (error) {
      // The form stays exactly as it was typed: bouncing back to a grid that kept nothing is how
      // somebody loses an hour's work without being told why. A stale write gets a band with a way
      // out rather than a toast, and the hash it was seeded with is kept — a bare retry is refused
      // again instead of silently overwriting what the other client wrote.
      if (isConflict(error)) {
        setConflict(true)
        setSaveError(t('errors.conflict'))
        return
      }
      setSaveError(apiErrorMessage(error, t('errors.save')))
    }
  }

  /** The user's own choice, never a consequence of the refusal: the form stands untouched behind
      the band until this runs. A refetch that failed has nothing to seed from, so nothing moves. */
  async function reloadEvent() {
    const { isError: failed } = await eventQuery.refetch()
    if (!failed) setReloads(previous => previous + 1)
  }

  async function runDelete(id: string, scope: EditScope, instanceId?: string) {
    if (scope !== 'All' && !instanceId) {
      addToast(t('errors.occurrenceMissing'), 'error')
      return
    }
    try {
      // The instance names an occurrence, so it travels only with a scope that reaches one.
      await removeEvent.mutateAsync({
        id, scope, instanceId: scope === 'All' ? undefined : instanceId,
      })
      addToast(t('editor.deleted'), 'success')
      setPreview(null)
      if (inEditor) backToGrid()
    } catch (error) {
      addToast(apiErrorMessage(error, t('errors.delete')), 'error')
    }
  }

  /** One deletion, whichever door it came through: nothing to ask on a lone event, the scope
      question on a series. `repeatText` is `null` for an event that does not repeat, and the empty
      string for one whose rule nothing has worded — which is still a series. */
  async function askDelete(
    id: string, name: string, repeatText: string | null, instanceId?: string,
  ) {
    if (repeatText === null) return setPendingEvent({ id, name })
    const scope = await askScope('delete', name, repeatText)
    if (scope) await runDelete(id, scope, instanceId)
  }

  function deleteEdited() {
    if (!detail) return
    const rule = detail.fields.repeat
    void askDelete(detail.id, detail.fields.summary || t('views.noTitle'),
      rule ? detail.recurrenceText ?? recurrenceSummary(rule, t, lang, region) : null,
      occurrence?.instanceId)
  }

  function deletePreviewed(one: Occurrence) {
    // An occurrence of a series carries its RECURRENCE-ID; a lone event's is empty.
    void askDelete(one.eventId, one.summary || t('views.noTitle'),
      one.instanceId ? one.recurrenceText ?? '' : null, one.instanceId)
  }

  const sidebar = (
    <CalendarSidebar calendars={calendars} anchor={anchor} today={today} rules={rules}
      locale={locale} loading={calendarsQuery.isLoading} failed={calendarsQuery.isError}
      onPickDay={setAnchor} onNewEvent={openNewEvent}
      onNewCalendar={() => setEditing({ mode: 'create' })}
      onRename={calendar => setEditing({ mode: 'rename', calendar })}
      onRecolour={calendar => setEditing({ mode: 'colour', calendar })}
      onImport={setImporting} onExport={exportOne} onDelete={setPendingDelete}
      onToggleVisible={toggleVisible} />
  )

  // The ✕ is drawn before the form is: a load that never lands — a refused calendar list on
  // `/calendar/new` — would otherwise be a room whose only door is the browser's Back button.
  const editorBody = editorReady && seed ? (
    <EventEditor key={seed.key} detail={detail} occurrence={occurrence} initial={seed.form}
      calendars={calendars} saving={createEvent.isPending || updateEvent.isPending}
      error={saveError} onReload={conflict ? () => void reloadEvent() : null} fullScreen={phone}
      onSave={saveEvent} onDelete={deleteEdited}
      onClose={dirty => (dirty ? setDiscarding(true) : backToGrid())} />
  ) : (
    <>
      <div className={phone ? 'calendar-editor-head' : 'modal-header'}>
        <span className="modal-title">{t(routeId ? 'editor.editTitle' : 'editor.newTitle')}</span>
        <button type="button" className="modal-close" aria-label={t('editor.close')}
          onClick={backToGrid}>✕</button>
      </div>
      <LoadingBlock />
    </>
  )

  const ready = calendarsQuery.data !== undefined && windowQuery.data !== undefined
  // Every chip of that occurrence lights, wherever it is drawn — both slices of an evening
  // crossing midnight, and the one the open bubble hangs off (décisions 3 and 10).
  const selectedKey = preview ? occurrenceKey(preview.occurrence) : undefined
  const stage = view === 'month'
    ? phone
      ? (
        <div className="phone-month-stage">
          <PhoneMonth anchor={anchor} selected={anchor} dotsByDay={dotsByDay}
            onPick={setAnchor} />
          <UpcomingList days={[anchor]} empty={t('views.emptyDay')} onOpen={openPreview}
            onOpenEditor={openFromChip} />
        </div>
      )
      : <MonthView selectedKey={selectedKey} onOpen={openPreview} onOpenEditor={openFromChip} />
    : view === 'list'
      ? (
        <UpcomingList days={days} selectedKey={selectedKey} onOpen={openPreview}
          onOpenEditor={openFromChip} />
      )
      : (
        <>
          {phone && <DayStrip selected={anchor} onPick={setAnchor} />}
          <WeekView days={days} gestures={!phone} selectedKey={selectedKey}
            previewOpen={preview !== null} onOpen={openPreview}
            onOpenEditor={openFromChip} />
        </>
      )

  return (
    <CalendarContext.Provider value={context}>
      <div className="calendar-layout">
        {drawer.inDrawer
          ? <ContextDrawer open={drawer.open} onClose={drawer.close}>{sidebar}</ContextDrawer>
          : sidebar}

        <div className="calendar-main">
          <CalendarToolbar view={view} title={formatRangeTitle(
            window.firstVisible, window.lastVisible, view, lang, region)}
            weekNumber={view === 'day' || view === 'week' ? weekNumberOf(anchor, rules) : null}
            query={query} phone={phone} inDrawer={drawer.inDrawer} onOpenDrawer={drawer.toggle}
            onQuery={setQuery} onCommitQuery={() => setAsked(query)}
            onToday={() => setAnchor(today)} onView={setView}
            onStep={delta => setAnchor(stepAnchor(view, anchor, delta))} />

          {/* A band of its own rather than a row of the toolbar: the phone's toolbar has no room
              for a 30ch box beside three segments, and searching is what the list is opened for. */}
          {phone && view === 'list' && (
            <CalendarSearch className="phone-search" query={query} onQuery={setQuery}
              onCommitQuery={() => setAsked(query)} />
          )}

          <div className="calendar-stage">
            {typed ? (
              <SearchResults occurrences={searchQuery.data ?? []}
                loading={searchQuery.isLoading || term !== typed} failed={searchQuery.isError}
                tooShort={typed.length < SEARCH_MIN} onClear={clearSearch}
                selectedKey={selectedKey} onOpen={openPreview} onOpenEditor={openFromChip} />
            ) : windowError ? (
              <div className="calendar-error">
                <p>{windowError}</p>
                <button type="button" className="btn" onClick={retryWindow}>{t('errors.retry')}</button>
              </div>
            ) : ready ? stage : <LoadingBlock />}
          </div>
        </div>

        {preview && (
          <EventPreview occurrence={preview.occurrence}
            calendar={calendarById.get(preview.occurrence.calendarId) ?? null}
            anchor={preview.anchor} rect={preview.rect} onClose={() => setPreview(null)}
            onEdit={() => openFromChip(preview.occurrence)}
            onDelete={() => deletePreviewed(preview.occurrence)} />
        )}

        {/* A dialogue over the grid from 640px up, the whole screen below it. */}
        {inEditor && (phone
          ? (
            <div className="calendar-editor-screen" data-testid="calendar-editor">{editorBody}</div>
          )
          : (
            <div className="modal-overlay" data-testid="calendar-editor">
              <div className="modal calendar-editor">{editorBody}</div>
            </div>
          ))}

        {scopeAsk && (
          <ScopeModal title={scopeAsk.title} sentence={scopeAsk.sentence} allowed={scopeAsk.allowed}
            onPick={scope => { setScopeAsk(null); scopeAsk.resolve(scope) }}
            onClose={() => { setScopeAsk(null); scopeAsk.resolve(null) }} />
        )}

        {pendingEvent && (
          <DeleteConfirmModal
            message={t('dialogs.deleteEventMessage', { name: pendingEvent.name })}
            loading={removeEvent.isPending}
            onConfirm={() => {
              const { id } = pendingEvent
              setPendingEvent(null)
              void runDelete(id, 'All')
            }}
            onClose={() => setPendingEvent(null)} />
        )}

        {discarding && (
          <DeleteConfirmModal title={t('editor.discardTitle')} message={t('editor.discardBody')}
            confirmLabel={t('editor.discard')}
            onConfirm={() => { setDiscarding(false); backToGrid() }}
            onClose={() => setDiscarding(false)} />
        )}

        {editing && (
          <CalendarDialog
            title={t(editing.mode === 'create' ? 'dialogs.newCalendar' : 'dialogs.editCalendar')}
            initialName={editing.mode === 'create' ? '' : editing.calendar.displayName}
            initialColor={editing.mode === 'create' ? CALENDAR_COLORS[0] : editing.calendar.color}
            focus={editing.mode === 'colour' ? 'colour' : 'name'}
            saving={createCalendar.isPending || updateCalendar.isPending}
            onSubmit={saveCalendar} onClose={() => setEditing(null)} />
        )}

        {importing && (
          <ImportDialog calendars={calendars} targetId={importing.id}
            saving={importInto.isPending || importAsNew.isPending}
            onImport={runImport} onClose={() => setImporting(null)} />
        )}

        {report && <CalendarImportReportModal report={report} onClose={() => setReport(null)} />}

        {pendingDelete && (
          <DeleteConfirmModal
            message={t('dialogs.deleteCalendarMessage', { name: pendingDelete.displayName })}
            loading={deleteCalendar.isPending}
            onConfirm={confirmDelete} onClose={() => setPendingDelete(null)} />
        )}

        {/* Anchored 73px up from the edge the tab bar owns, and the editor owns the whole screen
            below 640px: it is withheld there, exactly as mail and contacts withhold theirs. */}
        {!inEditor && (
          <FloatingAction label={t('phone.newEvent')} onClick={openNewEvent}>
            <PlusIcon size={22} />
          </FloatingAction>
        )}

        <Toasts toasts={toasts} onRemove={removeToast} />
      </div>
    </CalendarContext.Provider>
  )
}
