import { useCallback, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import type { TFunction } from 'i18next'
import FloatingAction from '../../components/FloatingAction'
import LoadingBlock from '../../components/LoadingBlock'
import Modal from '../../components/Modal'
import Toasts from '../../components/Toasts'
import { useLayer } from '../../hooks/useLayer'
import { useToasts } from '../../hooks/useToasts'
import { useViewport } from '../../hooks/useViewport'
import PlusIcon from '../../icons/PlusIcon'
import ContextDrawer, { useContextDrawer } from '../../layouts/ContextDrawer'
import { apiErrorMessage } from '../../lib/apiErrorMessage'
import { CalendarContext, type CalendarContextValue } from './calendarContext'
import CalendarDialogs, { type PendingEvent, type ScopeAsk } from './CalendarDialogs'
import CalendarSidebar from './CalendarSidebar'
import CalendarToolbar, { CalendarSearch } from './CalendarToolbar'
import { dateLocaleOf, formatRangeTitle, hourCycleOf, weekNumberOf, weekRulesOf } from './calendarLocale'
import type { EditScope, Occurrence } from './calendarTypes'
import EventEditor, { EDITOR_TITLE_ID } from './EventEditor'
import EventPreview from './EventPreview'
import MonthView from './MonthView'
import { itemsByDay, placeAll } from './multiDay'
import { colorOf, occurrenceKey } from './occurrenceStyle'
import DayStrip from './phone/DayStrip'
import PhoneMonth from './phone/PhoneMonth'
import { addDays, daysBetween, type PlainDate } from './plainDate'
import {
  useCalendars, useCreateEvent, useDeleteEvent, useMoveOccurrence, useUpdateEvent, useWindow,
} from './queries'
import { scopeSentence } from './ScopeModal'
import SearchResults from './SearchResults'
import UpcomingList from './UpcomingList'
import { SEARCH_MIN, useCalendarSearch } from './useCalendarSearch'
import { stepAnchor, useCalendarUrlState } from './useCalendarUrlState'
import { useCalendarWrites } from './useCalendarWrites'
import { useEditorSeed } from './useEditorSeed'
import { useEventWrites } from './useEventWrites'
import { useGestureWrites } from './useGestureWrites'
import { useToday } from './useToday'
import WeekView from './WeekView'
import { windowOf, type Window } from './windowOf'

const EVERY_SCOPE: EditScope[] = ['This', 'ThisAndFollowing', 'All']

function daysOf(window: Window): PlainDate[] {
  const count = daysBetween(window.firstVisible, window.lastVisible) + 1
  return Array.from({ length: count }, (_, index) => addDays(window.firstVisible, index))
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

interface Preview { occurrence: Occurrence; anchor: HTMLElement; rect: DOMRect }

/** The module's two columns. It owns the grid's and the editor's queries and hands the answers
 * down, so the sidebar, toolbar and dialogs mount in a test with no provider; only the bubble
 * fetches its own detail. */
export default function CalendarLayout() {
  const { t, i18n } = useTranslation('calendar')
  const navigate = useNavigate()
  const { toasts, addToast, removeToast, pauseToast, resumeToast } = useToasts()
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

  const today = useToday(tz)
  const {
    params, view, anchor, routeId, inEditor, instanceParam, searchWith, setView, setAnchor,
  } = useCalendarUrlState(phone, today)

  const window = useMemo(() => windowOf(view, anchor, tz, rules), [view, anchor, tz, rules])
  const calendarsQuery = useCalendars(tz)
  const windowQuery = useWindow(window, tz)
  const calendars = useMemo(() => calendarsQuery.data ?? [], [calendarsQuery.data])
  const calendarById = useMemo(
    () => new Map(calendars.map(one => [one.id, one])), [calendars])

  const calendarWrites = useCalendarWrites(tz, addToast)
  const createEvent = useCreateEvent()
  const updateEvent = useUpdateEvent()
  const removeEvent = useDeleteEvent()
  const moveEvent = useMoveOccurrence(window, tz)
  // One flag for the three surfaces a write in flight makes inert: the form's own Save, the
  // dialog's backdrop and Escape, and the phone screen's Escape.
  const savingEvent = createEvent.isPending || updateEvent.isPending

  const [preview, setPreview] = useState<Preview | null>(null)
  const [scopeAsk, setScopeAsk] = useState<ScopeAsk | null>(null)
  const [pendingEvent, setPendingEvent] = useState<PendingEvent | null>(null)
  const [discarding, setDiscarding] = useState(false)
  // Lifted out of the form: the ways out are the surface's, and the question behind each of them
  // is the same one the ✕ asks.
  const [editorDirty, setEditorDirty] = useState(false)
  const editorScreenRef = useRef<HTMLDivElement>(null)
  const editorTitleRef = useRef<HTMLInputElement>(null)
  // Where the editor hands focus back when what opened it is gone. Not the floating +, nor the
  // bubble's Edit: React attaches a ref in the layout phase, after the cleanup that reads this one.
  const mainRef = useRef<HTMLDivElement>(null)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [conflict, setConflict] = useState(false)
  const [reloads, setReloads] = useState(0)

  const openNewEvent = useCallback(() => {
    void navigate(`/calendar/new${searchWith()}`)
  }, [navigate, searchWith])

  const openEditor = useCallback((id: string, instanceId?: string) => {
    void navigate(`/calendar/${id}/edit${searchWith(instanceId ? { instance: instanceId } : {})}`)
  }, [navigate, searchWith])

  // Search params rather than router state: a reload has to reopen the same draft, and state
  // does not survive one.
  const createAt = useCallback((start: Date, end: Date, allDay: boolean) => {
    void navigate(`/calendar/new${searchWith({
      start: start.toISOString(), end: end.toISOString(), allDay: allDay ? '1' : '0',
    })}`)
  }, [navigate, searchWith])

  // The query's own refetch, not the query object: TanStack keeps that function stable while
  // the result is a fresh object every render, which would rebuild the context each time.
  const refetchWindow = windowQuery.refetch
  const retryWindow = useCallback(() => { void refetchWindow() }, [refetchWindow])

  // A refetch that fails keeps the data it had: only a window with nothing to draw is an error.
  const windowError = windowQuery.isError && windowQuery.data === undefined
    ? windowErrorOf(windowQuery.error, t) : null

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

  const {
    query, setQuery, commitQuery, typed, term, searchQuery, clearSearch,
  } = useCalendarSearch()

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

  // The mutate function and not the mutation: TanStack keeps the first stable while the object
  // around it is new on every render, which would rebuild the whole context each time.
  const moveEventAsync = moveEvent.mutateAsync
  const { moveOccurrence, resizeOccurrence } = useGestureWrites({
    moveEventAsync, askScope, addToast, lang, region,
  })
  const startGesture = useCallback(() => setPreview(null), [])

  const context: CalendarContextValue = useMemo(() => ({
    tz, rules, lang, region, cycle, view, anchor, today, setView, setAnchor, calendars,
    calendarById, window, occurrences: windowQuery.data, visible, windowError, retryWindow,
    openEditor, createAt, askScope, startGesture, moveOccurrence, resizeOccurrence,
  }), [tz, rules, lang, region, cycle, view, anchor, today, setView, setAnchor, calendars,
    calendarById, window, windowQuery.data, visible, windowError, retryWindow, openEditor,
    createAt, askScope, startGesture, moveOccurrence, resizeOccurrence])

  const { eventQuery, detail, occurrence, editorKey, seed, editorReady } = useEditorSeed({
    params, routeId, instanceParam, inEditor, reloads, anchor, tz, rules, windowQuery, searchQuery,
    calendarsLoaded: calendarsQuery.data !== undefined, calendars, calendarById, addToast, navigate,
  })
  // The discard question dies with the editor it belongs to, whatever took the route away — the
  // browser's Back never passes through `backToGrid`. Hidden is not dropped: a flag left standing
  // greeted the next editor with "Discard changes?" over an empty form.
  if (!inEditor && discarding) setDiscarding(false)

  // A refusal belongs to the form it happened in, and the bubble must not survive the editor
  // opening over it.
  const [errorKey, setErrorKey] = useState(editorKey)
  if (editorKey !== errorKey) {
    setErrorKey(editorKey)
    setSaveError(null)
    setConflict(false)
    if (editorKey) setPreview(null)
  }

  const backToGrid = () => { void navigate(`/calendar${searchWith()}`, { replace: true }) }
  /** The editor's one way out, whichever of the four was taken — the ✕, Escape, a press on the
      backdrop, or the phone screen's own Escape. */
  const closeEditor = (dirty: boolean) => (dirty ? setDiscarding(true) : backToGrid())

  // The phone editor is the screen rather than a dialog, so it draws no `Modal` — but while it
  // stands it owns Escape and Tab exactly as one does, which is what a layer is.
  useLayer({
    active: inEditor && phone,
    ref: editorScreenRef,
    // No handler while the write is in flight: the layer still swallows the key, so nothing under
    // it reacts either — `Modal`'s `busy` on the desktop side, in the one shape a layer has.
    onEscape: savingEvent ? undefined : () => closeEditor(editorDirty),
    initialFocusRef: editorTitleRef,
    returnFocusRef: mainRef,
  })

  const { saveEvent, reloadEvent, runDelete, deleteEdited, deletePreviewed } = useEventWrites({
    detail, occurrence, seed, eventQuery, inEditor, previewOpen: preview !== null,
    closePreview: () => setPreview(null), mainRef, askScope, backToGrid, setSaveError,
    setConflict, setReloads, setPendingEvent, createEvent, updateEvent, removeEvent, addToast,
    lang, region,
  })

  const sidebar = (
    <CalendarSidebar calendars={calendars} anchor={anchor} today={today} rules={rules}
      locale={locale} loading={calendarsQuery.isLoading}
      failed={calendarsQuery.isError && calendarsQuery.data === undefined}
      onPickDay={setAnchor} onNewEvent={openNewEvent}
      onNewCalendar={() => calendarWrites.setEditing({ mode: 'create' })}
      onRename={calendar => calendarWrites.setEditing({ mode: 'rename', calendar })}
      onRecolour={calendar => calendarWrites.setEditing({ mode: 'colour', calendar })}
      onImport={calendarWrites.setImporting}
      onExport={calendar => void calendarWrites.exportOne(calendar)}
      onDelete={calendarWrites.setPendingDelete} onToggleVisible={calendarWrites.toggleVisible} />
  )

  // The ✕ is drawn before the form is: a load that never lands — a refused calendar list on
  // `/calendar/new` — would otherwise be a room whose only door is the browser's Back button.
  const editorBody = editorReady && seed ? (
    <EventEditor key={seed.key} detail={detail} initial={seed.form}
      calendars={calendars} saving={savingEvent}
      error={saveError} onReload={conflict ? () => void reloadEvent() : null} fullScreen={phone}
      titleRef={editorTitleRef} onSave={(form, scope) => void saveEvent(form, scope)} onDelete={deleteEdited}
      onClose={closeEditor} onDirtyChange={setEditorDirty} />
  ) : (
    <>
      <div className={phone ? 'calendar-editor-head' : 'modal-header'}>
        <span className="modal-title" id={EDITOR_TITLE_ID}>
          {t(routeId ? 'editor.editTitle' : 'editor.newTitle')}
        </span>
        <button type="button" className="modal-close" aria-label={t('editor.close')}
          onClick={backToGrid}>✕</button>
      </div>
      <LoadingBlock />
    </>
  )

  // Wait for the list to answer or be refused: drawn earlier, chips would wear the default colour
  // and hidden calendars' events would show.
  const ready = windowQuery.data !== undefined
    && (calendarsQuery.data !== undefined || calendarsQuery.isError)
  // Every chip of that occurrence lights, wherever it is drawn — both slices of an evening
  // crossing midnight, and the one the open bubble hangs off.
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
      : (
        // Keyed on the anchor, so the stop follows it through either door: a chevron re-keys five
        // rows and the hook recovered next door — an outside day, where Enter created in the month
        // just left — while a mini-month pick moves only `aria-selected`, which nothing observes.
        <MonthView key={anchor} previewOpen={preview !== null}
          selectedKey={selectedKey} onOpen={openPreview} onOpenEditor={openFromChip} />
      )
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

        <div className="calendar-main" ref={mainRef} tabIndex={-1}>
          <CalendarToolbar view={view} title={formatRangeTitle(
            window.firstVisible, window.lastVisible, view, lang, region)}
            weekNumber={view === 'day' || view === 'week' ? weekNumberOf(anchor, rules) : null}
            query={query} phone={phone} inDrawer={drawer.inDrawer} onOpenDrawer={drawer.toggle}
            onQuery={setQuery} onCommitQuery={commitQuery}
            onToday={() => setAnchor(today)} onView={setView}
            onStep={delta => setAnchor(stepAnchor(view, anchor, delta))} />

          {/* A band of its own rather than a row of the toolbar: the phone's toolbar has no room
              for a 30ch box beside three segments, and searching is what the list is opened for. */}
          {phone && view === 'list' && (
            <CalendarSearch className="phone-search" query={query} onQuery={setQuery}
              onCommitQuery={commitQuery} />
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
            anchor={preview.anchor} rect={preview.rect} returnFocusRef={mainRef}
            onClose={() => setPreview(null)}
            onEdit={() => openFromChip(preview.occurrence)}
            onDelete={() => deletePreviewed(preview.occurrence)} />
        )}

        {/* A dialogue over the grid from 640px up, the whole screen below it. The header is the
            editor's own, so the dialog is named by it rather than by one `Modal` would draw. */}
        {inEditor && (phone
          ? (
            /* Not a `Modal`, but it covers the screen and traps Tab, so it says so: a reader left
               free to browse the grid behind it would be reading what no key can reach. */
            <div className="calendar-editor-screen" data-testid="calendar-editor"
              role="dialog" aria-modal="true" aria-labelledby={EDITOR_TITLE_ID} tabIndex={-1}
              ref={editorScreenRef}>{editorBody}</div>
          )
          : (
            <Modal header={false} labelledBy={EDITOR_TITLE_ID} className="calendar-editor"
              initialFocusRef={editorTitleRef} returnFocusRef={mainRef} busy={savingEvent}
              onClose={() => closeEditor(editorDirty)}>
              {editorBody}
            </Modal>
          ))}

        <CalendarDialogs scopeAsk={scopeAsk} onScopeAskClosed={() => setScopeAsk(null)}
          pendingEvent={pendingEvent} onPendingEventClosed={() => setPendingEvent(null)}
          deletingEvent={removeEvent.isPending} onDeleteEvent={id => runDelete(id, 'All')}
          inEditor={inEditor} discarding={discarding} onDiscard={backToGrid}
          onDiscardClosed={() => setDiscarding(false)} calendars={calendars}
          writes={calendarWrites} returnFocusRef={mainRef} />

        {/* Anchored 73px up from the edge the tab bar owns, and the editor owns the whole screen
            below 640px: it is withheld there, exactly as mail and contacts withhold theirs. */}
        {!inEditor && (
          <FloatingAction label={t('phone.newEvent')} onClick={() => openNewEvent()}>
            <PlusIcon size={22} />
          </FloatingAction>
        )}

        <Toasts toasts={toasts} onRemove={removeToast} onPause={pauseToast} onResume={resumeToast} />
      </div>
    </CalendarContext.Provider>
  )
}
