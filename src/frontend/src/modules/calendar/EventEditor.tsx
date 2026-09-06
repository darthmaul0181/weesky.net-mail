import { useState, type CSSProperties, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import ChevronDownIcon from '../../icons/ChevronDownIcon'
import ChevronRightIcon from '../../icons/ChevronRightIcon'
import { useCalendar } from './calendarContext'
import type {
  Availability, Calendar, EditScope, EventDetail, Occurrence, RecurrenceWrite, Visibility,
} from './calendarTypes'
import {
  ruleOf, type EventFormState, type RepeatChoice, validate,
} from './eventForm'
import { addDays, clockOf, daysBetween, isPlainDate, MINUTES_PER_DAY } from './plainDate'
import RecurrenceEditor from './RecurrenceEditor'
import { recurrenceSummary } from './recurrenceSummary'
import ReminderList from './ReminderList'
import { convertReminder } from './reminderPresets'

export interface EventEditorProps {
  /** `null` is a creation: the layout owns every query, so an absent detail is the whole signal. */
  detail: EventDetail | null
  occurrence: Occurrence | null
  initial: EventFormState
  calendars: Calendar[]
  saving: boolean
  error: string | null
  /** Non-null only after a stale write: the band under the form then carries a way out, and a
      bare retry is refused again rather than overwriting what the other client wrote. */
  onReload: (() => void) | null
  fullScreen: boolean
  onSave(form: EventFormState, scope: EditScope | null): void
  onDelete(scope: EditScope | null): void
  /** Carries whether anything was typed: the layout owns the discard question, and only the form
      knows what it holds. */
  onClose(dirty: boolean): void
}

const FORM_ID = 'calendar-event-form'
const REPEATS: RepeatChoice['kind'][] = ['never', 'daily', 'weekly', 'monthly', 'yearly', 'custom']
const DEFAULT_RULE: RecurrenceWrite = {
  frequency: 'WEEKLY', interval: 1, byDay: [], end: 'Never',
}

const CLOCK = /^\d{2}:\d{2}$/
const minutesOf = (time: string) => Number(time.slice(0, 2)) * 60 + Number(time.slice(3, 5))

/** Moving the start carries the end with it: a meeting pushed to next week is still an hour long,
    and an end left behind would silently become an end before its start. */
function withStart(form: EventFormState, startDate: string, startTime: string): EventFormState {
  // A box being emptied is a keystroke, not a date: `addDays` on it answers an invalid instant,
  // and with no ErrorBoundary anywhere in the app that whitens the whole screen.
  const usable = [startDate, form.startDate, form.endDate].every(isPlainDate)
    && (form.isAllDay || [startTime, form.startTime, form.endTime].every(one => CLOCK.test(one)))
  if (!usable) return { ...form, startDate, startTime }

  const days = daysBetween(form.startDate, form.endDate)
  if (form.isAllDay) return { ...form, startDate, endDate: addDays(startDate, days) }

  const duration = days * MINUTES_PER_DAY + minutesOf(form.endTime) - minutesOf(form.startTime)
  const total = minutesOf(startTime) + duration
  const carried = Math.floor(total / MINUTES_PER_DAY)
  return {
    ...form, startDate, startTime,
    endDate: addDays(startDate, carried),
    endTime: clockOf(total - carried * MINUTES_PER_DAY),
  }
}

/** A value equality over a shape both sides were built from by spreading, so the key order is the
    same on each — what "the user changed something" means here. */
const same = (a: EventFormState, b: EventFormState) => JSON.stringify(a) === JSON.stringify(b)

/** `occurrence` is not read here — the layout builds `initial` from it and sends its instance id
    with the save — but it stays on the contract: task 6 hands the same pair to the same screen. */
export default function EventEditor({
  detail, initial, calendars, saving, error, onReload, fullScreen, onSave, onDelete, onClose,
}: EventEditorProps) {
  const { t } = useTranslation('calendar')
  const { tz, lang, region } = useCalendar()
  const [form, setForm] = useState(initial)
  const [invalid, setInvalid] = useState<string | null>(null)
  const [more, setMore] = useState(
    initial.availability !== 'Busy' || initial.visibility !== 'Default'
    || initial.url !== '' || (detail?.attendees.length ?? 0) > 0)

  const set = (patch: Partial<EventFormState>) => setForm(current => ({ ...current, ...patch }))
  const rule = ruleOf(form.repeat)
  const locked = detail != null && !detail.repeatIsExact && form.keepRepeat
  const title = detail ? t('editor.editTitle') : t('editor.newTitle')

  // The key rather than `t(key)`: a key reaching t() as a variable is invisible to the typed
  // guard and to src/locales/keys.test.ts alike.
  function messageOf(key: string): string {
    switch (key) {
      case 'editor.endBeforeStart': return t('editor.endBeforeStart')
      default: return t('errors.save')
    }
  }

  function toggleAllDay(isAllDay: boolean) {
    // De-duplicated, order kept: the other ladder folds everything it cannot express onto one
    // default, so two distinct bells would otherwise become two identical lines.
    const converted = form.reminders.map(minutes => convertReminder(minutes, isAllDay))
    set({ isAllDay, reminders: [...new Set(converted)] })
  }

  function pickRepeat(kind: RepeatChoice['kind']) {
    if (kind === 'never') return set({ repeat: { kind: 'never' } })
    if (kind !== 'custom') return set({ repeat: { kind } })
    set({ repeat: { kind: 'custom', rule: rule ?? DEFAULT_RULE } })
  }

  function submit(event: FormEvent) {
    event.preventDefault()
    const key = validate(form)
    setInvalid(key)
    if (!key) onSave(form, null)
  }

  const save = (
    <button type="submit" form={FORM_ID} className="btn btn-primary" disabled={saving}>
      {saving ? <span className="spinner" /> : t('editor.save')}
    </button>
  )
  const close = (
    <button type="button" className="modal-close" aria-label={t('editor.close')}
      onClick={() => onClose(!same(form, initial))}>✕</button>
  )

  return (
    <>
      {fullScreen ? (
        <div className="calendar-editor-head">
          <span className="modal-title">{title}</span>
          {save}
          {close}
        </div>
      ) : (
        <div className="modal-header">
          <span className="modal-title">{title}</span>
          {close}
        </div>
      )}

      <form id={FORM_ID} className="calendar-editor-form" onSubmit={submit}>
        <div className="field-h">
          <label htmlFor="event-title">{t('editor.title')}</label>
          <input id="event-title" type="text" value={form.title} autoFocus
            placeholder={t('editor.titlePlaceholder')}
            onChange={event => set({ title: event.target.value })} />
        </div>

        <div className="field-h">
          <label htmlFor="event-calendar">{t('editor.calendar')}</label>
          <span className="calendar-swatch" aria-hidden="true" style={{
            '--cal': calendars.find(one => one.id === form.calendarId)?.color,
          } as CSSProperties} />
          <select id="event-calendar" value={form.calendarId}
            onChange={event => set({ calendarId: event.target.value })}>
            {calendars.map(one => (
              <option key={one.id} value={one.id}>{one.displayName}</option>
            ))}
          </select>
        </div>

        <div className="field-h">
          <label htmlFor="event-allday">{t('editor.allDay')}</label>
          <span className="toggle-switch">
            <input id="event-allday" type="checkbox" checked={form.isAllDay}
              onChange={event => toggleAllDay(event.target.checked)} />
            <span className="toggle-track" />
          </span>
        </div>

        <div className="field-h">
          <label htmlFor="event-start-date">{t('editor.start')}</label>
          <input id="event-start-date" type="date" required aria-label={t('editor.startDate')}
            value={form.startDate}
            onChange={event => setForm(withStart(form, event.target.value, form.startTime))} />
          {!form.isAllDay && (
            <input type="time" required aria-label={t('editor.startTime')} value={form.startTime}
              onChange={event => setForm(withStart(form, form.startDate, event.target.value))} />
          )}
        </div>

        <div className="field-h">
          <label htmlFor="event-end-date">{t('editor.end')}</label>
          <input id="event-end-date" type="date" required aria-label={t('editor.endDate')}
            value={form.endDate} onChange={event => set({ endDate: event.target.value })} />
          {!form.isAllDay && (
            <input type="time" required aria-label={t('editor.endTime')} value={form.endTime}
              onChange={event => set({ endTime: event.target.value })} />
          )}
        </div>

        {form.timeZone !== tz && (
          <p className="editor-hint">{t('editor.timesIn', { zone: form.timeZone })}</p>
        )}

        <div className="field-h">
          {/* A `for` pointing at a control that is not rendered names nothing: in the locked state
              the row's label is the group's own. */}
          {locked
            ? <span className="field-h-label" id="event-repeat-label">{t('editor.repeat')}</span>
            : <label htmlFor="event-repeat">{t('editor.repeat')}</label>}
          {locked ? (
            <div className="editor-kept" role="group" aria-labelledby="event-repeat-label">
              <span>{t('editor.keptRepeat')}</span>
              <button type="button" className="btn" onClick={() => set({ keepRepeat: false })}>
                {t('editor.replaceRepeat')}
              </button>
            </div>
          ) : (
            <select id="event-repeat" value={form.repeat.kind}
              onChange={event => pickRepeat(event.target.value as RepeatChoice['kind'])}>
              {REPEATS.map(kind => (
                <option key={kind} value={kind}>{repeatLabel(kind, t)}</option>
              ))}
            </select>
          )}
        </div>

        {!locked && rule && (
          <p className="editor-hint">{recurrenceSummary(rule, t, lang, region)}</p>
        )}
        {!locked && form.repeat.kind === 'custom' && (
          <RecurrenceEditor value={form.repeat.rule} startDate={form.startDate}
            onChange={next => set({ repeat: { kind: 'custom', rule: next } })} />
        )}

        <div className="field-h">
          <span className="field-h-label">{t('editor.reminder')}</span>
          <ReminderList reminders={form.reminders} allDay={form.isAllDay}
            foreignAlarms={form.foreignAlarms}
            onChange={reminders => set({ reminders })} />
        </div>

        <div className="field-h">
          <label htmlFor="event-location">{t('editor.location')}</label>
          <input id="event-location" type="text" value={form.location}
            onChange={event => set({ location: event.target.value })} />
        </div>

        <div className="field-h">
          <label htmlFor="event-description">{t('editor.description')}</label>
          <textarea id="event-description" value={form.description}
            onChange={event => set({ description: event.target.value })} />
        </div>

        <hr className="editor-rule" />

        <button type="button" className="editor-more" aria-expanded={more}
          onClick={() => setMore(!more)}>
          {more ? <ChevronDownIcon size={14} /> : <ChevronRightIcon size={14} />}
          {t('editor.moreOptions')}
        </button>

        {more && (
          <>
            <div className="field-h">
              <span className="field-h-label">{t('editor.availability')}</span>
              <div className="seg" role="radiogroup" aria-label={t('editor.availability')}>
                {(['Busy', 'Tentative', 'Free'] as Availability[]).map(one => (
                  <label key={one}>
                    <input type="radio" name="event-availability" checked={form.availability === one}
                      onChange={() => set({ availability: one })} />
                    {availabilityLabel(one, t)}
                  </label>
                ))}
              </div>
            </div>

            <div className="field-h">
              <span className="field-h-label">{t('editor.visibility')}</span>
              <div className="seg" role="radiogroup" aria-label={t('editor.visibility')}>
                {(['Default', 'Private'] as Visibility[]).map(one => (
                  <label key={one}>
                    <input type="radio" name="event-visibility" checked={form.visibility === one}
                      onChange={() => set({ visibility: one })} />
                    {visibilityLabel(one, t)}
                  </label>
                ))}
              </div>
            </div>

            <div className="field-h">
              <label htmlFor="event-url">{t('editor.url')}</label>
              <input id="event-url" type="url" value={form.url}
                onChange={event => set({ url: event.target.value })} />
            </div>

            {detail && detail.attendees.length > 0 && (
              <div className="field-h">
                <span className="field-h-label">{t('editor.attendees')}</span>
                <div className="editor-attendees">
                  {detail.attendees.map(one => (
                    <span className="editor-attendee" key={`${one.recurrenceId ?? ''}${one.email}`}>
                      {one.isOrganizer && (
                        <span className="editor-organizer" title={t('editor.organizer')} />
                      )}
                      <span>{one.name || one.email}</span>
                      {one.partStat && <span className="editor-partstat">{one.partStat}</span>}
                    </span>
                  ))}
                  <span className="editor-hint">{t('editor.attendeesReadOnly')}</span>
                </div>
              </div>
            )}
          </>
        )}

        {(invalid || error) && (
          <div className="editor-error">
            <span>{invalid ? messageOf(invalid) : error}</span>
            {!invalid && onReload && (
              <button type="button" className="btn" onClick={onReload}>{t('errors.reload')}</button>
            )}
          </div>
        )}

        <div className="editor-actions">
          {detail && (
            <button type="button" className="btn btn-danger" onClick={() => onDelete(null)}>
              {t('editor.delete')}
            </button>
          )}
          {!fullScreen && save}
        </div>
      </form>
    </>
  )
}

// Spelled out rather than `t(`repeat.${kind}`)`: a key reaching t() as a variable is invisible to
// the typed guard and to src/locales/keys.test.ts alike. Same reason for the two below.
function repeatLabel(kind: RepeatChoice['kind'], t: TFunction<'calendar'>) {
  switch (kind) {
    case 'never': return t('repeat.never')
    case 'daily': return t('repeat.frequency.daily', { count: 1 })
    case 'weekly': return t('repeat.frequency.weekly', { count: 1 })
    case 'monthly': return t('repeat.frequency.monthly', { count: 1 })
    case 'yearly': return t('repeat.frequency.yearly', { count: 1 })
    default: return t('repeat.custom')
  }
}

function availabilityLabel(one: Availability, t: TFunction<'calendar'>) {
  switch (one) {
    case 'Busy': return t('availability.Busy')
    case 'Tentative': return t('availability.Tentative')
    default: return t('availability.Free')
  }
}

function visibilityLabel(one: Visibility, t: TFunction<'calendar'>) {
  return one === 'Private' ? t('visibility.Private') : t('visibility.Default')
}
